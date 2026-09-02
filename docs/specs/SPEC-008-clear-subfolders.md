# SPEC-008 — Clear subfolders

<!-- Sync contract: `## Current behavior & invariants` is the section a behavior-changing commit must update in the same commit. -->

- Status: Current
- Feature area: folder tools
- Ships in: **Unreleased**. The Clear Subfolders action landed in `9ad146c` (2026-04-16), the paged/sized inventory in `1ba3f51` (2026-04-17), and Recycle-Bin deletion with undo history in `567ac3c` + `f094c80` (2026-04-18) — all after the 1.0.0 baseline commit `53bfad1`.

## What

Starting from the folders checked in the folder-search results ([SPEC-007](SPEC-007-folder-search.md)), **🔍 Scan Folders** builds two inventories of what lives inside them:

- **Subfolders** — every distinct subfolder *name*, with how many times it was found, the total bytes it accounts for, and an expandable, filterable, paged list of the exact locations.
- **File Types** — every distinct file *extension*, with the same count/size/locations shape.

The user ticks names in either list and presses **🗑 Clear Selected Subfolders** or **🗑 Clear Selected Files**. After a confirmation dialog, the matching items are sent to the **Recycle Bin**, a progress bar tracks the sweep, one undoable entry is pushed to the History tab, and the inventory is rescanned so it reflects the deletions.

This is the "delete `node_modules` from 40 projects at once" workflow — and the same machinery for "delete every `.log` under these folders".

## Why

Build output, dependency caches and log files reproduce the same folder names and extensions across dozens of unrelated projects. Reclaiming that space folder-by-folder is mechanical and slow, and the alternative — a scripted `rm -rf` — is unrecoverable if the pattern is slightly wrong.

The inventory step exists so the user chooses from **what is actually there**, ranked by how often it occurs, rather than typing a name from memory. Deleting to the Recycle Bin instead of unlinking, plus a history entry that can restore the whole batch, makes a mis-click survivable — the property that makes bulk deletion acceptable at all.

## Scope

### In

- Building the subfolder-name and file-extension inventories from the checked folder-search results, including counts, sizes, and per-item location lists.
- Filtering, paging and selection inside those inventories.
- **Clear Selected Subfolders** — recycling matching subfolders across all checked result folders.
- **Clear Selected Files** — recycling every discovered file of the checked extensions.
- The confirmation, progress, failure counting, history entry and rescan that surround both actions.

### Out

- Producing the checked set of folders in the first place — [SPEC-007](SPEC-007-folder-search.md).
- The other two actions driven by the same checked set: **Move Files to Root** (flatten, `FlattenSelectedFolders`, which builds its own `DiscoveredFlattenFileTypes` inventory) and **Link Sibling Folders** (`LinkSiblingFolders`, which creates `.lnk` shortcuts). Both are fully implemented and both push undo history, but neither has a spec yet.
- Deleting or moving *duplicate* files from the Duplication tab — [SPEC-004](SPEC-004-selection-and-file-actions.md).
- How the action-history list is persisted — [SPEC-009](SPEC-009-settings-and-window-state-persistence.md).
- Permanent deletion. Nothing in this feature bypasses the Recycle Bin, and no "shred"/"delete permanently" option exists or is scaffolded.

## Current behavior & invariants

**Entry points**

| Trigger | Handler | Notes |
|---------|---------|-------|
| **🔍 Scan Folders** (`ScanSubfoldersCommand`) | `MainViewModel.ScanSubfolders()` | `async void`; `CanExecute` = `SelectedFolderCount > 0 && !IsScanningFolders` |
| **Include Subdirectories** checkbox | `FolderSearchIncludeSubdirectories` (default `true`) | read once, at scan start; **not** persisted |
| The walk | `MainViewModel.ScanFolderContents(path, rootParent, subfolderData, fileTypeData, recurse)` | private, recursive, runs inside `Task.Run` |
| Subfolder / file-type filter boxes | `SubfolderFilter` / `FileTypeFilter` → `FilteredSubfolders` / `FilteredFileTypes` | `OrdinalIgnoreCase` substring over the item `Name` |
| **Select all** in a list | `SelectAllSubfoldersCommand` / `SelectAllFileTypesCommand` | operate on the **filtered** view |
| **Clear selection** in a list | `ClearSubfolderSelectionCommand` / `ClearFileTypeSelectionCommand` | operate on the **whole** collection |
| **‹ Prev / Next ›** inside an expanded item | `MainWindow.SubfolderPrevPage_Click` / `SubfolderNextPage_Click` → `SubfolderItem.PrevPage()` / `NextPage()` | code-behind, because the buttons bind to the item, not the VM |
| **🗑 Clear Selected Subfolders** (`ClearSelectedSubfoldersCommand`) | `MainViewModel.ClearSelectedSubfolders()` | `async void`; `CanExecute` = `DiscoveredSubfolders.Any(s => s.IsSelected)` |
| **🗑 Clear Selected Files** (`ClearSelectedFileTypesCommand`) | `MainViewModel.ClearSelectedFileTypes()` | `async void`; `CanExecute` = `DiscoveredFileTypes.Any(t => t.IsSelected)` |
| Recycle primitives | `RecycleDirectory(path)` / `RecycleFile(path)` | `Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory` / `DeleteFile` with `UIOption.OnlyErrorDialogs` + `RecycleOption.SendToRecycleBin` |
| **↶** undo | `UndoLastActionCommand` / `UndoSpecificActionCommand` → `UndoEntry` → `RestoreFromRecycleBin` | late-bound `Shell.Application` COM |

**Rules**

1. **Scan inputs.** The checked `FolderSearchResults` supply the root list; `FolderSearchIncludeSubdirectories` supplies the recursion flag. Both are captured before the background task starts. The scan clears both inventories, clears both filter boxes, sets `ClearSubfolderStatus = "Scanning folders and files..."`, and opens the global busy bar (`BeginBusy`, indeterminate).
2. **The walk.** For each root, `ScanFolderContents(root, rootParent: root, …)` runs. At every visited directory:
   - each subdirectory contributes a `SubfolderLocation { ParentPath = rootParent, FullPath = subDir }` filed under its **name** in an `OrdinalIgnoreCase` dictionary, and is recursed into when `recurse` is true;
   - each file in that directory (`SearchOption.TopDirectoryOnly`) contributes a location filed under `Path.GetExtension(file)`, with an empty extension mapped to the literal key `"(no extension)"`.

   `ParentPath` always names the **root** the location was reached from, never the immediate parent. Any exception at a directory is swallowed and that directory is skipped.
3. **Ordering.** Both inventories are ordered by occurrence count **descending**, then by name **ascending**.
4. **`Count` is an occurrence count, not a folder count.** It is the number of `Locations`. With **Include Subdirectories** on, the same name occurring twice inside one root counts twice. (The `SubfolderItem.Count` XML doc and the SCAN FOLDERS help popup both say *"how many result folders contain this subfolder"*, which is only true when the scan is non-recursive.)
5. **Sizes.** A subfolder item's `TotalSize` is the sum of `GetDirectorySize(location.FullPath)` — a recursive file-size sum — over its locations. A file-type item's `TotalSize` is the sum of `SafeFileSize(location.FullPath)`, which returns `0` for a file it cannot stat.
6. **Scan status.** `"Found N subfolder names (<size>) and M file types (<size>) across K folders."`, with both sizes rendered through the `SubfolderItem.TotalSizeDisplay` formatter.
7. **Filtering and paging inside an item** (`SubfolderItem`): `PageSize` is the constant `50`. `LocationFilter` matches a location when **either** `FullPath` **or** `ParentPath` contains the filter text (`OrdinalIgnoreCase`); `null` is coerced to `""`. Assigning a *different* filter resets `CurrentPage` to `0` and raises seven notifications (`CurrentPage`, `TotalPages`, `FilteredCount`, `PagedLocations`, `PageStatus`, `CanGoNextPage`, `CanGoPrevPage`); assigning the same value does nothing. `TotalPages` is `ceil(FilteredCount / 50)` and `1` when the filtered set is empty. `PagedLocations` is `Skip(CurrentPage * 50).Take(50)`. `PageStatus` is `"No matches"` when nothing matches, else `"Page X of Y · N result(s)"` with the plural `s` dropped at exactly one result. `NextPage()`/`PrevPage()` are no-ops at the bounds.
8. **Clear Selected Subfolders — pre-count.** The checked subfolder **names** and the checked **result folders** are captured; if either is empty the command returns. On a background thread the app counts, for every `(resultFolder, name)` pair, whether `Directory.Exists(Path.Combine(resultFolder, name))`. A count of `0` sets `"No matching subfolders found to delete."` and stops before any dialog.
9. **Confirmation.** A `MessageBox` (`YesNo`, `Warning`) states the total count, the checked names, and how many selected folders are involved, and says the folders can be restored from the Recycle Bin. Anything other than `Yes` aborts.
10. **Deletion sweep.** `BeginBusy("Recycling subfolders…", total, "subfolders")`, then on a background thread for every `(resultFolder, name)` pair: skip if the path no longer exists; else `RecycleDirectory(subPath)` → record the path and increment `deleted`; any exception → increment `failed`. Progress is reported every `max(1, total / 100)` items and unconditionally on the last one. `EndBusy()` runs in `finally`.
11. **History.** If at least one path was recycled, one `ActionHistoryEntry { Kind = RecycleDirectories, RecycledPaths, Summary = "Recycled N subfolders (<names>)" }` is inserted at index 0 of `ActionHistory`. `PushHistory` trims the list to `MaxHistoryEntries = 30` and calls `SaveSettings()`.
12. **Result status and rescan.** `"Cleared N subfolders."`, plus `" M failed (locked or access-denied)."` when anything failed. `ScanSubfolders()` is then called again so both inventories reflect the deletions.
13. **Clear Selected Files** follows the same shape with three differences: the target paths are **every `Locations` entry of every checked file-type item** (so files are deleted at whatever depth the scan reached, not only at depth 1); the confirmation states the total file count and the extension list; the history entry is `Kind = RecycleFiles` with `Summary = "Recycled N files (<extensions>)"`. It also ends with `ScanSubfolders()`.
14. **Undo.** `UndoEntry` handles `RecycleFiles` and `RecycleDirectories` identically: `RestoreFromRecycleBin(entry.RecycledPaths)` opens the Recycle Bin through late-bound `Shell.Application` `NameSpace(10)`, reads each item's original location with `GetDetailsOf(item, 1)`, and calls `InvokeVerb("&Restore")` on the items whose original folder + name is in the wanted set — iterating **backwards**, because restoring mutates the collection. The status reports `restored` vs `failed` (`failed` = requested minus restored). The entry is then removed from history and settings are saved.

**Invariants**

- **Deletion is always to the Recycle Bin.** Both primitives pass `RecycleOption.SendToRecycleBin`; `UIOption.OnlyErrorDialogs` suppresses the per-item shell prompt but not error dialogs. No code path in this feature calls `Directory.Delete` or `File.Delete` on user content.
- **Clear Selected Subfolders only ever targets a direct child of a checked result folder** — the path it deletes is exactly `Path.Combine(resultFolder, checkedName)`. The discovered `Locations` are used for display only.
- **Clear Selected Files targets exactly the paths the scan discovered** — every `Location.FullPath` of every checked extension.
- Every clear action that removed at least one item pushes exactly one history entry; an action that removed nothing pushes none.
- `ActionHistory` never exceeds 30 entries, newest first, and is persisted with the settings ([SPEC-009](SPEC-009-settings-and-window-state-persistence.md)).
- A failure on one item never aborts the sweep — failures are counted and reported, never thrown. Nothing is logged.
- The scan itself never writes to disk.
- `SubfolderItem.Display` is `"{Name} ({Count})"` — pinned by `SubfolderItemTests.Display_ShouldCombineNameAndCount`.
- `SubfolderItem.TotalSizeDisplay` uses the same B → KB/MB/GB/TB/PB `0.##` formatter as `FolderSearchResult` — pinned by `SubfolderItemTests.TotalSizeDisplay_Bytes_ShouldShowB`, `_Kilobytes_ShouldShowKB`, `_LargerUnits_ShouldScale`, `_VeryLargeBytes_ShouldCapAtPB`, `_FractionalMB_ShouldFormat`.
- Paging never throws and never leaves a page out of range — pinned by `SubfolderItemTests.NextPage_OnLastPage_ShouldBeNoOp`, `PrevPage_OnFirstPage_ShouldBeNoOp`, `NextPage_ShouldAdvanceAndShowNextSlice`, `PrevPage_ShouldMoveBack`, `PagedLocations_WithFilter_SkipsCorrectly`.
- Filter behavior is pinned by `LocationFilter_FiltersAndResetsPage`, `LocationFilter_SameValue_DoesNotReset`, `LocationFilter_NullTreatedAsEmpty`, `LocationFilter_MatchesParentPath`; empty-state behavior by `EmptyLocations_TotalPagesIsOne_PageStatusNoMatches`; pluralization by `PageStatus_SingleResult_UsesSingular` / `PageStatus_MultipleResults_UsesPlural`.

**Edge cases**

| Case | Behavior |
|------|----------|
| No folders checked in the results | **Scan Folders** is disabled (`CanExecute` false) |
| No names checked in an inventory | The matching clear button is disabled |
| A checked name exists only at depth ≥ 2 | The pre-count finds nothing at depth 1 → `"No matching subfolders found to delete."` and **nothing is deleted** (see *Not implemented*) |
| A subfolder is locked or in use | `RecycleDirectory` throws, `failed` increments, the sweep continues; status ends `"M failed (locked or access-denied)."` |
| Recycle Bin disabled, full, or the item exceeds its quota | The VB call throws (Windows may show its own error dialog) and the item counts as failed |
| Access denied while scanning a directory | That directory is skipped; the rest of the inventory is still built |
| Filter matches nothing inside an expanded item | `PageStatus` = `"No matches"`, `TotalPages` = 1, `PagedLocations` empty |
| Select-all with a filter active | Only the **filtered** items are selected; **Clear selection** always clears the **whole** collection — the two are deliberately asymmetric |
| Two checked result folders where one nests the other | The inner folder's contents are counted under both roots, so occurrence counts and sizes double-count |
| File with no extension | Filed under the literal key `"(no extension)"` and deletable like any other type |
| Recycle Bin emptied before undo | `RestoreFromRecycleBin` finds no match; status reports `"N not found in Recycle Bin (may have been emptied)"` |

**Not implemented**

- **Nested occurrences are discovered but not deletable.** With **Include Subdirectories** on, the inventory lists names found at any depth, but `ClearSelectedSubfolders` only probes `Path.Combine(resultFolder, name)` — depth 1. A name that exists only deeper is shown with a non-zero count and then reports `"No matching subfolders found to delete."` Clearing *files* does not have this limitation; it uses the discovered paths.
- **The confirmation never lists the exact paths.** It states counts and names only, so the user cannot review precisely what will go. There is no dry-run mode.
- **No guard on where clearing may happen.** There is no allow-list of roots, no block on drive roots or system directories (`C:\Windows`, `Program Files`), and no cap on how many items a single confirmation authorizes — see [`../SECURITY.md`](../SECURITY.md).
- **Failures are counted, never explained.** Every catch is bare (`catch { failed++; }`); the cause of a failure is not surfaced or logged.
- **Undo is best-effort and locale-fragile.** `RestoreFromRecycleBin` assumes Recycle-Bin detail column 1 is "Original Location", which is locale- and OS-dependent; when the assumption fails it silently restores nothing and reports every item as not found.
- **`FolderSearchIncludeSubdirectories` is not persisted.** It resets to `true` on every launch, so a scan configuration is not fully reproducible from a saved profile ([SPEC-009](SPEC-009-settings-and-window-state-persistence.md)).
- **Neither inventory is persisted.** `DiscoveredSubfolders` / `DiscoveredFileTypes` are rebuilt from scratch each time, and are cleared by `ClearFolderSearch` and by any profile switch.
- **No unit tests cover the scan or the clear actions.** `MainViewModel` is `[ExcludeFromCodeCoverage]` ([ADR-011](../adr/ADR-011-coverage-via-collector-and-script.md)); only the `SubfolderItem` / `SubfolderLocation` models are tested.

## Links

- Decisions: [ADR-002 — Hand-rolled MVVM](../adr/ADR-002-hand-rolled-mvvm.md) · [ADR-004 — All I/O behind `IFileSystemService`](../adr/ADR-004-ifilesystemservice-io-abstraction.md) · [ADR-011 — coverage measured by coverlet.collector, enforced by script](../adr/ADR-011-coverage-via-collector-and-script.md) · [ADR-006 — Persist settings on every mutation](../adr/ADR-006-persist-settings-on-every-mutation.md)
- Module docs: [WindowsFileManager (WPF UI)](../modules/ui.md) · [WindowsFileManager.Core](../modules/core.md) · [WindowsFileManager.Infrastructure](../modules/infrastructure.md)
- Related specs: [SPEC-007 — Folder search](SPEC-007-folder-search.md) · [SPEC-004 — Selection and file actions](SPEC-004-selection-and-file-actions.md) · [SPEC-009 — Settings and window-state persistence](SPEC-009-settings-and-window-state-persistence.md) · [SPEC-010 — Contextual help](SPEC-010-contextual-help.md)
- Background: [`../CONTEXT.md`](../CONTEXT.md) · [`../SECURITY.md`](../SECURITY.md)
- Tests: `tests/WindowsFileManager.Tests/Models/SubfolderItemTests.cs` · `tests/WindowsFileManager.Tests/Models/SubfolderLocationTests.cs` · `tests/WindowsFileManager.Tests/Models/ActionHistoryEntryTests.cs`
