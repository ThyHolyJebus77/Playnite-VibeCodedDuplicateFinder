# VibeCodedDuplicateFinder
VibeCodedDuplicateFinder is a [Playnite](https://playnite.link/) extension that offers a simple way to find duplicate games in your library.

Fork of [elramidreju's DuplicateFinder](https://github.com/elramidreju/Playnite-DuplicateFinder), extended with LLM-assisted features.

VibeCodedDuplicateFinder adds a sidebar view to your Playnite UI, allowing you to search for duplicate games and making it easier to find games you might want to hide or remove from your library.

This is a vibe coded extension of the original with some adjustments made from my own personal preference & the issues tracker of the original.

## Install
Download the `.pext` file from the [latest release](https://github.com/ThyHolyJebus77/Playnite-VibeCodedDuplicateFinder/releases) and either:
- double-click it (Playnite will install it), or
- drag & drop it onto the Playnite window, or
- in Playnite, go to **Add-ons > Install from file...** and pick the `.pext`.

> **Note:** install only ONE of VibeCodedDuplicateFinder or the original DuplicateFinder — they do the same job and will both show up in your sidebar if both are installed.

## Build from source
Requires MSBuild (Visual Studio Build Tools) and the official .NET 4.6.2 reference assemblies. The `libs/` folder (Playnite.SDK.dll, Fastenshtein.dll) is gitignored — get them via NuGet (`PlayniteSDK`, `Fastenshtein`) or copy `Playnite.SDK.dll` from your Playnite install, then:

```
MSBuild.exe DuplicateFinder.csproj -p:Configuration=Release -restore:false -v:m -nologo
```

## Features
- Identifies duplicate games in your library based on the game's name.
- Allows you to include or exclude hidden games from the search.
- Displays results in a detailed, ordered view showing the name, platform and hidden status of each game.
- Includes a '_Check for similarity_' option to search for **potentially** duplicate games with non-matching names.
- Offers a customised _Tolerance_ setting to fine-tune the '_Check for similarity_' results.
- **Hide / unhide** any duplicate straight from the results list — the _Hidden_ checkbox is now clickable and writes directly to the Playnite database.
- **Remove** a game from your library from within the plugin, guarded by a confirmation dialogue (the action is permanent).
- **Installed** column showing each duplicates' install state, plus an 'Only installed games' filter to find games accidentally installed on several platforms (issue #7).
- **Sortable results** — click the Name, Library, Installed or Hidden column headers to sort ascending / descending (issue #6).
- **Interactable results** — right-click a game for a context menu (show in library, edit, hide/unhide, keep this one & hide the rest, remove); double-clicking a row shows the game in your library view (issue #3).
- **Progress dialog** with a progress bar while searching, so the UI no longer freezes on big libraries with the similarity check enabled (issue #4).
- Tooltip and README explanation of how 'Tolerance' works (issue #5).
- **Platforms, Source, Playtime & Last played columns** — see at a glance which platform each duplicate is on and which copy you actually play.
- **Grouped results** — matches are clustered into groups (transitively, so fuzzy chains belong together), with a checkbox to toggle grouping on/off.
- **Filter box** — live text filter over the results list.
- **Multi-select & bulk actions** — Ctrl/Shift-click several rows, then hide, unhide or remove them all at once (deletion asks once and lists every game it will remove).

## How 'Tolerance' works
'Check for similarity' uses the [Levenshtein distance](https://en.wikipedia.org/wiki/Levenshtein_distance) to calculate how different two titles' names are, instead of just checking for equality. In short, it accounts for how many edits of a single character you would need to change one title into the other. The greater the distance, the more different two titles are, and so the smaller the chances that they represent the same game in your library.

'Tolerance' is a threshold used to filter out titles with distances greater than that value, thus seeing fewer positives and false positives:
- **0** — exact matches only (equivalent to leaving 'Check for similarity' off).
- **1-2** — catches typos and small spelling differences between editions.
- **3+** — increasingly loose; expect sequels and unrelated games to appear.

## Disclaimer
- When using '_Check for similarity_', false positives for sequels or similarly named games are expected (e.g. _Far Cry 3_ and _Far Cry 4_, or _Feud_ and _Fez_). While this feature is useful for identifying potential duplicates, always review the results to ensure accuracy.
- The original DuplicateFinder was built with full awareness of the existence of [DuplicateHider](https://github.com/felixkmh/DuplicateHider) by **felixkmh**. However, DuplicateFinder was created with simplicity in mind, offering fewer features and less automation, but with greater ease of use and clarity. It provides information so users can review and decide what to do on a per-game basis. It is also designed to be easier to maintain and keep compatible with every version of Playnite.

## Contribute
If you feel VibeCodedDuplicateFinder is lacking an important feature or something needs fixing, please open an issue or email me about it before working on anything.

## Third-Party Libraries
This project uses the following third-party libraries:

- [Fastenshtein](https://github.com/DanHarltey/Fastenshtein) is licensed under the [MIT License](https://github.com/DanHarltey/Fastenshtein/blob/master/LICENSE) - Copyright (c) 2017 DanHartley
- [PlayniteSDK](https://playnite.link/) is licensed under the [MIT License](https://github.com/JosefNemec/Playnite/blob/master/LICENSE.md) - Copyright (c) 2020 Josef Nemec
