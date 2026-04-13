using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
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
    [Name("Dependency Analyzer/Remove Unused Dependencies")]
    [Tool("Remove Unused Dependencies", "Analyze and remove unused assembly dependencies")]
    [ToolGroup("Dependencies")]
    public class UnusedDependenciesTool : EditorTool
    {
        private List<AssemblyInfo> _assembliesToProcess = new List<AssemblyInfo>();
        private List<AssemblyInfo> _lastAnalyzedAssemblies = new List<AssemblyInfo>();
        private int _totalUnusedCount;
        private int _analyzedAssemblyCount;
        private readonly Dictionary<string, List<string>> _namespaceCache = new Dictionary<string, List<string>>();
        private bool _analysisInProgress;
        private bool _analysisPerformed;

        public List<AssemblyInfo> AssembliesToProcess => _assembliesToProcess;
        public int TotalUnusedCount => _totalUnusedCount;
        public int AnalyzedAssemblyCount => _analyzedAssemblyCount;
        public bool AnalysisPerformed => _analysisPerformed;

        protected override void OnSetupTool()
        {
        }

        public async UniTask AnalyzeSelectedAssembliesAsync()
        {
            if (_analysisInProgress)
                return;

            var analyzer = DependencyAnalyzer.Instance;
            List<AssemblyInfo> selectedAssemblies = analyzer.GetSelectedAssembliesSorted()
                .Where(assembly => DependencyAnalyzer.IsAllowedAssemblyPath(assembly.Path))
                .ToList();

            ClearTrackedUnusedDependencies();
            _analysisPerformed = true;
            _analyzedAssemblyCount = selectedAssemblies.Count;

            if (selectedAssemblies.Count == 0)
            {
                _totalUnusedCount = 0;
                _assembliesToProcess.Clear();
                return;
            }

            _analysisInProgress = true;
            _lastAnalyzedAssemblies = selectedAssemblies;

            try
            {
                EditorUtility.DisplayProgressBar("Analyzing unused dependencies", "Preparing analysis...", 0f);

                int totalUnused = 0;
                var assemblies = selectedAssemblies;
                var assembliesByName = analyzer.GetAssembliesDictionary();
                var guidToName = analyzer.GetGuidToNameMap();
                _namespaceCache.Clear();

                int totalDependencies = assemblies.Sum(assembly => assembly.Dependencies.Count);
                if (totalDependencies <= 0)
                    totalDependencies = 1;

                int processedDependencies = 0;
                int assemblyIndex = 0;

                foreach (var assembly in assemblies)
                {
                    assemblyIndex++;
                    assembly.UnusedDependencies.Clear();

                    if (!DependencyAnalyzer.IsAllowedAssemblyPath(assembly.Path))
                        continue;

                    if (assembly.Dependencies.Count == 0)
                        continue;

                    List<string> csFiles = AssemblyFileUtility.GetCSharpFilesForAssembly(assembly.Path);

                    foreach (string depGuid in assembly.Dependencies)
                    {
                        processedDependencies++;
                        float progress = Mathf.Clamp01(processedDependencies / (float)totalDependencies);
                        EditorUtility.DisplayProgressBar(
                            "Analyzing unused dependencies",
                            $"{assembly.Name} ({assemblyIndex}/{assemblies.Count})",
                            progress);

                        if (!guidToName.ContainsKey(depGuid))
                            continue;

                        List<string> namespaces = GetDependencyNamespaces(depGuid, guidToName, assembliesByName);
                        if (namespaces.Count == 0)
                            continue;

                        bool isUsed = NamespaceUtility.IsNamespaceUsedInFiles(csFiles, namespaces);

                        if (!isUsed)
                        {
                            assembly.UnusedDependencies.Add(depGuid);
                            totalUnused++;
                        }

                        if (processedDependencies % 25 == 0)
                            await UniTask.Yield(PlayerLoopTiming.Update);
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                _assembliesToProcess = assemblies
                    .Where(a => a.UnusedDependencies.Count > 0 && DependencyAnalyzer.IsAllowedAssemblyPath(a.Path))
                    .ToList();
                _totalUnusedCount = totalUnused;

                Debug.Log($"Analysis complete. Found {totalUnused} unused dependencies across {_assembliesToProcess.Count} assemblies");
            }
            finally
            {
                _analysisInProgress = false;
                EditorUtility.ClearProgressBar();
            }
        }

        public void RemoveUnusedDependencies()
        {
            if (_assembliesToProcess.Count == 0)
            {
                EditorUtility.DisplayDialog("No Unused Dependencies", "No unused dependencies found.", "OK");
                return;
            }

            string message = $"Found {_totalUnusedCount} unused dependencies in {_assembliesToProcess.Count} assembly(ies).\n\nRemove them? This action cannot be undone (except through version control).";

            if (!EditorUtility.DisplayDialog("Remove Unused Dependencies", message, "Remove", "Cancel"))
            {
                return;
            }

            EditorUtility.DisplayProgressBar("Removing", "Removing unused dependencies...", 0.5f);

            try
            {
                int removedCount = 0;

                foreach (var assembly in _assembliesToProcess)
                {
                    if (assembly.UnusedDependencies.Count == 0)
                        continue;

                    try
                    {
                        string asmdefPath = assembly.Path;

                        if (!DependencyAnalyzer.IsAllowedAssemblyPath(asmdefPath))
                            continue;

                        string json = File.ReadAllText(asmdefPath);
                        AssemblyDefinitionData asmdefData = JsonUtility.FromJson<AssemblyDefinitionData>(json);

                        var originalCount = asmdefData.references.Count;
                        var newReferences = new List<string>();

                        foreach (string reference in asmdefData.references)
                        {
                            string refGuid = reference.StartsWith("GUID:") ? reference.Substring(5) : reference;

                            if (!assembly.UnusedDependencies.Contains(refGuid))
                            {
                                newReferences.Add(reference);
                            }
                        }

                        if (newReferences.Count < originalCount)
                        {
                            asmdefData.references = newReferences;
                            string newJson = JsonUtility.ToJson(asmdefData, true);
                            File.WriteAllText(asmdefPath, newJson);

                            removedCount += (originalCount - newReferences.Count);
                            Debug.Log($"Updated {assembly.Name}: removed {originalCount - newReferences.Count} dependencies");
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Failed to update {assembly.Name}: {e.Message}");
                    }
                }

                if (removedCount > 0)
                {
                    AssetDatabase.Refresh();
                }

                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Complete", $"Removed {removedCount} unused dependencies from {_assembliesToProcess.Count} assembly(ies).", "OK");

                var analyzer = DependencyAnalyzer.Instance;
                analyzer.BuildAssemblyDatabase();
                ClearTrackedUnusedDependencies();
                _analysisPerformed = true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void ClearTrackedUnusedDependencies()
        {
            foreach (AssemblyInfo assembly in _lastAnalyzedAssemblies)
            {
                assembly.UnusedDependencies.Clear();
            }

            _lastAnalyzedAssemblies.Clear();
            _assembliesToProcess.Clear();
            _totalUnusedCount = 0;
        }

        private List<string> GetDependencyNamespaces(
            string depGuid,
            Dictionary<string, string> guidToName,
            Dictionary<string, AssemblyInfo> assembliesByName)
        {
            if (_namespaceCache.TryGetValue(depGuid, out List<string> cached))
                return cached;

            var namespaces = new HashSet<string>();

            if (guidToName.TryGetValue(depGuid, out string depName) && assembliesByName.TryGetValue(depName, out AssemblyInfo depAssembly))
            {
                if (!string.IsNullOrWhiteSpace(depAssembly.RootNamespace))
                {
                    namespaces.Add(depAssembly.RootNamespace);
                }

                if (DependencyAnalyzer.IsAllowedAssemblyPath(depAssembly.Path))
                {
                    List<string> depFiles = AssemblyFileUtility.GetCSharpFilesForAssembly(depAssembly.Path);
                    namespaces.UnionWith(NamespaceUtility.GetNamespacesDeclaredInFiles(depFiles));
                }
            }

            List<string> result = namespaces.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            _namespaceCache[depGuid] = result;
            return result;
        }
    }
}
