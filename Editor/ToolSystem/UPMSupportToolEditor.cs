using UnityEngine;
using UnityEngine.UIElements;
using VladislavTsurikov.Core.Editor;

namespace VladislavTsurikov.AnalyzeDependencies.Editor.ToolSystem
{
    [ElementEditor(typeof(UPMSupportTool))]
    public class UPMSupportToolEditor : DependencyToolEditor
    {
        private Button _createButton;
        private Button _syncButton;
        private Button _manifestSnippetButton;
        private Label _selectionHint;

        private UPMSupportTool Tool => (UPMSupportTool)Target;

        protected override void BuildDependencyToolContent(VisualElement container)
        {
            var helpBox = new HelpBox(
                "Creates UPM support for selected asmdefs and synchronizes package dependencies from asmdef references.\n\n" +
                "Create UPM Support: writes a package.json if the asmdef folder does not have one yet.\n" +
                "Sync Dependencies: updates package.json dependencies while keeping other fields intact.\n" +
                "Generate Dependencies Snippet: copies a ready-to-paste dependencies block for the selected packages and all package dependencies they need.",
                HelpBoxMessageType.Info);
            container.Add(helpBox);

            _selectionHint = new Label();
            _selectionHint.style.marginTop = 10;
            _selectionHint.style.color = new Color(0.8f, 0.8f, 0.8f);
            container.Add(_selectionHint);

            _createButton = new Button(() => Tool.CreateUpmSupport());
            _createButton.text = "Create UPM Support";
            _createButton.style.height = 30;
            _createButton.style.marginTop = 10;
            container.Add(_createButton);

            _syncButton = new Button(() => Tool.SyncDependencies());
            _syncButton.text = "Sync Dependencies";
            _syncButton.style.height = 30;
            _syncButton.style.marginTop = 6;
            container.Add(_syncButton);

            _manifestSnippetButton = new Button(() => Tool.GenerateDependenciesSnippet());
            _manifestSnippetButton.text = "Generate Dependencies Snippet";
            _manifestSnippetButton.style.height = 30;
            _manifestSnippetButton.style.marginTop = 6;
            container.Add(_manifestSnippetButton);
        }

        protected override void OnAssemblySelectionChanged()
        {
            bool hasSelection = SelectedAssemblyCount > 0;

            if (_createButton != null)
                _createButton.SetEnabled(hasSelection);

            if (_syncButton != null)
                _syncButton.SetEnabled(hasSelection);

            if (_manifestSnippetButton != null)
                _manifestSnippetButton.SetEnabled(hasSelection);

            if (_selectionHint != null)
            {
                _selectionHint.text = hasSelection
                    ? $"Selected asmdefs: {SelectedAssemblyCount}"
                    : "Select at least one asmdef above to enable UPM actions.";
            }
        }
    }
}
