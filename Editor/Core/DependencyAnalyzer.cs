using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core.Models;
using VladislavTsurikov.Utility.Runtime;

namespace VladislavTsurikov.AnalyzeDependencies.Editor.Core
{
    public sealed class DependencyAnalyzer : DataTypeSingleton<DependencyAnalyzer>
    {
        private readonly Dictionary<string, string> _guidToName = new Dictionary<string, string>();
        private readonly Dictionary<string, AssemblyInfo> _assemblies = new Dictionary<string, AssemblyInfo>();
        private static readonly string[] AllowedRoots = { "Assets/", "Packages/" };
        private static readonly IComparer<AssemblyInfo> AssemblySortComparer = Comparer<AssemblyInfo>.Create(CompareAssemblies);

        public DependencyAnalyzer()
        {
            BuildAssemblyDatabase();
        }

        public List<AssemblyInfo> GetAllAssemblies() => _assemblies.Values.ToList();
        public List<AssemblyInfo> GetAllAssembliesSorted() => _assemblies.Values.OrderBy(assembly => assembly, AssemblySortComparer).ToList();

        public List<AssemblyInfo> GetSelectedAssemblies() => _assemblies.Values.Where(a => a.IsSelected).ToList();
        public List<AssemblyInfo> GetSelectedAssembliesSorted() => _assemblies.Values.Where(a => a.IsSelected).OrderBy(assembly => assembly, AssemblySortComparer).ToList();

        public void SelectAll()
        {
            foreach (var assembly in _assemblies.Values)
            {
                assembly.IsSelected = true;
            }
        }

        public void DeselectAll()
        {
            foreach (var assembly in _assemblies.Values)
            {
                assembly.IsSelected = false;
            }
        }

        public void ClearSelection()
        {
            DeselectAll();
        }

        public void SelectAssemblies(List<AssemblyInfo> assemblies)
        {
            foreach (var assembly in assemblies)
            {
                assembly.IsSelected = true;
            }
        }

        public void DeselectAssemblies(List<AssemblyInfo> assemblies)
        {
            foreach (var assembly in assemblies)
            {
                assembly.IsSelected = false;
            }
        }

        public void SetAssemblySelection(AssemblyInfo assembly, bool isSelected)
        {
            if (assembly == null)
                return;

            assembly.IsSelected = isSelected;
        }

        public void SetAssemblySelection(string assemblyName, bool isSelected)
        {
            if (string.IsNullOrEmpty(assemblyName))
                return;

            if (_assemblies.TryGetValue(assemblyName, out AssemblyInfo assembly))
            {
                assembly.IsSelected = isSelected;
            }
        }

        public void SelectAllFiltered(IEnumerable<AssemblyInfo> assemblies)
        {
            if (assemblies == null)
                return;

            foreach (AssemblyInfo assembly in assemblies)
            {
                assembly.IsSelected = true;
            }
        }

        public void SetSelectionRange(IReadOnlyList<AssemblyInfo> visibleAssemblies, int startIndex, int endIndex, bool isSelected)
        {
            if (visibleAssemblies == null || visibleAssemblies.Count == 0)
                return;

            int minIndex = Mathf.Clamp(Mathf.Min(startIndex, endIndex), 0, visibleAssemblies.Count - 1);
            int maxIndex = Mathf.Clamp(Mathf.Max(startIndex, endIndex), 0, visibleAssemblies.Count - 1);

            for (int i = minIndex; i <= maxIndex; i++)
            {
                visibleAssemblies[i].IsSelected = isSelected;
            }
        }

        public Dictionary<string, AssemblyInfo> GetAssembliesDictionary() => _assemblies;

        public Dictionary<string, string> GetGuidToNameMap() => _guidToName;

        public void BuildAssemblyDatabase()
        {
            var selectedAssemblyNames = _assemblies.Values
                .Where(assembly => assembly.IsSelected)
                .Select(assembly => assembly.Name)
                .ToHashSet();

            _guidToName.Clear();
            _assemblies.Clear();

            string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");

            foreach (string guid in asmdefGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (!IsAllowedAssemblyPath(path))
                    continue;

                try
                {
                    string json = File.ReadAllText(path);
                    AssemblyDefinitionData asmdefData = JsonUtility.FromJson<AssemblyDefinitionData>(json);

                    if (string.IsNullOrEmpty(asmdefData.name))
                        continue;

                    _guidToName[guid] = asmdefData.name;

                    var assemblyInfo = new AssemblyInfo
                    {
                        Name = asmdefData.name,
                        Guid = guid,
                        Path = path,
                        RootNamespace = asmdefData.rootNamespace,
                        IsSelected = selectedAssemblyNames.Contains(asmdefData.name)
                    };

                    if (asmdefData.references != null)
                    {
                        foreach (string reference in asmdefData.references)
                        {
                            string refGuid = reference.StartsWith("GUID:") ? reference.Substring(5) : reference;
                            assemblyInfo.Dependencies.Add(refGuid);
                        }
                    }

                    _assemblies[asmdefData.name] = assemblyInfo;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Failed to parse {path}: {e.Message}");
                }
            }
        }

        public string GetAssemblyNameByGuid(string guid) => _guidToName.ContainsKey(guid) ? _guidToName[guid] : guid;

        internal static bool IsAllowedAssemblyPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string normalized = path.Replace('\\', '/');
            bool hasAllowedRoot = false;

            foreach (string root in AllowedRoots)
            {
                if (normalized.StartsWith(root, System.StringComparison.OrdinalIgnoreCase))
                {
                    hasAllowedRoot = true;
                    break;
                }
            }

            if (!hasAllowedRoot)
                return false;

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return false;

            string fullPath = Path.GetFullPath(Path.Combine(projectRoot, normalized));
            return File.Exists(fullPath);
        }

        private static int CompareAssemblies(AssemblyInfo left, AssemblyInfo right)
        {
            if (ReferenceEquals(left, right))
                return 0;

            if (left == null)
                return 1;

            if (right == null)
                return -1;

            int groupCompare = GetPathSortGroup(left.Path).CompareTo(GetPathSortGroup(right.Path));
            if (groupCompare != 0)
                return groupCompare;

            return string.Compare(left.Name, right.Name, System.StringComparison.OrdinalIgnoreCase);
        }

        private static int GetPathSortGroup(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return 2;

            string normalized = path.Replace('\\', '/');
            if (normalized.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                return 0;

            if (normalized.StartsWith("Packages/", System.StringComparison.OrdinalIgnoreCase))
                return 1;

            return 2;
        }
    }
}
