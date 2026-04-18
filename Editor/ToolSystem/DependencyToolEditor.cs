using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core.Models;
using VladislavTsurikov.SearchUtility.Editor;
using VladislavTsurikov.ToolSystem.Editor.UIToolkit;

namespace VladislavTsurikov.AnalyzeDependencies.Editor.ToolSystem
{
    public abstract class DependencyToolEditor : UIToolkitToolEditor
    {
        private DependencyAnalyzer _analyzer;
        private SearchListView _assembliesSearchView;
        private Label _selectionLabel;

        protected DependencyAnalyzer Analyzer
        {
            get
            {
                if (_analyzer == null)
                {
                    _analyzer = DependencyAnalyzer.Instance;
                }

                return _analyzer;
            }
        }

        protected IReadOnlyList<AssemblyInfo> SelectedAssemblies => Analyzer.GetSelectedAssembliesSorted();
        protected int SelectedAssemblyCount => SelectedAssemblies.Count;

        protected sealed override VisualElement CreateVisualElement()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;

            container.Add(CreateAssemblySelectionSection());

            var toolContent = new VisualElement();
            toolContent.style.paddingTop = 10;
            toolContent.style.paddingBottom = 10;
            toolContent.style.paddingLeft = 10;
            toolContent.style.paddingRight = 10;
            toolContent.style.marginTop = 10;

            BuildDependencyToolContent(toolContent);
            container.Add(toolContent);

            RefreshAssemblySelectionUI();
            return container;
        }

        protected abstract void BuildDependencyToolContent(VisualElement container);

        protected virtual void OnAssemblySelectionChanged()
        {
        }

        protected void RefreshDependencyToolState()
        {
            if (_assembliesSearchView != null)
            {
                _assembliesSearchView.ItemsSource = Analyzer.GetAllAssembliesSorted();
                _assembliesSearchView.Refresh();
            }

            RefreshSelectionSummary();
            OnAssemblySelectionChanged();
        }

        private VisualElement CreateAssemblySelectionSection()
        {
            var section = new VisualElement();
            section.style.flexDirection = FlexDirection.Column;

            _selectionLabel = new Label();
            _selectionLabel.style.marginBottom = 6;
            _selectionLabel.style.color = new Color(0.75f, 0.75f, 0.75f);
            section.Add(_selectionLabel);

            var buttonsRow = new VisualElement();
            buttonsRow.style.flexDirection = FlexDirection.Row;
            buttonsRow.style.marginBottom = 6;

            var selectAllFilteredButton = new Button(() =>
            {
                Analyzer.SelectAllFiltered(GetFilteredAssemblies());
                RefreshDependencyToolState();
            });
            selectAllFilteredButton.text = "Select All Filtered";
            buttonsRow.Add(selectAllFilteredButton);

            var clearSelectionButton = new Button(() =>
            {
                Analyzer.ClearSelection();
                RefreshDependencyToolState();
            });
            clearSelectionButton.text = "Clear Selection";
            clearSelectionButton.style.marginLeft = 6;
            buttonsRow.Add(clearSelectionButton);

            section.Add(buttonsRow);

            _assembliesSearchView = new SearchListView
            {
                HeaderTitle = "Asmdef Selection",
                PlaceholderText = "Search asmdef",
                SelectionType = SelectionType.Multiple,
                ItemsSource = Analyzer.GetAllAssembliesSorted(),
                MakeItem = MakeAssemblyItem,
                BindItem = BindAssemblyItem,
                MatchesSearch = (item, query) =>
                {
                    AssemblyInfo assembly = item as AssemblyInfo;
                    return assembly != null &&
                           assembly.Name.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
                },
                OnSelectionChanged = HandleSelectionChange,
                OnQueryChanged = _ => RefreshSelectionSummary(),
                GetEmptyMessage = query =>
                    string.IsNullOrWhiteSpace(query)
                        ? "No asmdefs available."
                        : $"No asmdefs match '{query}'."
            };
            section.Add(_assembliesSearchView);

            return section;
        }

        private static VisualElement MakeAssemblyItem()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.justifyContent = Justify.Center;
            row.style.paddingLeft = 6;
            row.style.paddingRight = 6;

            var nameLabel = new Label();
            nameLabel.name = "assembly-name";
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.fontSize = 11;
            row.Add(nameLabel);

            var pathLabel = new Label();
            pathLabel.name = "assembly-path";
            pathLabel.style.fontSize = 10;
            pathLabel.style.color = new Color(0.68f, 0.68f, 0.68f);
            row.Add(pathLabel);

            return row;
        }

        private static void BindAssemblyItem(VisualElement element, object item, int index)
        {
            AssemblyInfo assembly = item as AssemblyInfo;
            if (assembly == null)
                return;

            element.Q<Label>("assembly-name").text = assembly.Name;
            element.Q<Label>("assembly-path").text = assembly.Path;
        }

        private void HandleSelectionChange(IEnumerable<object> selectedItems)
        {
            HashSet<AssemblyInfo> selectedAssemblies = selectedItems.OfType<AssemblyInfo>().ToHashSet();

            foreach (AssemblyInfo assembly in GetFilteredAssemblies())
            {
                Analyzer.SetAssemblySelection(assembly, selectedAssemblies.Contains(assembly));
            }

            RefreshSelectionSummary();
            OnAssemblySelectionChanged();
        }

        private void RefreshAssemblySelectionUI()
        {
            if (_assembliesSearchView != null)
            {
                _assembliesSearchView.ItemsSource = Analyzer.GetAllAssembliesSorted();
                _assembliesSearchView.Refresh();
            }

            RefreshSelectionSummary();
            OnAssemblySelectionChanged();
        }

        private void RefreshSelectionSummary()
        {
            if (_selectionLabel == null)
                return;

            int totalCount = Analyzer.GetAllAssembliesSorted().Count;
            _selectionLabel.text = $"Selected asmdefs: {SelectedAssemblyCount} / {totalCount}";
        }

        private List<AssemblyInfo> GetFilteredAssemblies()
        {
            if (_assembliesSearchView == null)
                return Analyzer.GetAllAssembliesSorted();

            return _assembliesSearchView.FilteredItems.OfType<AssemblyInfo>().ToList();
        }
    }
}
