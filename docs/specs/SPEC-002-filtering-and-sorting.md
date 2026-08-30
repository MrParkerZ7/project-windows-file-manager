# SPEC-002 — Filtering and sorting

<!-- Sync contract: `## Current behavior & invariants` is the section a behavior-changing commit must update in the same commit. -->

- Status: Current
- Feature area: duplicate scanning (results view)
- Ships in: **1.0.0** — the extension facet, minimum-file-size, minimum-duplicate-count and the ten sort options are all present at the 1.0.0 baseline commit `53bfad1`.

## What

After a scan finishes, the **Base Filters** bar lets the user narrow and re-order the result list without rescanning:

- **Sort** — a ten-entry drop-down that orders duplicate groups by size, copy count, wasted space, file type, or name (ascending or descending for each).
- **Types** — one checkbox per file extension found in the results, each showing how many groups carry that extension, plus **All** / **None** buttons.
- **Min size** — a number plus a unit (B / KB / MB / GB); groups whose file size is below the threshold are hidden.
- **Min dups** — hide groups that have fewer than N copies. Defaults to 2, so nothing is hidden by default.
- A live **"Showing X of Y groups"** readout.

Everything here works instantly against results already in memory. Nothing on this bar touches the disk, and nothing on it changes what a future scan looks for.

## Why

A scan of a real drive returns hundreds of groups spanning everything from 2 KB thumbnails to 4 GB video files. Almost all of the reclaimable space lives in a handful of large groups, and almost all of the *risk* lives in the long tail of tiny ones the user did not mean to touch. Sorting by wasted space puts the payoff first; the minimum-size threshold removes the noise; the type checkboxes let the user work one file category at a time ("just the photos") rather than reasoning about a mixed list.

Re-running the scan to answer each of those questions would cost minutes of hashing for a question that is purely about presentation — so these are view filters, deliberately separate from the scan options in [SPEC-001](SPEC-001-duplicate-detection.md).

## Scope

### In

- The `ICollectionView` over the scan results and the predicate that decides which groups are visible.
- Building the per-extension facet from a finished scan (which extensions exist, group counts, aggregate size).
- The minimum-file-size threshold and its unit conversion.
- The minimum-duplicate-count threshold.
- The ten sort options and how each maps to a sort key.
- The "Showing X of Y groups" counter.

### Out

- Deciding what counts as a duplicate in the first place, and the scan-time `MinimumFileSize` / `FileExtensions` options on `ScanOptions` — [SPEC-001](SPEC-001-duplicate-detection.md). Those are a different mechanism at a different time; they are also currently unreachable from the UI (see that spec's *Not implemented*).
- Pattern rules that *check* files rather than *hide* groups — [SPEC-003](SPEC-003-custom-filter-rules.md). Custom rules change selection; this spec changes visibility. The two never interact.
- Acting on what is visible (delete / move / reveal) — [SPEC-004](SPEC-004-selection-and-file-actions.md).
- The extension breakdown shown in the analytics panel, which is computed independently from `ScanResult` — [SPEC-006](SPEC-006-analytics-and-resource-monitor.md).
- Sorting the *folder search* results grid, which is a separate code-behind click-sort — [SPEC-007](SPEC-007-folder-search.md).

## Current behavior & invariants

> These behaviors live entirely in `MainViewModel`, which carries `[ExcludeFromCodeCoverage]`. They are therefore **not pinned by unit tests** — the 100% coverage figure covers `WindowsFileManager.Core`, `WindowsFileManager.Application`, and the UI's `Helpers`/`ViewModels` *excluding* the attributed types. Only the model-level defaults noted below have tests. Treat this section as the contract; verify changes by hand.

**Entry points**

| Trigger | Handler | Notes |
|---------|---------|-------|
| Sort drop-down (`SelectedSortOption`) | setter → `MainViewModel.ApplySorting()` | fires only when the value actually changes (`SetProperty`) |
| **Apply** button (`ApplyFileSizeFilterCommand`) | `MainViewModel.ApplyFilters()` | the only path that re-parses min-size and recounts |
| **All** (`ShowAllTypesCommand`) / **None** (`ClearAllTypesCommand`) | `SetAllExtensions(bool)` | sets `IsChecked` on every `ExtensionFilter` |
| Any extension checkbox | `ExtensionFilter.PropertyChanged` → `ApplyFilters()` | subscription added per item in `BuildExtensionFilters` |
| End of a scan | `BuildExtensionFilters(result.DuplicateGroups)` | rebuilds the facet, then sets `TotalGroupCount = FilteredGroupCount = groups.Count` |
| The view itself | `FilteredDuplicateGroups = CollectionViewSource.GetDefaultView(DuplicateGroups)` with `Filter = FilterDuplicateGroup` | wired once in the constructor |

**Rules**

1. **The facet is rebuilt from scratch after every scan.** `BuildExtensionFilters` clears `ExtensionFilters`, then groups the result by `Path.GetExtension(group.Files[0].FilePath).ToLowerInvariant()` — the *first file* of each group decides the group's extension. An empty extension becomes the literal string `"(no ext)"`. Per entry: `FileCount` = number of duplicate groups carrying that extension (not number of files), `TotalSize` = `Σ (group.FileSize × group.Count)`, `IsChecked` = `true`. Entries are ordered by `FileCount` descending.
2. **Every facet entry re-runs the whole filter pass on any property change.** The handler attached in `BuildExtensionFilters` is `(_, _) => ApplyFilters()` — it is not scoped to `IsChecked`, so a change to `FileCount` or `TotalSize` would also trigger a refresh. In practice only `IsChecked` changes after construction.
3. **`ApplyFilters` parses the size threshold, then refreshes and recounts.** `double.TryParse(MinFileSizeText, …)`; a parse failure or a value ≤ 0 leaves the threshold at 0 (= disabled). Otherwise the unit maps: `"B"` × 1, `"KB"` × 1024, `"MB"` × 1024², `"GB"` × 1024³, **any other string → 0**. The result is cast to `long` and stored in `_minFileSizeBytes`. Then `FilteredDuplicateGroups.Refresh()` runs and `FilteredGroupCount` is recomputed by iterating the refreshed view.
4. **`FilterDuplicateGroup` is the single visibility predicate.** A group is visible only if all four checks pass, evaluated in this order:
   1. the object is a `DuplicateGroup` (otherwise hidden);
   2. `group.Count >= MinDuplicateCount`;
   3. `_minFileSizeBytes == 0` **or** `group.FileSize >= _minFileSizeBytes`;
   4. if `ExtensionFilters` is non-empty: the group's extension is computed the same way as in rule 1, matched against `ExtensionFilters` by **exact string equality** on `Extension`; if a match is found and it is unchecked, the group is hidden. **No match found → the group is visible.**
5. **Sorting replaces, never accumulates.** `ApplySorting` clears `SortDescriptions`, adds exactly one description for the matched label, and calls `Refresh()`. The mapping is exhaustive over the ten labels in `SortOptionsList`:

   | Label | Sort key | Direction |
   |-------|----------|-----------|
   | `Size (largest)` | `DuplicateGroup.FileSize` | Descending |
   | `Size (smallest)` | `DuplicateGroup.FileSize` | Ascending |
   | `File count (most)` | `DuplicateGroup.Count` | Descending |
   | `File count (fewest)` | `DuplicateGroup.Count` | Ascending |
   | `Wasted space (most)` | `DuplicateGroup.WastedBytes` | Descending |
   | `Wasted space (least)` | `DuplicateGroup.WastedBytes` | Ascending |
   | `Type (A-Z)` | `DuplicateGroup.FileExtension` | Ascending |
   | `Type (Z-A)` | `DuplicateGroup.FileExtension` | Descending |
   | `Name (A-Z)` | `DuplicateGroup.FirstFileName` | Ascending |
   | `Name (Z-A)` | `DuplicateGroup.FirstFileName` | Descending |

   A label outside this set adds no `SortDescription`, leaving the view in insertion order. `FileExtension` and `FirstFileName` are both derived from `Files[0]`, so type/name sorting is by the group's *first* file (which, per [SPEC-001](SPEC-001-duplicate-detection.md), is the lowest path in the group).
6. **The initial ordering is the scanner's, not the drop-down's.** `SelectedSortOption` is initialised to `"Size (largest)"` as a field value and `ApplySorting` is never called from the constructor. Loading a profile assigns `SelectedSortOption = profile.SelectedSortOption`, which also defaults to `"Size (largest)"` — `SetProperty` sees no change and does not fire. So until the user actually picks a different option, the list keeps the order `DuplicateScannerService` returned it in: **wasted space descending**, not file size descending.
7. **`FilteredGroupCount` only moves when `ApplyFilters` runs.** `TotalGroupCount` and `FilteredGroupCount` are both set to the raw group count at the end of a scan. Typing into **Min size** or **Min dups** updates the bound property immediately (`UpdateSourceTrigger=PropertyChanged`) but does **not** refresh the view or the count until **Apply** is pressed. Toggling an extension checkbox *does* go through `ApplyFilters`, so that path updates both. `ApplySorting` refreshes the view but never recomputes the count (correctly — sorting cannot change how many rows pass).
8. **The whole Base Filters bar is hidden in regex-match mode.** Its `Border` binds `Visibility` to `DuplicateMatchByRegex` through `InverseBoolToVisibility`, and the Match-by-Name-Regex bar takes its place. The filter *state* is unchanged and still applied to the view; only the controls are out of reach. The extension checkbox row is a separate `Border` and stays visible whenever `ExtensionFilters.Count > 0`.
9. **Neither numeric box restricts typed input.** The repo's digit-only `IntegerTextBox_PreviewTextInput` handler is wired to the folder-search depth box and the link-siblings layer box only. `MinFileSizeText` is a `string` parsed leniently (rule 3); `MinDuplicateCount` is an `int` binding, so non-numeric text fails WPF conversion and the property retains its previous value.

**Invariants**

- Filtering and sorting are **display-only**. They never mutate `DuplicateGroups`, never touch the filesystem, never re-hash, and never re-run a scan. A hidden group still exists, still holds its files, and its files keep whatever selection state they had.
- Because [SPEC-004](SPEC-004-selection-and-file-actions.md)'s selection commands iterate `DuplicateGroups` directly rather than the view, **a hidden group is still acted on** by Select All / Select Newer / Select Older / Apply Rules. Visibility is not a safety boundary.
- Exactly one `SortDescription` is ever active.
- The extension facet describes the *last completed scan*. It is rebuilt only by `BuildExtensionFilters` at the end of a scan, so it can never disagree with the currently loaded results.
- Of the five controls, **only the sort option is persisted**: `ProfileSettings.SelectedSortOption` (default `"Size (largest)"`, pinned by `ProfileSettingsTests.Constructor_ShouldSetDefaults`) is written by `SnapshotLiveStateInto` and restored by `ApplyProfileToLiveState`. `MinFileSizeText` (default `""`), `SelectedSizeUnit` (default `"KB"`), `MinDuplicateCount` (default `2`) and the per-extension check states are session-only — they appear in neither `ProfileSettings` nor the snapshot/apply pair, and reset on restart and on every profile switch.
- `FilterDuplicateGroup` dereferences `group.Files[0]`. This is safe because [SPEC-001](SPEC-001-duplicate-detection.md) guarantees a group has ≥ 2 files at creation, and [SPEC-004](SPEC-004-selection-and-file-actions.md) removes a group from the collection once deletion leaves it with ≤ 1 file.

**Edge cases**

| Case | Behavior |
|------|----------|
| `Min size` blank, non-numeric, `0`, or negative | Threshold stays 0 — the size filter is off, all sizes pass |
| `Min size` set but unit is not `B`/`KB`/`MB`/`GB` | Threshold becomes 0, i.e. filter off. Unreachable through the UI: the combo is bound to `SizeUnits` = `{ B, KB, MB, GB }` |
| Fractional `Min size` (e.g. `1.5` MB) | Accepted — parsed as `double`, multiplied, then truncated by the `long` cast |
| `Min dups` set to 0 or 1 | Every group passes check 2 — groups always hold ≥ 2 files |
| All extensions unchecked | Every group fails check 4; the list is empty and the readout shows `Showing 0 of Y groups` |
| `ExtensionFilters` empty (no scan yet, or a scan that returned nothing) | Check 4 is skipped entirely |
| A group whose extension has no facet entry | Visible — an unmatched extension is treated as "not filtered out", not as "filtered out" |
| Groups with no extension | Bucketed under `"(no ext)"` in both the facet and the predicate, so the checkbox controls them consistently |
| Extension casing (`.JPG` vs `.jpg`) | Both sides lower-case with `ToLowerInvariant()`, so they always land in the same bucket |
| Changing `Min size` / `Min dups` without pressing **Apply** | No visible effect; the view and the counter are stale until **Apply**, an extension toggle, or the next scan |
| Switching profiles | Sort option is restored from the profile; the other four filters keep whatever the session already had (they are not part of a profile) |

**Not implemented**

- **No maximum-size or size-range filter** — only a lower bound exists.
- **No maximum-duplicate-count filter** — only a lower bound exists.
- **No text/name search over the results list.** Narrowing by name is done through the pattern rules in [SPEC-003](SPEC-003-custom-filter-rules.md), which *select* files rather than hide groups.
- **No "hide groups I have already acted on" state**; the view has no notion of handled vs unhandled.
- **Filter state is not part of a profile.** Only the sort option is. A user who relies on a 10 MB threshold has to retype it after every restart and every profile switch.

## Links

- Decisions: [ADR-002 — Hand-rolled MVVM (`ViewModelBase` + `RelayCommand`)](../adr/ADR-002-hand-rolled-mvvm.md) · [ADR-006 — Persist settings on every mutation](../adr/ADR-006-persist-settings-on-every-mutation.md)
- Module docs: [WindowsFileManager (WPF UI)](../modules/ui.md) · [WindowsFileManager.Core](../modules/core.md)
- Related specs: [SPEC-001 — Duplicate detection](SPEC-001-duplicate-detection.md) · [SPEC-003 — Custom filter rules](SPEC-003-custom-filter-rules.md) · [SPEC-004 — Selection and file actions](SPEC-004-selection-and-file-actions.md) · [SPEC-006 — Analytics and resource monitor](SPEC-006-analytics-and-resource-monitor.md) · [SPEC-009 — Settings and window-state persistence](SPEC-009-settings-and-window-state-persistence.md)
- Background: [`../CONTEXT.md`](../CONTEXT.md)
- Tests: `tests/WindowsFileManager.Tests/Models/DuplicateGroupTests.cs` (the `FileSize` / `Count` / `WastedBytes` / `FileExtension` / `FirstFileName` sort keys) · `tests/WindowsFileManager.Tests/Models/ProfileSettingsTests.cs` (the persisted `SelectedSortOption` default)
