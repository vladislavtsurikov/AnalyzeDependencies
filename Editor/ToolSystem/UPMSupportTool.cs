using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core.Models;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core.Utilities;
using VladislavTsurikov.ReflectionUtility;
using VladislavTsurikov.ToolSystem.Runtime.Core;
using VladislavTsurikov.ToolSystem.Runtime.Core.Attributes;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace VladislavTsurikov.AnalyzeDependencies.Editor.ToolSystem
{
    [Name("Dependency Analyzer/UPM Support")]
    [Tool("UPM Support", "Create package.json files and sync package dependencies for selected asmdefs")]
    [ToolGroup("Dependencies")]
    public class UPMSupportTool : EditorTool
    {
        protected override void OnSetupTool()
        {
        }

        public void CreateUpmSupport()
        {
            List<AssemblyInfo> assemblies = DependencyAnalyzerInitialize.Instance.GetSelectedAssembliesSorted();
            if (assemblies.Count == 0)
            {
                EditorUtility.DisplayDialog("No asmdefs selected", "Select at least one asmdef in the shared dependency selection block.", "OK");
                return;
            }

            int createdCount = 0;
            int skippedCount = 0;
            int failedCount = 0;
            var summary = new StringBuilder();

            try
            {
                for (int i = 0; i < assemblies.Count; i++)
                {
                    AssemblyInfo assembly = assemblies[i];
                    string packageRoot = PackageJsonUtility.GetPackageRoot(assembly.Path);
                    string packageJsonPath = PackageJsonUtility.GetPackageJsonPath(packageRoot);

                    EditorUtility.DisplayProgressBar("Create UPM Support", $"Processing {assembly.Name}", (i + 1f) / assemblies.Count);

                    if (File.Exists(packageJsonPath))
                    {
                        skippedCount++;
                        summary.AppendLine($"Skipped {assembly.Name}: package.json already exists.");
                        continue;
                    }

                    try
                    {
                        File.WriteAllText(packageJsonPath, PackageJsonUtility.CreateMinimalPackageJson(assembly.Name), Encoding.UTF8);
                        createdCount++;
                        summary.AppendLine($"Created UPM support for {assembly.Name}.");
                    }
                    catch (Exception exception)
                    {
                        failedCount++;
                        summary.AppendLine($"Failed {assembly.Name}: {exception.Message}");
                        Debug.LogError($"[AnalyzeDependencies][UPMSupport] Failed to create package.json for {assembly.Name}: {exception}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (createdCount > 0)
            {
                AssetDatabase.Refresh();
            }

            EditorUtility.DisplayDialog(
                "UPM Support",
                $"Created: {createdCount}\nSkipped: {skippedCount}\nFailed: {failedCount}\n\n{summary.ToString().Trim()}",
                "OK");
        }

        public void SyncDependencies()
        {
            List<AssemblyInfo> assemblies = DependencyAnalyzerInitialize.Instance.GetSelectedAssembliesSorted();
            if (assemblies.Count == 0)
            {
                EditorUtility.DisplayDialog("No asmdefs selected", "Select at least one asmdef in the shared dependency selection block.", "OK");
                return;
            }

            int updatedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;
            int unresolvedCount = 0;
            var summary = new StringBuilder();

            try
            {
                for (int i = 0; i < assemblies.Count; i++)
                {
                    AssemblyInfo assembly = assemblies[i];
                    string packageRoot = PackageJsonUtility.GetPackageRoot(assembly.Path);
                    string packageJsonPath = PackageJsonUtility.GetPackageJsonPath(packageRoot);

                    EditorUtility.DisplayProgressBar("Sync UPM Dependencies", $"Syncing {assembly.Name}", (i + 1f) / assemblies.Count);

                    if (!File.Exists(packageJsonPath))
                    {
                        skippedCount++;
                        summary.AppendLine($"Skipped {assembly.Name}: package.json is missing.");
                        continue;
                    }

                    try
                    {
                        AssemblyDefinitionData asmdefData = LoadAssemblyDefinition(assembly.Path);
                        var dependencies = new SortedDictionary<string, string>(StringComparer.Ordinal);
                        var unresolvedReferences = new List<string>();

                        foreach (string reference in asmdefData.references ?? new List<string>())
                        {
                            if (TryResolvePackageDependency(reference, assembly, asmdefData, out string packageId, out string version))
                            {
                                if (string.IsNullOrEmpty(packageId))
                                    continue;

                                if (dependencies.TryGetValue(packageId, out string existingVersion))
                                {
                                    if (existingVersion == "1.0.0" && !string.IsNullOrWhiteSpace(version))
                                    {
                                        dependencies[packageId] = version;
                                    }
                                }
                                else
                                {
                                    dependencies[packageId] = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version;
                                }
                            }
                            else
                            {
                                unresolvedReferences.Add(reference);
                            }
                        }

                        string originalJson = File.ReadAllText(packageJsonPath);
                        string updatedJson = PackageJsonUtility.UpsertDependencies(originalJson, dependencies);

                        if (!string.Equals(originalJson, updatedJson, StringComparison.Ordinal))
                        {
                            File.WriteAllText(packageJsonPath, updatedJson, Encoding.UTF8);
                            updatedCount++;
                        }

                        if (unresolvedReferences.Count > 0)
                        {
                            unresolvedCount += unresolvedReferences.Count;
                            summary.AppendLine($"Synced {assembly.Name} with {dependencies.Count} dependencies. Unresolved: {string.Join(", ", unresolvedReferences.Take(5))}{(unresolvedReferences.Count > 5 ? "..." : string.Empty)}");
                        }
                        else
                        {
                            summary.AppendLine($"Synced {assembly.Name} with {dependencies.Count} dependencies.");
                        }
                    }
                    catch (Exception exception)
                    {
                        failedCount++;
                        summary.AppendLine($"Failed {assembly.Name}: {exception.Message}");
                        Debug.LogError($"[AnalyzeDependencies][UPMSupport] Failed to sync dependencies for {assembly.Name}: {exception}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (updatedCount > 0)
            {
                AssetDatabase.Refresh();
            }

            EditorUtility.DisplayDialog(
                "UPM Dependency Sync",
                $"Updated: {updatedCount}\nSkipped: {skippedCount}\nFailed: {failedCount}\nUnresolved references: {unresolvedCount}\n\n{summary.ToString().Trim()}",
                "OK");
        }

        public void GenerateGitManifestSnippet()
        {
            List<AssemblyInfo> assemblies = DependencyAnalyzerInitialize.Instance.GetSelectedAssembliesSorted();
            if (assemblies.Count == 0)
            {
                EditorUtility.DisplayDialog("No asmdefs selected", "Select at least one asmdef in the shared dependency selection block.", "OK");
                return;
            }

            try
            {
                Dictionary<string, LocalPackageInfo> packagesById = BuildLocalPackageMap();
                var internalDependencies = new SortedDictionary<string, string>(StringComparer.Ordinal);
                var externalDependencies = new SortedDictionary<string, string>(StringComparer.Ordinal);
                var unresolvedPackages = new List<string>();
                var queue = new Queue<string>();
                var visited = new HashSet<string>(StringComparer.Ordinal);

                foreach (AssemblyInfo assembly in assemblies)
                {
                    string packageRoot = PackageJsonUtility.GetPackageRoot(assembly.Path);
                    string packageJsonPath = PackageJsonUtility.GetPackageJsonPath(packageRoot);

                    if (!File.Exists(packageJsonPath) ||
                        !PackageJsonUtility.TryReadPackageMetadata(packageJsonPath, out string packageId, out _))
                    {
                        unresolvedPackages.Add($"{assembly.Name} (package.json missing or invalid)");
                        continue;
                    }

                    queue.Enqueue(packageId);
                }

                while (queue.Count > 0)
                {
                    string packageId = queue.Dequeue();
                    if (!visited.Add(packageId))
                        continue;

                    if (!packagesById.TryGetValue(packageId, out LocalPackageInfo packageInfo))
                    {
                        unresolvedPackages.Add(packageId);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(packageInfo.RemoteUrl))
                    {
                        internalDependencies[packageId] = BuildGitDependencyValue(packageInfo.RemoteUrl, packageInfo.Branch);
                    }
                    else
                    {
                        unresolvedPackages.Add($"{packageId} (git remote missing)");
                    }

                    foreach (KeyValuePair<string, string> dependency in packageInfo.Dependencies)
                    {
                        if (dependency.Key.StartsWith("com.vladislavtsurikov.", StringComparison.Ordinal))
                        {
                            queue.Enqueue(dependency.Key);
                        }
                        else if (!string.IsNullOrWhiteSpace(dependency.Value))
                        {
                            externalDependencies[dependency.Key] = dependency.Value;
                        }
                    }
                }

                var snippetDependencies = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> dependency in externalDependencies)
                {
                    snippetDependencies[dependency.Key] = dependency.Value;
                }

                foreach (KeyValuePair<string, string> dependency in internalDependencies)
                {
                    snippetDependencies[dependency.Key] = dependency.Value;
                }

                string snippet = BuildManifestSnippet(snippetDependencies);
                EditorGUIUtility.systemCopyBuffer = snippet;

                string summary = $"Copied manifest.json snippet to clipboard.\n\nPackages: {internalDependencies.Count}\nExternal dependencies: {externalDependencies.Count}";
                if (unresolvedPackages.Count > 0)
                {
                    summary += $"\nUnresolved: {unresolvedPackages.Count}\n\n{string.Join("\n", unresolvedPackages.Take(10))}";
                    if (unresolvedPackages.Count > 10)
                    {
                        summary += "\n...";
                    }
                }
                else
                {
                    summary += "\nUnresolved: 0";
                }

                Debug.Log($"[AnalyzeDependencies][UPMSupport] Generated manifest snippet:\n{snippet}");
                EditorUtility.DisplayDialog("Git Manifest Snippet", summary, "OK");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AnalyzeDependencies][UPMSupport] Failed to generate manifest snippet: {exception}");
                EditorUtility.DisplayDialog("Git Manifest Snippet", exception.Message, "OK");
            }
        }

        private static AssemblyDefinitionData LoadAssemblyDefinition(string asmdefPath)
        {
            string json = File.ReadAllText(asmdefPath);
            AssemblyDefinitionData asmdefData = JsonUtility.FromJson<AssemblyDefinitionData>(json);
            if (asmdefData.references == null)
                asmdefData.references = new List<string>();

            if (asmdefData.versionDefines == null)
                asmdefData.versionDefines = new List<VersionDefine>();

            return asmdefData;
        }

        private bool TryResolvePackageDependency(
            string reference,
            AssemblyInfo ownerAssembly,
            AssemblyDefinitionData ownerAsmdef,
            out string packageId,
            out string version)
        {
            packageId = null;
            version = "1.0.0";

            if (string.IsNullOrWhiteSpace(reference))
                return false;

            string normalizedReference = reference.StartsWith("GUID:", StringComparison.Ordinal)
                ? reference.Substring(5)
                : reference;

            DependencyAnalyzer analyzer = DependencyAnalyzerInitialize.Instance;
            Dictionary<string, string> guidToName = analyzer.GetGuidToNameMap();
            Dictionary<string, AssemblyInfo> assembliesByName = analyzer.GetAssembliesDictionary();

            if (reference.StartsWith("GUID:", StringComparison.Ordinal))
            {
                if (!guidToName.TryGetValue(normalizedReference, out string assemblyName))
                    return false;

                if (!assembliesByName.TryGetValue(assemblyName, out AssemblyInfo assembly))
                    return false;

                return TryResolveAssemblyPackage(assembly, ownerAssembly, out packageId, out version);
            }

            if (assembliesByName.TryGetValue(reference, out AssemblyInfo referencedAssembly))
            {
                return TryResolveAssemblyPackage(referencedAssembly, ownerAssembly, out packageId, out version);
            }

            if (reference.StartsWith("UnityEngine.", StringComparison.Ordinal) ||
                reference.StartsWith("UnityEditor.", StringComparison.Ordinal))
            {
                return false;
            }

            return TryResolveFromVersionDefines(ownerAsmdef.versionDefines, reference, out packageId, out version);
        }

        private static bool TryResolveAssemblyPackage(
            AssemblyInfo referencedAssembly,
            AssemblyInfo ownerAssembly,
            out string packageId,
            out string version)
        {
            packageId = null;
            version = "1.0.0";

            if (referencedAssembly == null || referencedAssembly == ownerAssembly)
                return false;

            if (PackageJsonUtility.TryFindNearestPackageJson(referencedAssembly.Path, out string packageJsonPath) &&
                PackageJsonUtility.TryReadPackageMetadata(packageJsonPath, out string existingPackageName, out string existingVersion))
            {
                packageId = existingPackageName;
                version = string.IsNullOrWhiteSpace(existingVersion) ? "1.0.0" : existingVersion;
                return true;
            }

            if (referencedAssembly.Path.IndexOf("Assets/VladislavTsurikov", StringComparison.OrdinalIgnoreCase) >= 0 ||
                referencedAssembly.Name.StartsWith("VladislavTsurikov.", StringComparison.Ordinal))
            {
                packageId = PackageJsonUtility.BuildPackageName(referencedAssembly.Name);
                version = "1.0.0";
                return true;
            }

            return false;
        }

        private static bool TryResolveFromVersionDefines(
            List<VersionDefine> versionDefines,
            string referenceName,
            out string packageId,
            out string version)
        {
            packageId = null;
            version = "1.0.0";

            if (versionDefines == null || versionDefines.Count == 0)
                return false;

            string normalizedReference = PackageJsonUtility.NormalizeForComparison(referenceName);
            if (string.IsNullOrEmpty(normalizedReference))
                return false;

            List<VersionDefine> candidates = versionDefines
                .Where(define => !string.IsNullOrWhiteSpace(define.name) &&
                                 define.name.StartsWith("com.", StringComparison.OrdinalIgnoreCase) &&
                                 IsVersionDefineMatch(normalizedReference, define.name))
                .ToList();

            if (candidates.Count != 1)
                return false;

            VersionDefine candidate = candidates[0];
            packageId = candidate.name;
            version = string.IsNullOrWhiteSpace(candidate.expression) ? "1.0.0" : candidate.expression;
            return true;
        }

        private static bool IsVersionDefineMatch(string normalizedReference, string packageName)
        {
            string normalizedPackage = PackageJsonUtility.NormalizeForComparison(packageName);
            if (string.IsNullOrEmpty(normalizedPackage))
                return false;

            if (normalizedPackage == normalizedReference ||
                normalizedPackage.Contains(normalizedReference) ||
                normalizedReference.Contains(normalizedPackage))
            {
                return true;
            }

            string[] referenceTokens = normalizedReference.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] packageTokens = normalizedPackage.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (referenceTokens.Length == 0 || packageTokens.Length == 0)
                return false;

            return referenceTokens.Last() == packageTokens.Last();
        }

        private static Dictionary<string, LocalPackageInfo> BuildLocalPackageMap()
        {
            var packagesById = new Dictionary<string, LocalPackageInfo>(StringComparer.Ordinal);
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return packagesById;

            string assetsRoot = Path.Combine(projectRoot, "Assets", "VladislavTsurikov");
            if (!Directory.Exists(assetsRoot))
                return packagesById;

            foreach (string packageJsonPath in Directory.GetFiles(assetsRoot, "package.json", SearchOption.AllDirectories))
            {
                if (!PackageJsonUtility.TryReadPackageMetadata(packageJsonPath, out string packageId, out string version))
                    continue;

                PackageJsonUtility.TryReadPackageDependencies(packageJsonPath, out Dictionary<string, string> dependencies);

                string packageRoot = Path.GetDirectoryName(packageJsonPath);
                packagesById[packageId] = new LocalPackageInfo
                {
                    PackageId = packageId,
                    Version = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version,
                    PackageRoot = packageRoot,
                    RemoteUrl = TryGetGitOutput(packageRoot, "remote get-url origin"),
                    Branch = GetPreferredBranch(packageRoot),
                    Dependencies = dependencies ?? new Dictionary<string, string>(StringComparer.Ordinal)
                };
            }

            return packagesById;
        }

        private static string GetPreferredBranch(string workingDirectory)
        {
            string branch = TryGetGitOutput(workingDirectory, "rev-parse --abbrev-ref HEAD");
            return string.IsNullOrWhiteSpace(branch) ? "master" : branch;
        }

        private static string BuildGitDependencyValue(string remoteUrl, string branch)
        {
            if (string.IsNullOrWhiteSpace(remoteUrl))
                return string.Empty;

            string normalizedUrl = remoteUrl.Trim();
            if (normalizedUrl.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            {
                normalizedUrl = "https://github.com/" + normalizedUrl.Substring("git@github.com:".Length);
            }

            return string.IsNullOrWhiteSpace(branch)
                ? normalizedUrl
                : $"{normalizedUrl}#{branch}";
        }

        private static string BuildManifestSnippet(IReadOnlyDictionary<string, string> dependencies)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"dependencies\": {");

            int index = 0;
            foreach (KeyValuePair<string, string> dependency in dependencies)
            {
                bool isLast = index == dependencies.Count - 1;
                sb.AppendLine($"    \"{dependency.Key}\": \"{dependency.Value}\"{(isLast ? string.Empty : ",")}");
                index++;
            }

            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string TryGetGitOutput(string workingDirectory, string arguments)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
                return null;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return null;

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                return process.ExitCode == 0 ? output : null;
            }
            catch
            {
                return null;
            }
        }

        private sealed class LocalPackageInfo
        {
            public string PackageId;
            public string Version;
            public string PackageRoot;
            public string RemoteUrl;
            public string Branch;
            public Dictionary<string, string> Dependencies;
        }
    }
}
