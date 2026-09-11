using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace DuplicateFinder
{
    public class DuplicateFinder : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("888e1e40-9a63-482f-8385-52be9b279a0e");

        private DuplicateFinderSidebarView _sidebarView;
        private DuplicateFinderSidebarViewModel _sidebarViewModel;

        public DuplicateFinder(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties
            {
                HasSettings = false,
            };

            _sidebarViewModel = new DuplicateFinderSidebarViewModel(this);
            _sidebarViewModel.RequestFindDuplicates += ExecuteDuplicateFinder;
            _sidebarView = new DuplicateFinderSidebarView(_sidebarViewModel);
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return base.GetSettings(firstRunSettings);
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return base.GetSettingsView(firstRunSettings);
        }

        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            return base.GetGameViewControl(args);
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return new SidebarItem
            {
                Type = SiderbarItemType.View,
                Icon = new Image
                {
                    Source = new BitmapImage(new Uri("pack://application:,,,/DuplicateFinder;component/icon.png"))
                },
                Title = "VibeCodedDuplicateFinder",
                Visible = true,
                Opened = () => { return _sidebarView; },
            };
        }

        private void ExecuteDuplicateFinder(bool includeHiddenGames, bool installedOnly, bool checkSimilarity, uint tolerance)
        {
            IEnumerable<Game> query = PlayniteApi.Database.Games;

            if (!includeHiddenGames)
            {
                query = query.Where(game => !game.Hidden);
            }

            if (installedOnly)
            {
                query = query.Where(game => game.IsInstalled);
            }

            // Snapshot the data needed for the search on the UI thread, so the
            // potentially slow Levenshtein pass never touches database objects
            // from the background thread of the progress dialog.
            List<GameSnapshot> snapshots = query.Select(game => new GameSnapshot
            {
                Id = game.Id,
                Name = game.Name,
                SortingName = game.SortingName,
                Icon = game.Icon,
                Hidden = game.Hidden,
                PluginId = game.PluginId,
                Installed = game.IsInstalled,
                Platforms = game.Platforms != null && game.Platforms.Count > 0
                    ? string.Join(", ", game.Platforms.Where(p => p != null).Select(p => p.Name).Where(n => !string.IsNullOrEmpty(n)))
                    : string.Empty,
                SourceName = game.Source?.Name ?? string.Empty,
                InstallDirectory = game.InstallDirectory ?? string.Empty,
                PlaytimeSeconds = game.Playtime,
                LastPlayed = game.LastActivity
            }).ToList();

            List<GameSnapshot> matches;

            if (snapshots.Count == 0)
            {
                matches = new List<GameSnapshot>();
            }
            else
            {
                matches = FindDuplicatesWithProgressDialog(snapshots, checkSimilarity, tolerance);
            }

            _sidebarViewModel.SetResults(matches.Select(snapshot => snapshot.ToDisplayModel()).ToList());
        }

        /// <summary>
        /// Runs the duplicate search inside Playnite's global progress dialog,
        /// with a determinate progress bar, so the UI stays responsive while
        /// the (potentially slow) similarity search runs.
        /// Results are grouped into match clusters via union-find, so transitively
        /// matching titles (A~B, B~C) end up in the same group even when A and C
        /// do not match each other directly.
        /// </summary>
        private List<GameSnapshot> FindDuplicatesWithProgressDialog(List<GameSnapshot> snapshots, bool checkSimilarity, uint tolerance)
        {
            string progressText = ResourceProvider.GetString("LOCDuplicateFinder_Searching") ?? "Searching for duplicates...";

            GlobalProgressOptions options = new GlobalProgressOptions(progressText)
            {
                IsIndeterminate = false,
                Cancelable = false
            };

            List<GameSnapshot> matched = null;

            PlayniteApi.Dialogs.ActivateGlobalProgress((args) =>
            {
                int n = snapshots.Count;
                args.ProgressMaxValue = n;

                // Union-find over snapshot indices.
                int[] parent = Enumerable.Range(0, n).ToArray();

                int Find(int i)
                {
                    while (parent[i] != i)
                    {
                        parent[i] = parent[parent[i]];
                        i = parent[i];
                    }
                    return i;
                }

                void Union(int a, int b)
                {
                    int ra = Find(a), rb = Find(b);
                    if (ra != rb)
                    {
                        parent[rb] = ra;
                    }
                }

                // Fastenshtein objects pre-compute their character arrays on construction,
                // so build one per game up front instead of once per pair.
                Fastenshtein.Levenshtein[] levenshteins = checkSimilarity
                    ? snapshots.Select(s => new Fastenshtein.Levenshtein(s.Name ?? string.Empty)).ToArray()
                    : null;

                bool Matches(int i, int j)
                {
                    if (!checkSimilarity)
                    {
                        return snapshots[i].Name == snapshots[j].Name;
                    }

                    return levenshteins[i].DistanceFrom(snapshots[j].Name ?? string.Empty) <= tolerance;
                }

                // Update the bar roughly every 1% of games to avoid flooding the UI thread.
                int reportStep = Math.Max(1, n / 100);

                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        if (Matches(i, j))
                        {
                            Union(i, j);
                        }
                    }

                    if (i % reportStep == 0)
                    {
                        args.CurrentProgressValue = i;
                    }
                }

                // Collect connected components with more than one member.
                Dictionary<int, List<int>> groups = new Dictionary<int, List<int>>();

                for (int i = 0; i < n; i++)
                {
                    int root = Find(i);

                    if (!groups.TryGetValue(root, out List<int> members))
                    {
                        members = new List<int>();
                        groups[root] = members;
                    }

                    members.Add(i);
                }

                matched = new List<GameSnapshot>();

                foreach (List<int> members in groups.Values)
                {
                    if (members.Count < 2)
                    {
                        continue;
                    }

                    // Group key: the alphabetically first title in the cluster.
                    string groupKey = members.Select(i => snapshots[i].SortingName ?? snapshots[i].Name ?? string.Empty)
                                            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
                                            .First();

                    foreach (int i in members)
                    {
                        snapshots[i].GroupKey = groupKey;
                        snapshots[i].GroupSize = members.Count;
                        matched.Add(snapshots[i]);
                    }
                }

                args.CurrentProgressValue = n;
            }, options);

            return matched ?? new List<GameSnapshot>();
        }

        /// <summary>
        /// Lightweight read-only copy of the game properties the search needs,
        /// taken on the UI thread before the background search starts.
        /// </summary>
        public class GameSnapshot
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public string SortingName { get; set; }
            public string Icon { get; set; }
            public bool Hidden { get; set; }
            public Guid PluginId { get; set; }
            public bool Installed { get; set; }
            public string Platforms { get; set; }
            public string SourceName { get; set; }
            public string InstallDirectory { get; set; }
            public ulong PlaytimeSeconds { get; set; }
            public DateTime? LastPlayed { get; set; }
            public string GroupKey { get; set; } = string.Empty;
            public int GroupSize { get; set; } = 1;

            public DuplicateGame ToDisplayModel()
            {
                return new DuplicateGame(this);
            }
        }

        /// <summary>
        /// Applies a new hidden state to a game in the Playnite database.
        /// Returns true if the game was found and updated.
        /// </summary>
        public bool SetGameHidden(Guid gameId, bool hidden)
        {
            Game game = PlayniteApi.Database.Games.Get(gameId);

            if (game == null)
            {
                return false;
            }

            game.Hidden = hidden;
            PlayniteApi.Database.Games.Update(game);

            logger.Debug($"DuplicateFinder: hidden state of '{game.Name}' ({gameId}) set to {hidden}.");
            return true;
        }

        /// <summary>
        /// Permanently removes a game from the Playnite library.
        /// Returns true if the game was found and removed.
        /// </summary>
        public bool DeleteGame(Guid gameId)
        {
            bool removed = PlayniteApi.Database.Games.Remove(gameId);

            if (removed)
            {
                logger.Debug($"DuplicateFinder: game {gameId} removed from the library.");
            }

            return removed;
        }

        /// <summary>
        /// Asks the user to confirm removing one or more games from the library.
        /// Returns true if the user answered Yes.
        /// </summary>
        public bool ConfirmDeleteGames(IReadOnlyList<string> gameNames)
        {
            string title = ResourceProvider.GetString("LOCDuplicateFinder_DeleteTitle") ?? "Remove game";

            string message;

            if (gameNames.Count == 1)
            {
                string template = ResourceProvider.GetString("LOCDuplicateFinder_DeleteConfirm")
                    ?? "Remove \"{0}\" from your library?\nThis cannot be undone.";
                message = string.Format(template, gameNames[0]);
            }
            else
            {
                const int maxListed = 20;
                string list = string.Join("\n", gameNames.Take(maxListed));

                if (gameNames.Count > maxListed)
                {
                    list += $"\n... ({gameNames.Count - maxListed} more)";
                }

                string template = ResourceProvider.GetString("LOCDuplicateFinder_BatchDeleteConfirm")
                    ?? "Remove these {0} games from your library?\n\n{1}\n\nThis cannot be undone.";
                message = string.Format(template, gameNames.Count, list);
            }

            MessageBoxResult result = PlayniteApi.Dialogs.ShowMessage(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);

            return result == MessageBoxResult.Yes;
        }

        public class DuplicateGame : ObservableObject
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public string SortingName { get; set; }
            public string Icon { get; set; }
            public string Library { get; set; }
            public string Platforms { get; set; }
            public string Source { get; set; }

            /// <summary>
            /// Where the game is installed. Empty for store games whose install
            /// path Playnite does not track: those display a dash instead of a
            /// useless blank cell, so the column scans cleanly.
            /// </summary>
            public string InstallDirectory { get; set; }

            /// <summary>Display text for the install location column ('—' when unknown).</summary>
            public string InstallDirectoryDisplay
            {
                get
                {
                    return string.IsNullOrEmpty(InstallDirectory) ? "—" : InstallDirectory;
                }
            }

            public ulong PlaytimeSeconds { get; set; }
            public DateTime? LastPlayed { get; set; }

            /// <summary>Alphabetically first title of the match cluster this row belongs to.</summary>
            public string GroupKey { get; set; } = string.Empty;

            /// <summary>How many rows are in this row's match cluster.</summary>
            public int GroupSize { get; set; } = 1;

            public bool IsHidden
            {
                get => _isHidden;
                set => SetValue(ref _isHidden, value);
            }

            private bool _isHidden;

            public bool IsInstalled { get; set; }

            /// <summary>Localized Yes/No text for the Installed column.</summary>
            public string InstalledDisplay
            {
                get
                {
                    return ResourceProvider.GetString(IsInstalled ? "LOCDuplicateFinder_Yes" : "LOCDuplicateFinder_No")
                        ?? (IsInstalled ? "Yes" : "No");
                }
            }

            /// <summary>Playtime in hours for display ('—' when never played).</summary>
            public string PlaytimeDisplay
            {
                get
                {
                    if (PlaytimeSeconds == 0)
                    {
                        return "—";
                    }

                    double hours = PlaytimeSeconds / 3600.0;
                    return hours >= 10 ? hours.ToString("F0") : hours.ToString("F1");
                }
            }

            /// <summary>Last played date for display ('—' when never launched).</summary>
            public string LastPlayedDisplay
            {
                get
                {
                    if (LastPlayed == null)
                    {
                        return "—";
                    }

                    return LastPlayed.Value.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
                }
            }

            /// <summary>SortingName with Name fallback, used by the sortable Name column.</summary>
            public string SortName
            {
                get => SortingName ?? Name;
            }

            public object IconImage
            {
                get
                {
                    if (string.IsNullOrEmpty(Icon))
                    {
                        return ResourceProvider.GetResource("DefaultGameIcon");
                    }
                    else
                    {
                        return Playnite.SDK.API.Instance.Database.GetFullFilePath(Icon);
                    }
                }
            }

            public DuplicateGame(GameSnapshot snapshot)
            {
                Id = snapshot.Id;
                Name = snapshot.Name;
                SortingName = snapshot.SortingName;
                Icon = snapshot.Icon;
                IsHidden = snapshot.Hidden;
                IsInstalled = snapshot.Installed;
                Platforms = snapshot.Platforms;
                Source = snapshot.SourceName;
                InstallDirectory = snapshot.InstallDirectory;
                PlaytimeSeconds = snapshot.PlaytimeSeconds;
                LastPlayed = snapshot.LastPlayed;
                GroupKey = snapshot.GroupKey;
                GroupSize = snapshot.GroupSize;

                LibraryPlugin libraryPlugin = Playnite.SDK.API.Instance.Addons.Plugins.FirstOrDefault(plugin => plugin.Id == snapshot.PluginId) as LibraryPlugin;
                if (libraryPlugin == null)
                {
                    Library = ResourceProvider.GetString("LOCDuplicateFinder_NoLibraryPlugin");
                }
                else
                {
                    Library = libraryPlugin.Name;
                }
            }
        }
    }
}
