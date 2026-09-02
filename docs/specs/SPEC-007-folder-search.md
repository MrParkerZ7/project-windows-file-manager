# SPEC-007 — Folder search

<!-- Sync contract: `## Current behavior & invariants` is the section a behavior-changing commit must update in the same commit. -->

- Status: Current
- Feature area: folder tools
- Ships in: **Unreleased**. The Folder Control tab landed in commits `c8074b6` + `a974d99` (2026-04-15), *after* the 1.0.0 baseline commit `53bfad1`, and is not in the 1.0.0 CHANGELOG entry. Three match types (`Contains`/`Exclude`/`Mismatch`) were added by `125a7b1` (2026-04-16), `NotContain` by `40e2fa3` (2026-04-23), and the depth limit by `cd541cc` (2026-04-27).

## What

On the **Folder** tab the user builds a list of folder-name patterns, each with a match type, then presses **▶ Search**. The app walks every enabled target folder and lists the subfolders that satisfy *all* enabled patterns, showing folder name, recursive total size, and a clickable full path that opens the folder in Explorer. Results carry a checkbox; the checked set is what the folder actions ([SPEC-008](SPEC-008-clear-subfolders.md)) operate on.

- Six match types decide what a pattern means: three name comparisons (`Include`, `Match`, `Mismatch`, `Exclude`) and two child-item probes (`Contains`, `NotContain`).
- All enabled patterns must pass — the combination is AND, never OR.
- A **layers** box caps how deep the walk descends; empty means unlimited.
- **⏹ Stop** cancels a running search; **✕ Clear** discards results and the discovered inventories.
- Patterns, the depth cap, the result paths, and which results were checked are all saved with the active profile and restored on the next launch.

## Why

The duplicate scanner answers "which *files* are copies of each other". A different, equally common cleanup question is "which *folders* are of a kind" — every `node_modules` under a projects tree, every folder that holds a `.sln`, every folder that is *not* a git repo. Answering that by hand means walking a tree in Explorer and reading folder contents one by one.

Folder search makes that a query. The child-item probes (`Contains` / `NotContain`) are what make it useful beyond name matching: "a folder is a .NET solution" is not a name rule, it is "has a `*.sln` inside". AND-combining the patterns lets a user express a compound predicate — *contains `.git`* **and** *name is not `archive`* — and get a checked set they can then act on in bulk.

## Scope

### In

- Authoring, ordering, enabling/disabling and de-duplicating folder-search patterns.
- The recursive folder walk: roots, exclusions, depth limiting, cancellation.
- The six match-type semantics and their AND combination.
- Per-result recursive size computation and the result list's selection, sorting, and open-in-Explorer behavior.
- Restoring a saved result set when a profile is applied, including dropping folders that no longer exist.

### Out

- Everything done *with* the checked folders — the subfolder/file-type inventory and the bulk clear actions are [SPEC-008](SPEC-008-clear-subfolders.md). Flatten ("Move Files to Root") and Link Sibling Folders are implemented on the same checked set but have no spec of their own yet.
- Finding duplicate *files* — [SPEC-001](SPEC-001-duplicate-detection.md). Folder search is a separate walk with separate semantics and shares nothing but the target-folder and exclude-folder lists.
- How patterns and results are written to and read from `settings.json` — [SPEC-009](SPEC-009-settings-and-window-state-persistence.md).
- The `?` popups that document the toolbar — [SPEC-010](SPEC-010-contextual-help.md).
- Searching for *files* by name. There is no file-name search; the Search tab that once existed was removed in `2104fcc` and nothing replaced it.

## Current behavior & invariants

**Entry points**

| Trigger | Handler | Notes |
|---------|---------|-------|
| **+ Add** button / Enter in the pattern box (`AddFolderSearchPatternCommand`) | `MainViewModel.AddFolderSearchPattern(string?)` | trims input; rejects empty and exact `(Pattern, MatchType)` duplicates |
| **▲ / ▼** on a pattern chip | `MoveSearchPatternUp` / `MoveSearchPatternDown` | `ObservableCollection.Move` then `RefreshSearchPatternPriorities()` |
| **✕** on a pattern chip (`RemoveFolderSearchPatternCommand`) | `RemoveFolderSearchPattern(object?)` | re-prioritizes and saves |
| **▶ Search** (`SearchFoldersCommand`) | `MainViewModel.SearchFoldersAsync()` | `async void`; `CanExecute` = `!IsFolderSearching && TargetPaths.Any(t => t.IsEnabled)` |
| **⏹ Stop** (`StopFolderSearchCommand`) | `StopFolderSearch()` | cancels `_folderSearchCts`; `CanExecute` = `IsFolderSearching` |
| **✕ Clear** (`ClearFolderSearchCommand`) | `ClearFolderSearch()` | `CanExecute` = results **or** either discovered inventory non-empty |
| The walk | `MainViewModel.SearchFoldersRecursive(path, patterns, excludeNames, results, currentDepth, maxDepth, token)` | private, runs inside `Task.Run` |
| Child-item probe | `MainViewModel.FolderContainsItem(folderPath, pattern)` | `static`; uses `System.IO.Directory`/`File` **directly**, not `IFileSystemService` |
| Size fill | `MainViewModel.GetDirectorySize(path, token)` | `IFileSystemService.EnumerateFiles(path, "*", AllDirectories)` summed via `SafeFileSize` |
| Depth parse | `MainViewModel.ParseFolderSearchMaxDepth()` | reads `FolderSearchMaxDepthText` |
| Header select-all checkbox | `AreAllFoldersSelected` setter → `BulkSetFolderSelection(bool)` | the setter is both indicator and command |
| Column header click | `MainWindow.FolderResults_ColumnHeaderClick` | code-behind sorting on the default `CollectionView` |
| Full-path hyperlink | `OpenFolderLocationCommand` → `OpenFolderLocation(string?)` | `Process.Start("explorer.exe", "\"<path>\"")` |

**Rules**

1. **Adding a pattern.** The input (command parameter, else `NewFolderSearchPattern`) is trimmed. Empty → `FolderPatternAddStatus = "Enter a name first"`, nothing added. An existing pattern with the **same text and the same match type** → `"'<pattern>' with <MatchType> already in the list"`, nothing added; the same text with a *different* match type is allowed. Otherwise a `FolderSearchPattern { Pattern, MatchType = NewFolderSearchMatchType }` is appended (`IsEnabled` defaults to `true`), the input is cleared, priorities are refreshed and settings are saved.
2. **Priorities are positional, not persisted.** `RefreshSearchPatternPriorities()` assigns `Priority = index + 1` after every add, remove and move. `FolderSearchPattern.Priority` is `[JsonIgnore]`, so the numbering is rebuilt from list order on load rather than read from the file.
3. **Search inputs are snapshotted before the walk starts:** the `Value` of **enabled** `TargetPaths`; an `OrdinalIgnoreCase` `HashSet` of **enabled** `ExcludeFolderNames`; the **enabled** `FolderSearchPatterns` only; and `maxDepth` from `ParseFolderSearchMaxDepth()`. Disabled entries in any of the three lists are invisible to the search.
4. **Depth parsing.** `FolderSearchMaxDepthText` is trimmed; empty/whitespace → `null` (unlimited). Otherwise `int.TryParse` with `NumberStyles.Integer` + `CultureInfo.InvariantCulture`; a parsed value `>= 1` is the cap, anything else (non-numeric, `0`, negative) → `null`. The textbox rejects non-digit keystrokes (`MainWindow.IntegerTextBox_PreviewTextInput`) and shows an `∞` watermark while empty. Setting the property saves settings.
5. **Starting a search.** Any previous `CancellationTokenSource` is cancelled and disposed and a new one is created; `IsFolderSearching = true`; every existing result is unsubscribed from `FolderResult_PropertyChanged` and the collection is cleared; counts reset to 0; status becomes `"Searching folders..."`; `BeginBusy("Searching N target path(s) for folders (depth ≤ D)…")` starts the global busy bar (the depth suffix appears only when a cap is set).
6. **Roots.** Each enabled target path is checked with `IFileSystemService.DirectoryExists`; a missing root is **skipped silently** — no error, no status text. A surviving root is walked starting at `currentDepth = 1`.
7. **Only subdirectories become results.** A target path itself is never a result, whatever the patterns say.
8. **Per level.** `IFileSystemService.EnumerateDirectories(currentPath)` is called inside a `try`; **any** exception returns from that level, abandoning that branch without propagating. The concrete implementation enumerates with `IgnoreInaccessible = true` and `AttributesToSkip = FileAttributes.System`, so inaccessible and system directories never appear.
9. **Per subdirectory.** `token.ThrowIfCancellationRequested()`, then `dirName = Path.GetFileName(subDir)`. If `dirName` is in the exclude set the folder is skipped **both** as a candidate result **and** as a recursion target.
10. **Zero enabled patterns means everything matches.** The folder is added with `MatchedPattern = string.Empty`. A pattern list that is non-empty but entirely disabled behaves identically, because rule 3 filters to enabled patterns first.
11. **Match semantics** — name comparisons are `OrdinalIgnoreCase` against `dirName`:

    | Match type | Ordinal | Passes when |
    |------------|---------|-------------|
    | `Include` | 0 | `dirName` **contains** the pattern text |
    | `Match` | 1 | `dirName` **equals** the pattern text |
    | `Contains` | 2 | `FolderContainsItem(subDir, pattern)` is true |
    | `Exclude` | 3 | `dirName` does **not** contain the pattern text |
    | `Mismatch` | 4 | `dirName` does **not** equal the pattern text |
    | `NotContain` | 5 | `FolderContainsItem(subDir, pattern)` is false |

    An unrecognized enum value falls through to `_ => false`.
12. **Combination is AND.** Patterns are evaluated in list order and the loop `break`s on the first failure; the folder is added only if every enabled pattern passed. `MatchedPattern` records the **first** enabled pattern's text (it is assigned once, after the first pattern passes) — not the pattern that "caused" the match.
13. **Recursion is independent of matching.** After a subdirectory is handled it is descended into when `!maxDepth.HasValue || currentDepth < maxDepth.Value`. A non-matching folder is still walked through; only an excluded *name* stops descent. `maxDepth = 1` therefore means "direct children of each target path only".
14. **`FolderContainsItem` inspects immediate children only.** A pattern starting with `"*."` is a wildcard extension probe: `Directory.EnumerateFiles(folderPath)` (non-recursive) is scanned for a file whose `Path.GetExtension` equals `pattern[1..]` `OrdinalIgnoreCase`. Any other pattern is an exact-name probe: `Path.Combine(folderPath, pattern)` must exist as **either** a directory **or** a file — so `".git"` and `"package.json"` both work. Every exception is swallowed and returns `false`.
15. **Sizes are filled after the walk, inside the same background task.** For each found result, `TotalSize = GetDirectorySize(FullPath, token)`, which sums `SafeFileSize` over `EnumerateFiles(path, "*", AllDirectories)`. `SafeFileSize` returns `0` for a file it cannot stat; `GetDirectorySize` rethrows `OperationCanceledException` and returns `0` for anything else.
16. **Publishing results.** Only after the task completes are results added to `FolderSearchResults`, each subscribed to `FolderResult_PropertyChanged`. `FolderSearchCount` is set, `SelectedFolderCount` is reset to 0, and the status becomes `"Found N folders matching M patterns."` or, with no enabled patterns, `"Found N folders (no filter)."`.
17. **Cancellation and failure.** `OperationCanceledException` → `"Search stopped. {FolderSearchResults.Count} folders found so far."`; any other exception → `"Error: {ex.Message}"`. `finally` always clears `IsFolderSearching`, calls `EndBusy()` and `SaveSettings()`.
18. **Selection.** Each row's checkbox writes `FolderSearchResult.IsSelected`; the handler recomputes `SelectedFolderCount`, refreshes the header checkbox state through the backing field (never the setter, to avoid re-entrancy) and saves settings. The header checkbox's setter calls `BulkSetFolderSelection`, which sets every row inside a `_isBulkFolderSelectionUpdate` guard that suppresses the per-item handler, then updates the counts and saves **once**.
19. **Sorting is code-behind.** `FolderResults_ColumnHeaderClick` maps the header text to a sort property, overriding the display binding for two columns so formatted strings do not sort lexicographically: `"Size"` → `TotalSize`, `"Full Path"` → `FullPath`; other columns fall back to the column's `DisplayMemberBinding` path. Clicking the same header again toggles direction, and the header text is rewritten with a ` ▲` / ` ▼` suffix (all headers are stripped of the suffix first).
20. **Restore on profile load.** `ApplyProfileToLiveState` rebuilds `FolderSearchResults` from `ProfileSettings.FolderSearchResultPaths`, deriving `FolderName` from `Path.GetFileName` and `ParentPath` from `Path.GetDirectoryName`, and marks a row selected when its path is in `SelectedFolderSearchResultPaths` (`OrdinalIgnoreCase`). `MatchedPattern` is **not** restored. Status becomes `"Restored N folders from profile '<name>'."`. If any rows were restored, `ComputeRestoredSizesAsync()` runs off-thread: it recomputes each size and **removes every row whose directory no longer exists**, then refreshes the counts and header state.

**Invariants**

- Every result is a directory that existed at walk time and is a strict descendant of an enabled target path.
- A folder whose name is in the enabled exclude list never appears in results and is never descended into.
- With a depth cap of `N`, no result is more than `N` levels below its root.
- All enabled patterns must pass; a disabled pattern has no effect at all.
- The search only reads. No file or folder is created, moved, or deleted by a search or by size computation.
- `FolderMatchType` ordinals are frozen at `Include=0, Match=1, Contains=2, Exclude=3, Mismatch=4, NotContain=5` — pinned by the six-case theory `FolderSearchPatternTests.FolderMatchType_Ordinals_Preserved`. Reordering the enum silently reinterprets every saved profile ([ADR-007](../adr/ADR-007-system-text-json-settings-compatibility.md)).
- A new `FolderSearchPattern` defaults to `MatchType = Match`, `IsEnabled = true`, `Pattern = ""`, `Priority = 0` — pinned by `FolderSearchPatternTests.Constructor_ShouldSetDefaults`. `IsEnabled`, `MatchType` and `Priority` each raise `PropertyChanged` only on an actual change.
- Assigning `FolderSearchResult.TotalSize` raises exactly two notifications, in order — `TotalSize` then `TotalSizeDisplay` — pinned by `FolderSearchResultTests.TotalSize_WhenChanged_ShouldRaisePropertyChanged_ForBothTotalSizeAndDisplay`.
- `TotalSizeDisplay` formats `< 1024` as `"{n} B"` and otherwise divides by 1024 through KB/MB/GB/TB/**PB** with a `0.##` format: `1024 → "1 KB"`, `1073741824 → "1 GB"`, `1610612736 → "1.5 GB"`, `5 PB` caps at PB. Pinned by `FolderSearchResultTests.TotalSizeDisplay_ShouldFormat`, `_HugeValues_ShouldCapAtPB`, `_FractionalGB_ShouldFormatDecimals`. This is a **different** formatter from `ScannedFile.FormatFileSize` used by the duplicate list ([SPEC-001](SPEC-001-duplicate-detection.md)) — the two disagree on `1024` (`"1 KB"` vs `"1.0 KB"`).
- All folder enumeration goes through `IFileSystemService`; `FolderContainsItem` is the one deliberate exception and touches `System.IO` directly.

**Edge cases**

| Case | Behavior |
|------|----------|
| A target path does not exist | Skipped silently — no error, no status text, the other roots still run |
| Access denied partway down | `EnumerateDirectories` throws, that branch returns; results already collected are kept and the search completes |
| No patterns, or every pattern disabled | Every non-excluded folder within the depth cap is a result, `MatchedPattern` empty |
| Duplicate `(pattern, matchType)` added | Rejected with `"'<pattern>' with <MatchType> already in the list"` |
| Empty pattern input | Rejected with `"Enter a name first"` |
| Depth text `0`, `-3`, `abc`, or blank | Unlimited (`ParseFolderSearchMaxDepth` returns `null`) |
| Search cancelled | Results collected in the background task are discarded — they are published only on success — so the status reports `0` found (see *Not implemented*) |
| Two target paths where one contains the other | The overlapping folders appear **twice**; results are not de-duplicated |
| `Contains` pattern `"*.sln"` | Matches a folder holding a `.sln` file directly; a `.sln` in a *child* folder does not match |
| `Contains` pattern `".git"` | Matches whether `.git` is a directory or a file (worktree pointer file) |
| A result folder is deleted before the next launch | Dropped from the restored list by `ComputeRestoredSizesAsync` |
| Files inside a result cannot be stat'ed | They contribute `0` to `TotalSize` rather than failing the size pass |

**Not implemented**

- **`MatchedPattern` is never shown.** It is computed, stored on the result and dropped on restore, but the results grid has only the checkbox, `Folder Name`, `Size` and `Full Path` columns. Nothing surfaces which pattern matched.
- **Results are not de-duplicated across overlapping target paths.** Unlike the duplicate scanner, which passes its enumeration through `Distinct` ([SPEC-001](SPEC-001-duplicate-detection.md) rule 4), the folder walk simply appends per root, so a folder reachable from two enabled targets is listed twice and counted twice.
- **The cancel message always reports zero.** `FolderSearchResults` is cleared before the background task starts and repopulated only after it completes, so the `OperationCanceledException` handler reads `Count == 0` and renders `"Search stopped. 0 folders found so far."` regardless of how much was walked.
- **`FolderSearchIncludeSubdirectories` does not control this search.** Despite the name, that checkbox lives in the folder-action panel and governs the Scan Folders inventory ([SPEC-008](SPEC-008-clear-subfolders.md)). Folder-search recursion is controlled solely by the depth cap, and the checkbox is not persisted.
- **No unit tests cover the walk.** `MainViewModel` is `[ExcludeFromCodeCoverage]`, so `SearchFoldersRecursive`, `FolderContainsItem`, `GetDirectorySize` and the selection/restore paths are outside the 100 % gate ([ADR-011](../adr/ADR-011-coverage-via-collector-and-script.md)). Only the `FolderSearchPattern` and `FolderSearchResult` models are tested.
- **Two help popups describe an older version of this feature.** The **FOLDER SEARCH** popup still says *"Matching: Case-insensitive contains match"*, which was true before the match types existed, and the **MATCH TYPES** popup documents five types — `NotContain` is in the combo box and implemented but undocumented. See [SPEC-010](SPEC-010-contextual-help.md).
- **No name-pattern wildcards or regex for folder names.** `Include`/`Match` are literal substring/equality tests; the only wildcard support anywhere in this feature is the `*.ext` form inside `FolderContainsItem`.

## Links

- Decisions: [ADR-002 — Hand-rolled MVVM](../adr/ADR-002-hand-rolled-mvvm.md) · [ADR-004 — All I/O behind `IFileSystemService`](../adr/ADR-004-ifilesystemservice-io-abstraction.md) · [ADR-011 — coverage measured by coverlet.collector, enforced by script](../adr/ADR-011-coverage-via-collector-and-script.md) · [ADR-007 — `System.Text.Json` settings compatibility](../adr/ADR-007-system-text-json-settings-compatibility.md)
- Module docs: [WindowsFileManager (WPF UI)](../modules/ui.md) · [WindowsFileManager.Core](../modules/core.md) · [WindowsFileManager.Infrastructure](../modules/infrastructure.md)
- Related specs: [SPEC-008 — Clear subfolders](SPEC-008-clear-subfolders.md) · [SPEC-009 — Settings and window-state persistence](SPEC-009-settings-and-window-state-persistence.md) · [SPEC-010 — Contextual help](SPEC-010-contextual-help.md) · [SPEC-001 — Duplicate detection](SPEC-001-duplicate-detection.md)
- Background: [`../CONTEXT.md`](../CONTEXT.md) · [`../SECURITY.md`](../SECURITY.md)
- Tests: `tests/WindowsFileManager.Tests/Models/FolderSearchPatternTests.cs` · `tests/WindowsFileManager.Tests/Models/FolderSearchResultTests.cs`
