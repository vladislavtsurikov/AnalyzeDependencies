using System;
using System.Collections.Generic;

namespace VladislavTsurikov.AnalyzeDependencies.Editor.Core.Graph
{
    [Serializable]
    internal class DependencyGraphJson
    {
        public List<NodeJson> nodes;
        public List<EdgeJson> edges;
        public StatisticsJson statistics;
    }

    [Serializable]
    internal class NodeJson
    {
        public string id;
        public string name;
        public int usageCount;
        public int dependencyCount;
        public float centrality;
        public string group;
        public int level;
    }

    [Serializable]
    internal class EdgeJson
    {
        public string source;
        public string target;
        public bool isUnused;
    }

    [Serializable]
    internal class StatisticsJson
    {
        public int totalAssemblies;
    }
}
