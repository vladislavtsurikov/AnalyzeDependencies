using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core.Graph;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core.Models;
using VladislavTsurikov.ReflectionUtility;
using VladislavTsurikov.ToolSystem.Runtime.Core;
using VladislavTsurikov.ToolSystem.Runtime.Core.Attributes;

namespace VladislavTsurikov.AnalyzeDependencies.Editor.ToolSystem
{
    [Name("Dependency Analyzer/Remove Cyclic Dependencies")]
    [Tool("Remove Cyclic Dependencies", "Break circular dependency chains by removing dependencies")]
    [ToolGroup("Dependencies")]
    public class RemoveCyclicDependenciesTool : EditorTool
    {
        private List<CyclicDependency> _cycles = new List<CyclicDependency>();

        public List<CyclicDependency> Cycles => _cycles;

        protected override void OnSetupTool()
        {
            DetectCycles();
        }

        private void DetectCycles()
        {
            var analyzer = DependencyAnalyzer.Instance;
            var detector = new CyclicDependencyDetector(analyzer);
            _cycles = detector.DetectCycles();

            if (_cycles.Count == 0)
            {
                Debug.Log("No cyclic dependencies detected. Your assembly dependency graph is acyclic (DAG).");
            }
            else
            {
                Debug.Log($"Detected {_cycles.Count} cyclic dependency chain(s).");
            }
        }

    }
}
