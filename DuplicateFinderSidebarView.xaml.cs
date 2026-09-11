using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Playnite.SDK;

namespace DuplicateFinder
{
    public partial class DuplicateFinderSidebarView : UserControl
    {
        public DuplicateFinderSidebarView(DuplicateFinderSidebarViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel;
        }

        private DuplicateFinderSidebarViewModel ViewModel => DataContext as DuplicateFinderSidebarViewModel;

        private void OnFindButtonClicked(object sender, RoutedEventArgs e)
        {
            ViewModel?.OnFindButtonClicked();
        }

        private void OnHiddenToggled(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = sender as CheckBox;
            DuplicateFinder.DuplicateGame game = checkBox?.DataContext as DuplicateFinder.DuplicateGame;

            if (ViewModel == null || game == null)
            {
                return;
            }

            // Push the control's state into the model explicitly: the Checked/Unchecked
            // event can fire before the binding has transferred the value to IsHidden.
            game.IsHidden = checkBox.IsChecked == true;
            ViewModel.ApplyHiddenState(game);
        }

        private void OnDeleteButtonClicked(object sender, RoutedEventArgs e)
        {
            DuplicateFinder.DuplicateGame game = (sender as FrameworkElement)?.DataContext as DuplicateFinder.DuplicateGame;
            ViewModel?.DeleteGame(game);
        }

        // ----- Sorting (issue #6) -----

        private void OnColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            GridViewColumnHeader header = e.OriginalSource as GridViewColumnHeader;

            if (header == null)
            {
                return;
            }

            // Identify columns by reference, so sorting is independent of theme
            // restyling and localization. The icon and Remove columns are ignored.
            string sortKey;

            if (header.Column == NameColumn)
            {
                sortKey = "Name";
            }
            else if (header.Column == LibraryColumn)
            {
                sortKey = "Library";
            }
            else if (header.Column == PlatformsColumn)
            {
                sortKey = "Platforms";
            }
            else if (header.Column == SourceColumn)
            {
                sortKey = "Source";
            }
            else if (header.Column == PlaytimeColumn)
            {
                sortKey = "Playtime";
            }
            else if (header.Column == LastPlayedColumn)
            {
                sortKey = "LastPlayed";
            }
            else if (header.Column == InstallDirectoryColumn)
            {
                sortKey = "InstallDirectory";
            }
            else if (header.Column == InstalledColumn)
            {
                sortKey = "Installed";
            }
            else if (header.Column == HiddenColumn)
            {
                sortKey = "Hidden";
            }
            else
            {
                return;
            }

            ViewModel?.SortBy(sortKey);
        }

        // ----- Context menu (issue #3) -----

        /// <summary>
        /// Right-clicking a row does not change ListView selection, so the menu
        /// targets the clicked row explicitly via hit-testing, and also selects
        /// it so the visual highlight matches the game being acted on.
        /// </summary>
        private void OnResultsContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            ListView listView = sender as ListView;

            if (listView?.ContextMenu == null)
            {
                return;
            }

            // ContextMenuEventArgs exposes the cursor position relative to the sender.
            DependencyObject hit = listView.InputHitTest(new Point(e.CursorLeft, e.CursorTop)) as DependencyObject;
            ListViewItem item = hit != null ? FindAncestor<ListViewItem>(hit) : null;

            if (item != null)
            {
                item.IsSelected = true;
                listView.ContextMenu.DataContext = item.DataContext;
            }
            else
            {
                // Right-click on empty space below the rows: no game to act on.
                listView.ContextMenu.DataContext = null;
                e.Handled = true;
            }
        }

        private static T FindAncestor<T>(DependencyObject node) where T : DependencyObject
        {
            while (node != null && !(node is T))
            {
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }

            return node as T;
        }

        private DuplicateFinder.DuplicateGame GameFromMenuItem(object sender)
        {
            MenuItem menuItem = sender as MenuItem;
            return menuItem?.ContextMenu?.DataContext as DuplicateFinder.DuplicateGame
                ?? menuItem?.DataContext as DuplicateFinder.DuplicateGame;
        }

        private void OnShowInLibraryClicked(object sender, RoutedEventArgs e)
        {
            ViewModel?.ShowInLibrary(GameFromMenuItem(sender));
        }

        /// <summary>Double-clicking a row shows the game in the library view.</summary>
        private void OnResultsDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject hit)
            {
                // Ignore double-clicks on interactive controls (Remove button, Hidden
                // checkbox) — those handle their own clicks.
                if (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(hit) != null)
                {
                    return;
                }

                ListViewItem item = FindAncestor<ListViewItem>(hit);

                if (item?.DataContext is DuplicateFinder.DuplicateGame game)
                {
                    ViewModel?.ShowInLibrary(game);
                }
            }
        }

        private void OnEditGameClicked(object sender, RoutedEventArgs e)
        {
            ViewModel?.EditGame(GameFromMenuItem(sender));
        }

        private void OnHideClicked(object sender, RoutedEventArgs e)
        {
            SetHiddenFromMenu(sender, true);
        }

        private void OnUnhideClicked(object sender, RoutedEventArgs e)
        {
            SetHiddenFromMenu(sender, false);
        }

        private void SetHiddenFromMenu(object sender, bool hidden)
        {
            DuplicateFinder.DuplicateGame game = GameFromMenuItem(sender);

            if (ViewModel == null || game == null)
            {
                return;
            }

            game.IsHidden = hidden;
            ViewModel.ApplyHiddenState(game);
        }

        private void OnRemoveFromContextMenuClicked(object sender, RoutedEventArgs e)
        {
            ViewModel?.DeleteGame(GameFromMenuItem(sender));
        }

        // ----- Bulk actions (multi-select toolbar) -----

        private List<DuplicateFinder.DuplicateGame> SelectedGames()
        {
            return ResultsList.SelectedItems.OfType<DuplicateFinder.DuplicateGame>().ToList();
        }

        private void OnHideSelectedClicked(object sender, RoutedEventArgs e)
        {
            ViewModel?.SetHiddenForGames(SelectedGames(), true);
        }

        private void OnUnhideSelectedClicked(object sender, RoutedEventArgs e)
        {
            ViewModel?.SetHiddenForGames(SelectedGames(), false);
        }

        private void OnRemoveSelectedClicked(object sender, RoutedEventArgs e)
        {
            ViewModel?.DeleteGames(SelectedGames());
        }

        private void OnKeepOneHideRestClicked(object sender, RoutedEventArgs e)
        {
            ViewModel?.KeepOneHideRest(GameFromMenuItem(sender));
        }
    }
}
