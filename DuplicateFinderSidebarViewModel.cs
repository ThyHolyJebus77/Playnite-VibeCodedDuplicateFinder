using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

namespace DuplicateFinder
{
    public class DuplicateFinderSidebarViewModel : ObservableObject
    {
        public Action<bool, bool, bool, uint> RequestFindDuplicates;

        /// <summary>The view bound to the results list (filter + sort + grouping live here).</summary>
        public ListCollectionView FoundDuplicates { get => _foundDuplicates; private set => SetValue(ref _foundDuplicates, value); }
        public bool IncludeHiddenGames { get => _includeHiddenGames; set => SetValue(ref _includeHiddenGames, value); }
        public bool InstalledOnly { get => _installedOnly; set => SetValue(ref _installedOnly, value); }
        public bool CheckSimilarity { get => _checkSimilarity; set => SetValue(ref _checkSimilarity, value); }
        public uint Tolerance { get => _tolerance; set => SetValue(ref _tolerance, value); }

        /// <summary>Free-text filter over result names (case-insensitive substring).</summary>
        public string FilterText
        {
            get => _filterText;
            set
            {
                SetValue(ref _filterText, value ?? string.Empty);
                RefreshView();
            }
        }

        /// <summary>Show results grouped into their match clusters.</summary>
        public bool GroupResults
        {
            get => _groupResults;
            set
            {
                SetValue(ref _groupResults, value);
                RefreshView();
            }
        }

        private ListCollectionView _foundDuplicates;
        private List<DuplicateFinder.DuplicateGame> _allResults = new List<DuplicateFinder.DuplicateGame>();
        private bool _includeHiddenGames = false;
        private bool _installedOnly = false;
        private bool _checkSimilarity = false;
        private uint _tolerance = 0;
        private string _filterText = string.Empty;
        private bool _groupResults = true;
        private DuplicateFinder _plugin;

        public DuplicateFinderSidebarViewModel(DuplicateFinder plugin)
        {
            _plugin = plugin;
            _foundDuplicates = new ListCollectionView(_allResults);
        }

        public void OnFindButtonClicked()
        {
            RequestFindDuplicates?.Invoke(_includeHiddenGames, _installedOnly, _checkSimilarity, _tolerance);
        }

        /// <summary>Entry point from the plugin after a search completes.</summary>
        public void SetResults(List<DuplicateFinder.DuplicateGame> results)
        {
            _allResults = results ?? new List<DuplicateFinder.DuplicateGame>();
            // Assign through the property so change notification fires: the
            // ListView is bound to the previous view instance, and assigning
            // the field to itself via SetValue would compare equal and never
            // raise the event, leaving the list stuck on stale results.
            FoundDuplicates = new ListCollectionView(_allResults);
            RefreshView();
        }

        // ----- Sorting (issue #6) & filtering / grouping -----

        /// <summary>Column currently used for sorting, null when unsorted.</summary>
        public string SortColumn
        {
            get => _sortColumn;
            private set => SetValue(ref _sortColumn, value);
        }

        /// <summary>true = ascending, false = descending.</summary>
        public bool SortAscending
        {
            get => _sortAscending;
            private set => SetValue(ref _sortAscending, value);
        }

        private string _sortColumn = null;
        private bool _sortAscending = true;

        /// <summary>
        /// Sorts the results by the given column key. Clicking the column that
        /// is already active flips the direction; clicking another one resets
        /// to ascending.
        /// </summary>
        public void SortBy(string columnKey)
        {
            if (columnKey != SortColumn)
            {
                SortColumn = columnKey;
                SortAscending = true;
            }
            else
            {
                SortAscending = !SortAscending;
            }

            RefreshView();
        }

        /// <summary>Maps a column key to the DuplicateGame property it sorts by.</summary>
        private static string SortPropertyName(string columnKey)
        {
            switch (columnKey)
            {
                case "Name": return "SortName";
                case "Installed": return "IsInstalled";
                case "Hidden": return "IsHidden";
                case "Playtime": return "PlaytimeSeconds";
                case "LastPlayed": return "LastPlayed";
                case "InstallDirectory": return "InstallDirectory";
                default: return columnKey; // Library, Platforms, Source
            }
        }

        private void RefreshView()
        {
            ListCollectionView view = _foundDuplicates;

            if (view == null)
            {
                return;
            }

            // Filter: free-text over game names.
            if (string.IsNullOrWhiteSpace(_filterText))
            {
                view.Filter = null;
            }
            else
            {
                string needle = _filterText.Trim().ToLowerInvariant();
                view.Filter = o => (o as DuplicateFinder.DuplicateGame)?.Name?.ToLowerInvariant()?.Contains(needle) == true;
            }

            // Grouping: cluster members share their GroupKey.
            view.GroupDescriptions.Clear();

            if (_groupResults)
            {
                view.GroupDescriptions.Add(new PropertyGroupDescription("GroupKey"));
            }

            // Sorting via the view, so grouping and filtering compose with it.
            view.SortDescriptions.Clear();

            if (!string.IsNullOrEmpty(_sortColumn))
            {
                ListSortDirection direction = SortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;
                view.SortDescriptions.Add(new SortDescription(SortPropertyName(SortColumn), direction));

                // Grouping sorts by group first; within a group use the chosen column.
                if (_groupResults)
                {
                    view.SortDescriptions.Insert(0, new SortDescription("GroupKey", ListSortDirection.Ascending));
                }
            }

            view.Refresh();
        }

        // ----- Row actions (single row) -----

        /// <summary>
        /// Saves the hidden state currently shown on a row's checkbox to the
        /// Playnite database. If the game no longer exists, the row is removed.
        /// </summary>
        public void ApplyHiddenState(DuplicateFinder.DuplicateGame game)
        {
            if (game == null)
            {
                return;
            }

            bool saved = _plugin.SetGameHidden(game.Id, game.IsHidden);

            if (!saved)
            {
                ShowGameNotFound(game);
                RemoveFromResults(game);
            }
        }

        /// <summary>
        /// Asks for confirmation and deletes a game from the Playnite library.
        /// The row is removed from the results only if the deletion succeeded.
        /// </summary>
        public void DeleteGame(DuplicateFinder.DuplicateGame game)
        {
            if (game == null)
            {
                return;
            }

            if (!_plugin.ConfirmDeleteGames(new List<string> { game.Name }))
            {
                return;
            }

            bool deleted = _plugin.DeleteGame(game.Id);

            if (!deleted)
            {
                ShowGameNotFound(game);
            }

            RemoveFromResults(game);
        }

        /// <summary>
        /// Keeps the given game visible and hides every other row in its match
        /// cluster. Hiding is reversible, so no confirmation dialog.
        /// </summary>
        public void KeepOneHideRest(DuplicateFinder.DuplicateGame kept)
        {
            if (kept == null)
            {
                return;
            }

            List<DuplicateFinder.DuplicateGame> others = _allResults
                .Where(g => g != kept && g.GroupKey == kept.GroupKey)
                .ToList();

            if (others.Count == 0)
            {
                return;
            }

            // The kept copy is the canonical one, so make sure it is visible.
            if (kept.IsHidden)
            {
                kept.IsHidden = false;

                if (!_plugin.SetGameHidden(kept.Id, false))
                {
                    ShowGameNotFound(kept);
                    RemoveFromResults(kept);
                    return;
                }
            }

            foreach (DuplicateFinder.DuplicateGame game in others)
            {
                game.IsHidden = true;

                if (!_plugin.SetGameHidden(game.Id, true))
                {
                    ShowGameNotFound(game);
                    RemoveFromResults(game);
                }
            }
        }

        // ----- Bulk actions on selected rows -----

        public void SetHiddenForGames(IEnumerable<DuplicateFinder.DuplicateGame> games, bool hidden)
        {
            List<DuplicateFinder.DuplicateGame> targets = games?.Where(g => g != null).ToList() ?? new List<DuplicateFinder.DuplicateGame>();

            if (targets.Count == 0)
            {
                ShowNoRowsChecked();
                return;
            }

            foreach (DuplicateFinder.DuplicateGame game in targets)
            {
                game.IsHidden = hidden;

                if (!_plugin.SetGameHidden(game.Id, hidden))
                {
                    ShowGameNotFound(game);
                    RemoveFromResults(game);
                }
            }
        }

        /// <summary>
        /// Deletes every selected row after ONE confirmation listing them all.
        /// </summary>
        public void DeleteGames(IEnumerable<DuplicateFinder.DuplicateGame> games)
        {
            List<DuplicateFinder.DuplicateGame> targets = games?.Where(g => g != null).ToList() ?? new List<DuplicateFinder.DuplicateGame>();

            if (targets.Count == 0)
            {
                ShowNoRowsChecked();
                return;
            }

            if (!_plugin.ConfirmDeleteGames(targets.Select(g => g.Name).ToList()))
            {
                return;
            }

            foreach (DuplicateFinder.DuplicateGame game in targets)
            {
                bool deleted = _plugin.DeleteGame(game.Id);

                if (!deleted)
                {
                    ShowGameNotFound(game);
                }

                RemoveFromResults(game);
            }
        }

        private void RemoveFromResults(DuplicateFinder.DuplicateGame game)
        {
            int index = _allResults.IndexOf(game);

            if (index >= 0)
            {
                _allResults.RemoveAt(index);
                _foundDuplicates.Refresh();
            }
        }

        private void ShowNoRowsChecked()
        {
            _plugin.PlayniteApi.Dialogs.ShowMessage(
                ResourceProvider.GetString("LOCDuplicateFinder_NoRowsChecked")
                    ?? "No rows are selected. Ctrl/Shift-click rows to select the games you want to act on.",
                "DuplicateFinder",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void ShowGameNotFound(DuplicateFinder.DuplicateGame game)
        {
            _plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                ResourceProvider.GetString("LOCDuplicateFinder_GameNotFound")
                    ?? "This game is no longer in the library. The row has been removed from the results.",
                ResourceProvider.GetString("LOCDuplicateFinder_DeleteTitle") ?? "Remove game");
        }

        // ----- Context menu actions (issue #3) -----

        /// <summary>
        /// Switches to Playnite's library view and selects the game, so the
        /// user can use every normal library interaction on it.
        /// </summary>
        public void ShowInLibrary(DuplicateFinder.DuplicateGame game)
        {
            if (game == null)
            {
                return;
            }

            _plugin.PlayniteApi.MainView.SwitchToLibraryView();
            _plugin.PlayniteApi.MainView.SelectGame(game.Id);
        }

        /// <summary>
        /// Opens Playnite's own edit dialog for the game.
        /// </summary>
        public void EditGame(DuplicateFinder.DuplicateGame game)
        {
            if (game == null)
            {
                return;
            }

            _plugin.PlayniteApi.MainView.OpenEditDialog(game.Id);
        }
    }
}
