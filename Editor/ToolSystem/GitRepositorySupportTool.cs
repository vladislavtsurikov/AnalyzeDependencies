using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core.Models;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core.Utilities;
using VladislavTsurikov.ReflectionUtility;
using VladislavTsurikov.ToolSystem.Runtime.Core;
using VladislavTsurikov.ToolSystem.Runtime.Core.Attributes;

namespace VladislavTsurikov.AnalyzeDependencies.Editor.ToolSystem
{
    [Name("Dependency Analyzer/Git Repository Support")]
    [Tool("Git Repository Support", "Initialize git repositories for selected asmdefs")]
    [ToolGroup("Dependencies")]
    public class GitRepositorySupportTool : EditorTool
    {
        protected override void OnSetupTool()
        {
        }

        public void PrepareRepositories()
        {
            ExecuteForSelectedAssemblies(
                "Prepare Git Repositories",
                "Git Repository Support",
                "Initialized",
                repositoryRoot =>
                {
                    if (GitRepositoryUtility.HasGitRepository(repositoryRoot))
                        return OperationResult.Skipped(".git already exists.");

                    string output = GitRepositoryUtility.InitializeRepository(repositoryRoot);
                    return OperationResult.Success(output);
                });
        }

        public void RegisterSubmodules()
        {
            List<AssemblyInfo> assemblies = DependencyAnalyzerInitialize.Instance.GetSelectedAssembliesSorted();
            if (assemblies.Count == 0)
            {
                EditorUtility.DisplayDialog("No asmdefs selected", "Select at least one asmdef in the shared dependency selection block.", "OK");
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                EditorUtility.DisplayDialog("Git Repository Support", "Unable to resolve Unity project root.", "OK");
                return;
            }

            string rootRepositoryPath;
            try
            {
                rootRepositoryPath = GitRepositoryUtility.GetRepositoryRoot(projectRoot);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Git Repository Support", $"Root git repository was not found: {exception.Message}", "OK");
                return;
            }

            int registeredCount = 0;
            int skippedCount = 0;
            int failedCount = 0;
            var summary = new StringBuilder();
            var repositoryRoots = new List<string>();

            foreach (AssemblyInfo assembly in assemblies)
            {
                string repositoryRoot = PackageJsonUtility.GetPackageRoot(assembly.Path);
                if (!GitRepositoryUtility.HasGitRepository(repositoryRoot))
                {
                    skippedCount++;
                    summary.AppendLine($"Skipped {assembly.Name}: .git was not found in the asmdef folder.");
                    continue;
                }

                repositoryRoots.Add(repositoryRoot);
            }

            try
            {
                EditorUtility.DisplayProgressBar("Register Git Submodules", "Preparing selected repositories...", 0.25f);

                GitRepositoryUtility.BatchRegistrationResult result = GitRepositoryUtility.RegisterSubmodules(rootRepositoryPath, repositoryRoots);
                registeredCount = result.Messages.Count;
                foreach (string message in result.Messages)
                {
                    summary.AppendLine(message);
                }
            }
            catch (Exception exception)
            {
                failedCount = repositoryRoots.Count;
                summary.AppendLine(exception.Message);
                Debug.LogError($"[AnalyzeDependencies][GitRepositorySupport] Batch submodule registration failed: {exception}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Git Submodule Registration",
                $"Registered: {registeredCount}\nSkipped: {skippedCount}\nFailed: {failedCount}\n\n{summary.ToString().Trim()}",
                "OK");
        }

        public void RemoveGitMarkers()
        {
            if (!EditorUtility.DisplayDialog(
                    "Remove .git",
                    "This will delete the .git entry inside each selected asmdef folder.\n\nUse this before exporting to Asset Store if you do not want nested git repositories in the package.",
                    "Remove",
                    "Cancel"))
            {
                return;
            }

            ExecuteForSelectedAssemblies(
                "Remove .git",
                "Remove .git",
                "Removed",
                repositoryRoot =>
                {
                    if (!GitRepositoryUtility.HasGitRepository(repositoryRoot))
                        return OperationResult.Skipped(".git was not found.");

                    GitRepositoryUtility.RemoveGitRepositoryMarker(repositoryRoot);
                    return OperationResult.Success(".git removed.");
                });
        }

        private void ExecuteForSelectedAssemblies(
            string progressTitle,
            string dialogTitle,
            string successLabel,
            Func<string, OperationResult> operation)
        {
            List<AssemblyInfo> assemblies = DependencyAnalyzerInitialize.Instance.GetSelectedAssembliesSorted();
            if (assemblies.Count == 0)
            {
                EditorUtility.DisplayDialog("No asmdefs selected", "Select at least one asmdef in the shared dependency selection block.", "OK");
                return;
            }

            int initializedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;
            var summary = new StringBuilder();

            try
            {
                for (int i = 0; i < assemblies.Count; i++)
                {
                    AssemblyInfo assembly = assemblies[i];
                    string repositoryRoot = PackageJsonUtility.GetPackageRoot(assembly.Path);

                    EditorUtility.DisplayProgressBar(progressTitle, $"Processing {assembly.Name}", (i + 1f) / assemblies.Count);

                    try
                    {
                        OperationResult result = operation(repositoryRoot);
                        if (result.State == OperationState.Skipped)
                        {
                            skippedCount++;
                            summary.AppendLine($"Skipped {assembly.Name}: {result.Message}");
                            continue;
                        }

                        if (result.State == OperationState.Failed)
                        {
                            failedCount++;
                            summary.AppendLine($"Failed {assembly.Name}: {result.Message}");
                            continue;
                        }

                        initializedCount++;
                        summary.AppendLine($"{successLabel} {assembly.Name}: {result.Message}");
                    }
                    catch (Exception exception)
                    {
                        failedCount++;
                        summary.AppendLine($"Failed {assembly.Name}: {exception.Message}");
                        Debug.LogError($"[AnalyzeDependencies][GitRepositorySupport] Failed for {assembly.Name}: {exception}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                dialogTitle,
                $"Initialized: {initializedCount}\nSkipped: {skippedCount}\nFailed: {failedCount}\n\n{summary.ToString().Trim()}",
                "OK");
        }

        private readonly struct OperationResult
        {
            public readonly OperationState State;
            public readonly string Message;

            private OperationResult(OperationState state, string message)
            {
                State = state;
                Message = message;
            }

            public static OperationResult Success(string message) => new OperationResult(OperationState.Success, message);
            public static OperationResult Skipped(string message) => new OperationResult(OperationState.Skipped, message);
        }

        private enum OperationState
        {
            Success,
            Skipped,
            Failed
        }
    }
}
