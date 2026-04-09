using System.Collections;
using NUnit.Framework;
using UnityEngine.UIElements;
using VladislavTsurikov.SearchUtility.Editor;

namespace VladislavTsurikov.AnalyzeDependencies.Tests.Editor
{
    public class SearchViewTests
    {
        [Test]
        public void SetQueryWithoutNotify_FiltersCaseInsensitiveWithoutInvokingCallback()
        {
            var searchView = new SearchView
            {
                ItemsSource = new ArrayList { "Alpha", "beta", "Gamma" }
            };

            int queryChangedCount = 0;
            searchView.OnQueryChanged = _ => queryChangedCount++;

            searchView.SetQueryWithoutNotify("AL");

            Assert.That(queryChangedCount, Is.EqualTo(0));
            Assert.That(searchView.FilteredItems.Count, Is.EqualTo(1));
            Assert.That(searchView.FilteredItems[0], Is.EqualTo("Alpha"));
        }

        [Test]
        public void EmptyState_UsesCustomMessageWhenNoMatchesFound()
        {
            var searchView = new SearchView
            {
                ItemsSource = new ArrayList { "Alpha" },
                GetEmptyMessage = query => $"No results for {query}"
            };

            searchView.SetQueryWithoutNotify("zzz");

            Assert.That(searchView.Q<VisualElement>("EmptyStateContainer").style.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(searchView.Q<Label>("EmptyStateLabel").text, Is.EqualTo("No results for zzz"));
        }

        [Test]
        public void SelectionChanged_InvokesCallbackForSelectedItems()
        {
            var searchView = new SearchView
            {
                ItemsSource = new ArrayList { "Alpha", "Beta" },
                SelectionType = SelectionType.Single
            };

            object selectedItem = null;
            searchView.OnSelectionChanged = items =>
            {
                foreach (object item in items)
                {
                    selectedItem = item;
                    break;
                }
            };

            searchView.Q<ListView>("ResultsListView").SetSelection(1);

            Assert.That(selectedItem, Is.EqualTo("Beta"));
        }
    }
}
