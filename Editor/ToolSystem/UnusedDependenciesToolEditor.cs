using Cysharp.Threading.Tasks;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using VladislavTsurikov.Core.Editor;

namespace VladislavTsurikov.AnalyzeDependencies.Editor.ToolSystem
{
    [ElementEditor(typeof(UnusedDependenciesTool))]
    public class UnusedDependenciesToolEditor : DependencyToolEditor
    {
        private Button _analyzeButton;
        private Button _removeButton;
        private Label _resultLabel;
        private VisualElement _resultsContainer;
        private Label _statusLabel;

        private UnusedDependenciesTool Tool => (UnusedDependenciesTool)Target;

        protected override void BuildDependencyToolContent(VisualElement container)
        {
            var helpBox = new HelpBox(
                "Analyzes selected assemblies to find unused dependencies.\n\n" +
                "Removing unused dependencies can:\n" +
                "• Reduce compilation times\n" +
                "• Improve code maintainability\n" +
                "• Make dependency graphs cleaner",
                HelpBoxMessageType.Info);
            container.Add(helpBox);

            _statusLabel = new Label();
            _statusLabel.style.marginTop = 10;
            _statusLabel.style.color = new Color(0.78f, 0.78f, 0.78f);
            container.Add(_statusLabel);

            _analyzeButton = new Button(() => RunAnalysisAsync().Forget());
            _analyzeButton.text = "Find Unused In Selected";
            _analyzeButton.style.height = 30;
            _analyzeButton.style.marginTop = 10;
            container.Add(_analyzeButton);

            _resultLabel = new Label();
            _resultLabel.style.marginTop = 10;
            _resultLabel.style.marginBottom = 5;
            _resultLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            container.Add(_resultLabel);

            _resultsContainer = new VisualElement();
            _resultsContainer.style.marginTop = 5;
            container.Add(_resultsContainer);

            _removeButton = new Button(() =>
            {
                Tool.RemoveUnusedDependencies();
                RefreshDependencyToolState();
            });
            _removeButton.text = "Clear Unused Dependencies";
            _removeButton.style.height = 30;
            _removeButton.style.marginTop = 10;
            container.Add(_removeButton);

            RefreshContentState();
        }

        protected override void OnAssemblySelectionChanged()
        {
            RefreshContentState();
        }

        private async UniTaskVoid RunAnalysisAsync()
        {
            await Tool.AnalyzeSelectedAssembliesAsync();
            RefreshDependencyToolState();
        }

        private void RefreshContentState()
        {
            bool hasSelection = SelectedAssemblyCount > 0;

            if (_analyzeButton != null)
                _analyzeButton.SetEnabled(hasSelection);

            if (_statusLabel != null)
            {
                _statusLabel.text = hasSelection
                    ? $"Selected asmdefs for analysis: {SelectedAssemblyCount}"
                    : "Select at least one asmdef above to analyze unused dependencies.";
            }

            if (_resultLabel == null || _resultsContainer == null || _removeButton == null)
                return;

            _resultsContainer.Clear();

            if (!Tool.AnalysisPerformed)
            {
                _resultLabel.text = "Unused dependencies found: 0";
                _resultLabel.style.color = new Color(0.78f, 0.78f, 0.78f);
                _resultsContainer.Add(CreateInfoLabel("Run the analysis to check selected asmdefs."));
                _removeButton.style.display = DisplayStyle.None;
                return;
            }

            _resultLabel.text = $"Unused dependencies found: {Tool.TotalUnusedCount}";

            if (Tool.TotalUnusedCount > 0)
            {
                _resultLabel.style.color = new Color(0.95f, 0.75f, 0.25f);

                var scrollView = new ScrollView(ScrollViewMode.Vertical);
                scrollView.style.maxHeight = 200;
                scrollView.style.marginBottom = 10;
                scrollView.style.backgroundColor = new UnityEngine.Color(0.2f, 0.2f, 0.2f);
                scrollView.style.paddingTop = 5;
                scrollView.style.paddingBottom = 5;
                scrollView.style.paddingLeft = 5;
                scrollView.style.paddingRight = 5;

                foreach (var assembly in Tool.AssembliesToProcess.Take(20))
                {
                    var assemblyLabel = new Label($"• {assembly.Name} ({assembly.UnusedDependencies.Count} unused)");
                    assemblyLabel.style.fontSize = 11;
                    scrollView.Add(assemblyLabel);
                }

                if (Tool.AssembliesToProcess.Count > 20)
                {
                    var moreLabel = new Label($"... and {Tool.AssembliesToProcess.Count - 20} more assemblies");
                    moreLabel.style.fontSize = 11;
                    moreLabel.style.color = new UnityEngine.Color(0.7f, 0.7f, 0.7f);
                    scrollView.Add(moreLabel);
                }

                _resultsContainer.Add(scrollView);
                _removeButton.style.display = DisplayStyle.Flex;
            }
            else
            {
                _resultLabel.style.color = new Color(0.3f, 0.8f, 0.3f);
                var noIssuesLabel = new Label(
                    $"No unused dependencies found in {Tool.AnalyzedAssemblyCount} selected asmdefs.");
                noIssuesLabel.style.color = new UnityEngine.Color(0.3f, 0.8f, 0.3f);
                noIssuesLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                _resultsContainer.Add(noIssuesLabel);
                _removeButton.style.display = DisplayStyle.None;
            }
        }

        private static Label CreateInfoLabel(string text)
        {
            var label = new Label(text);
            label.style.color = new Color(0.78f, 0.78f, 0.78f);
            return label;
        }
    }
}
