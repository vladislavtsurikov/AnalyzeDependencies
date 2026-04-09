using UnityEngine;
using UnityEngine.UIElements;
using VladislavTsurikov.Core.Editor;

namespace VladislavTsurikov.AnalyzeDependencies.Editor.ToolSystem
{
    [ElementEditor(typeof(GitRepositorySupportTool))]
    public class GitRepositorySupportToolEditor : DependencyToolEditor
    {
        private Button _prepareButton;
        private Button _registerSubmoduleButton;
        private Button _removeGitButton;
        private Label _selectionHint;

        private GitRepositorySupportTool Tool => (GitRepositorySupportTool)Target;

        protected override void BuildDependencyToolContent(VisualElement container)
        {
            var helpBox = new HelpBox(
                "Manage git repositories for selected asmdef folders.\n\n" +
                "You can initialize local repositories, register them as submodules in the root repository, or remove the local .git entry before exporting to Asset Store.",
                HelpBoxMessageType.Info);
            container.Add(helpBox);

            _selectionHint = new Label();
            _selectionHint.style.marginTop = 10;
            _selectionHint.style.color = new Color(0.8f, 0.8f, 0.8f);
            container.Add(_selectionHint);

            _prepareButton = new Button(() => Tool.PrepareRepositories());
            _prepareButton.text = "Prepare Git Repository";
            _prepareButton.style.height = 30;
            _prepareButton.style.marginTop = 10;
            container.Add(_prepareButton);

            _registerSubmoduleButton = new Button(() => Tool.RegisterSubmodules());
            _registerSubmoduleButton.text = "Register Submodule";
            _registerSubmoduleButton.style.height = 30;
            _registerSubmoduleButton.style.marginTop = 6;
            container.Add(_registerSubmoduleButton);

            _removeGitButton = new Button(() => Tool.RemoveGitMarkers());
            _removeGitButton.text = "Remove .git";
            _removeGitButton.style.height = 30;
            _removeGitButton.style.marginTop = 6;
            container.Add(_removeGitButton);
        }

        protected override void OnAssemblySelectionChanged()
        {
            bool hasSelection = SelectedAssemblyCount > 0;

            if (_prepareButton != null)
                _prepareButton.SetEnabled(hasSelection);
            if (_registerSubmoduleButton != null)
                _registerSubmoduleButton.SetEnabled(hasSelection);
            if (_removeGitButton != null)
                _removeGitButton.SetEnabled(hasSelection);

            if (_selectionHint != null)
            {
                _selectionHint.text = hasSelection
                    ? $"Selected asmdefs: {SelectedAssemblyCount}"
                    : "Select at least one asmdef above to enable git actions.";
            }
        }
    }
}
