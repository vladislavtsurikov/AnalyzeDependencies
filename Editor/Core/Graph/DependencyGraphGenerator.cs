using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core.Models;
using VladislavTsurikov.Core.Runtime;

namespace VladislavTsurikov.AnalyzeDependencies.Editor.Core.Graph
{
    public class DependencyGraphGenerator
    {
        private const string TemplateFileName = "DependencyGraphTemplate.html";

        private readonly DependencyAnalyzer _analyzer;
        private readonly HashSet<string> _includedAssemblyNames;
        private DependencyGraph _graph;

        public DependencyGraphGenerator(DependencyAnalyzer analyzer, IEnumerable<AssemblyInfo> assemblies = null)
        {
            _analyzer = analyzer;
            _includedAssemblyNames = assemblies != null
                ? new HashSet<string>(assemblies.Select(assembly => assembly.Name))
                : null;
        }

        public DependencyGraph BuildGraph()
        {
            _graph = new DependencyGraph();
            Dictionary<string, AssemblyInfo> assembliesDict = _analyzer.GetAssembliesDictionary();
            List<AssemblyInfo> assemblies = (_includedAssemblyNames == null || _includedAssemblyNames.Count == 0
                    ? assembliesDict.Values
                    : assembliesDict.Values.Where(assembly => _includedAssemblyNames.Contains(assembly.Name)))
                .ToList();
            HashSet<string> includedGuids = new HashSet<string>(assemblies.Select(assembly => assembly.Guid));

            foreach (AssemblyInfo assembly in assemblies)
            {
                assembly.UsedBy.Clear();
                assembly.Centrality = 0;
                _graph.Nodes.Add(assembly);
            }

            foreach (AssemblyInfo assembly in assemblies)
            {
                foreach (string depGuid in assembly.Dependencies)
                {
                    if (!includedGuids.Contains(depGuid))
                        continue;

                    if (!assembliesDict.TryGetValue(_analyzer.GetAssemblyNameByGuid(depGuid), out AssemblyInfo depAssembly))
                        continue;

                    depAssembly.UsedBy.Add(assembly.Guid);

                    _graph.Edges.Add(new DependencyEdge
                    {
                        FromGuid = assembly.Guid,
                        ToGuid = depGuid,
                        FromName = assembly.Name,
                        ToName = depAssembly.Name,
                        IsUnused = assembly.UnusedDependencies.Contains(depGuid)
                    });

                    if (assembly.UnusedDependencies.Contains(depGuid))
                        _graph.UnusedDependencies++;
                }
            }

            CalculateCentrality();
            _graph.TotalAssemblies = _graph.Nodes.Count;
            _graph.TotalDependencies = _graph.Edges.Count;

            return _graph;
        }

        public string ExportToHTMLVisualization()
        {
            if (_graph == null)
                BuildGraph();

            return LoadTemplate().Replace("__GRAPH_DATA__", BuildJson());
        }

        private void CalculateCentrality()
        {
            if (_graph.Nodes.Count == 0)
                return;

            int maxUsage = _graph.Nodes.Max(node => node.UsageCount);
            int maxDependencies = _graph.Nodes.Max(node => node.DependencyCount);
            int maxConnections = _graph.Nodes.Max(node => node.UsageCount + node.DependencyCount);

            foreach (AssemblyInfo node in _graph.Nodes)
            {
                float usageScore = maxUsage > 0 ? (float)node.UsageCount / maxUsage : 0;
                float dependencyScore = maxDependencies > 0 ? (float)node.DependencyCount / maxDependencies : 0;
                float connectionScore = maxConnections > 0
                    ? (float)(node.UsageCount + node.DependencyCount) / maxConnections
                    : 0;

                node.Centrality = usageScore * 0.35f + dependencyScore * 0.35f + connectionScore * 0.3f;
            }
        }

        private string BuildJson()
        {
            DependencyGraphJson json = new DependencyGraphJson
            {
                nodes = _graph.Nodes
                    .OrderByDescending(node => node.Centrality)
                    .Select(node => new NodeJson
                    {
                        id = node.Guid,
                        name = node.Name,
                        usageCount = node.UsageCount,
                        dependencyCount = GetVisibleDependencyCount(node),
                        centrality = node.Centrality,
                        group = GetNodeGroup(node),
                        level = GetNodeLevel(node)
                    })
                    .ToList(),
                edges = _graph.Edges
                    .Select(edge => new EdgeJson
                    {
                        source = edge.FromGuid,
                        target = edge.ToGuid,
                        isUnused = edge.IsUnused
                    })
                    .ToList(),
                statistics = new StatisticsJson
                {
                    totalAssemblies = _graph.TotalAssemblies
                }
            };

            return JsonUtility.ToJson(json, true);
        }

        private static string LoadTemplate()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                throw new InvalidOperationException("Unable to resolve Unity project root.");

            string templatePath = Path.Combine(projectRoot, GraphPath.Path.Replace('/', Path.DirectorySeparatorChar), TemplateFileName);
            if (!File.Exists(templatePath))
                throw new FileNotFoundException("Dependency graph HTML template was not found.", templatePath);

            return File.ReadAllText(templatePath);
        }

        private string GetNodeGroup(AssemblyInfo node)
        {
            return GetVisibleDependencyCount(node) > 0 ? "connected" : "independent";
        }

        private int GetNodeLevel(AssemblyInfo node)
        {
            if (node.Centrality > 0.7f)
                return 1;

            if (node.Centrality > 0.3f)
                return 2;

            return 3;
        }

        private int GetVisibleDependencyCount(AssemblyInfo node)
        {
            return _graph.Edges.Count(edge => edge.FromGuid == node.Guid);
        }
    }

    internal class GraphPath : BasePathFinder<GraphPath>
    {
    }
}
