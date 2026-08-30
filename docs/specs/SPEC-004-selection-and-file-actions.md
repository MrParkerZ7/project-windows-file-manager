# SPEC-004 — Selection and file actions

<!-- Sync contract: `## Current behavior & invariants` is the section a behavior-changing commit must update in the same commit. -->

- Status: Current
- Feature area: duplicate scanning (acting on results)
- Ships in: 1.0.0 — Recycle Bin semantics, undo, and background progress landed after 1.0.0 (`Unreleased`; commits `567ac3c`, `ab4925e`); the per-file size column landed in `7123c0a` (`Unreleased`)

## What

After a scan produces duplicate groups ([SPEC-001](SPEC-001-duplicate-detection.md)), the user decides which copies to keep and what to do with the rest. Every file row carries a checkbox; toolbar buttons set that checkbox in bulk; then two actions — delete and move — operate on everything checked, across every group at once.

- Check or uncheck any individual file.
- Bulk-select: **all** files, the **newer** duplicates (keeping the oldest in each group), or the **older** duplicates (keeping the newest); or clear the whole selection.
- Select or clear only the files inside one group.
- **Delete** — send checked files to the Recycle Bin. Also available per-file and per-group ("Delete Both" / "Delete All (N)").
- **Move** — relocate checked files into a target folder, creating it if it does not exist and renaming on collision.
- **Open** — reveal a file in Windows Explorer with the file pre-selected.
- **Undo** — reverse the last delete or move from the action history.

## Why

A duplicate finder is only useful if acting on the findings is faster than acting by hand. The dangerous part is not finding duplicates — it is deleting the wrong copy. So selection is deliberately separated from action (commit `e3fa45c`): the user builds a selection with cheap, reversible clicks, sees the running count, and only then commits to an action behind a confirmation dialog.

The keep-oldest / keep-newest bulk selectors encode the two decisions people actually make about identical files, so the common case is one click instead of one click per group. Deletion goes to the Recycle Bin rather than unlinking, and every delete and move is recorded in an undo history, because the cost of a wrong bulk action on a user's own files is unrecoverable data loss.

## Scope

### In

- The per-file `IsFileSelected` flag and every command that sets it.
- Bulk selection (all / newer / older / clear) and per-group selection.
- Recycle-based deletion: per file, per group, and for the whole selection.
- Moving the selection to a target folder, including target creation and collision renaming.
- Reveal-in-Explorer for a duplicate file.
- The action-history entries these actions push, and undoing them.

### Out

- Which groups are *visible*. The extension / size / count filters and the sort order belong to [SPEC-002](SPEC-002-filtering-and-sorting.md). Note that selection deliberately ignores the visible filter — see the rules below.
- Rule-driven selection (`Apply Rules`, include/exclude patterns) — [SPEC-003](SPEC-003-custom-filter-rules.md). This spec only documents the *ignore* pass that the bulk selectors invoke.
- The preview panel and the `👁 Preview` button on each file row — [SPEC-005](SPEC-005-file-preview.md).
- Folder-level actions (clear subfolders, flatten, link siblings) and the permanent `Directory.Delete` inside the flatten flow — [SPEC-008](SPEC-008-clear-subfolders.md).
- Persisting `MoveTargetPath` and the action history to disk — [SPEC-009](SPEC-009-settings-and-window-state-persistence.md).
- The help popup wording shown next to these buttons — [SPEC-010](SPEC-010-contextual-help.md).
- **Non-goal:** there is no "move oldest / move newest / move by filename / move by path" variant. Those commands existed briefly (commit `30ef875`) and were removed in `e3fa45c` when selection was decoupled from action. Age-based intent is expressed through the *selection* commands, then a single Move.

## Current behavior & invariants

All behavior below lives in `src/WindowsFileManager/ViewModels/MainViewModel.cs` unless another file is named. The whole `MainViewModel` type is marked `[ExcludeFromCodeCoverage]`, so **none of the command logic in this spec is covered by an automated test** — the tests named below pin only the Core models these commands read and write. Treat this section as the contract a change must not silently break.

**Entry points**

| Trigger | Handler | CanExecute |
|---------|---------|------------|
| `✓ All` (Action bar) | `SelectAllFilesCommand` → `SelectAllFiles()` | `DuplicateGroups.Count > 0` |
| `✓ Newer` | `SelectNewerFilesCommand` → `SelectNewerFiles()` | `DuplicateGroups.Count > 0` |
| `✓ Older` | `SelectOlderFilesCommand` → `SelectOlderFiles()` | `DuplicateGroups.Count > 0` |
| `✕ Clear` | `ClearFileSelectionCommand` → `ClearFileSelection()` | always |
| Per-group `✓ Select All` | `SelectAllInGroupCommand` → `SelectAllInGroup(group)` | always; `CommandParameter="{Binding}"` = the group |
| Per-group `✕ Clear` | `ClearSelectionInGroupCommand` → `ClearSelectionInGroup(group)` | always |
| Per-file checkbox | two-way binding on `ScannedFile.IsFileSelected` | — |
| `🗑 Delete` (Action bar) | `DeleteSelectedFilesCommand` → `DeleteSelectedFiles()` | `SelectedFileCount > 0` |
| Per-group `🗑 Delete Both` / `🗑 Delete All (N)` | `DeleteAllInGroupCommand` → `DeleteAllInGroup(group)` | `!IsScanning` |
| Per-file `🗑 Delete` | `DeleteFileCommand` → `DeleteFile(file)` | `!IsScanning` |
| `📁 Move` | `MoveSelectedFilesCommand` → `MoveSelectedFiles()` | `SelectedFileCount > 0` |
| `...` (browse) | `BrowseMoveTargetCommand` → `BrowseMoveTarget()` | always |
| Per-file `📂 Open` | `OpenFileLocationCommand` → `OpenFileLocation(path)` | always |
| `↶` (two copies in the Action bar) | `UndoLastActionCommand` → `UndoLastAction()` | always |

**Rules — selection**

1. Selection state is one boolean per file: `ScannedFile.IsFileSelected` (`src/WindowsFileManager.Core/Models/ScannedFile.cs`). It is the only property on `ScannedFile` that raises `PropertyChanged`, and it raises **only when the value actually changes** — pinned by `ScannedFileTests.IsFileSelected_WhenChanged_ShouldRaisePropertyChanged`.
2. Every bulk selector iterates `DuplicateGroups` — the backing `ObservableCollection`, **not** the filtered `FilteredDuplicateGroups` view. Files inside groups hidden by an active filter ([SPEC-002](SPEC-002-filtering-and-sorting.md)) are therefore selected too, and a subsequent Delete or Move will act on them.
3. `SelectAllFiles()` sets `IsFileSelected = true` on every file in every group, then runs `ApplyIgnoreRules()`.
4. `SelectNewerFiles()` clears the whole selection first, then per group with `Count > 1`: `oldest = Files.OrderBy(f => f.LastModified).First()`, and every *other* file is selected. Groups with `Count <= 1` are skipped entirely. Then `ApplyIgnoreRules()` runs.
5. `SelectOlderFiles()` is the mirror image: `newest = Files.OrderByDescending(f => f.LastModified).First()` is left unselected, everything else is selected. Then `ApplyIgnoreRules()`.
6. `ApplyIgnoreRules()` takes `FilterRules.Where(r => r.Action == FilterAction.Exclude)` — **`IsEnabled` is not consulted here**, unlike `ApplyFilterRules()` in [SPEC-003](SPEC-003-custom-filter-rules.md), which skips disabled rules. A *disabled* exclude rule still deselects. For each currently-selected file it matches `FileName` or `FilePath` (per the rule's `Target`) with `MatchesFilter`, deselects on the first matching rule, and returns the number deselected. With no exclude rules it returns `0` immediately.
7. `StatusMessage` after a bulk selector names the count and, when `ignored > 0`, appends `" (N excluded by ignore rules)"`. `SelectAllFiles` reports the post-ignore `SelectedFileCount`; the newer/older selectors report their own tally minus `ignored`.
8. `SelectAllInGroup` / `ClearSelectionInGroup` set the flag on the passed group's files only, and **do not** run `ApplyIgnoreRules()`. A `null` parameter is a silent no-op.
9. `RefreshSelectedFileCount()` recomputes `SelectedFileCount` by a full `DuplicateGroups.SelectMany(g => g.Files).Count(f => f.IsFileSelected)`. It runs after every command in this spec that mutates selection or removes files. `HasSelectedFiles` is derived from it and re-raised in its setter. Ticking a checkbox directly does **not** call it — the count refreshes on the next command.

**Rules — delete (all three paths recycle)**

10. The single primitive is `RecycleFile(path)` → `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)`. No delete path in this spec calls `File.Delete`. `OnlyErrorDialogs` suppresses the shell's own per-item confirmation.
11. `DeleteFile(file)` — `null` is a no-op. Shows a Yes/No `MessageBox` titled *Confirm Recycle File* with `MessageBoxImage.Warning` and the **full path**. On Yes: recycle → push one `ActionHistoryKind.RecycleFiles` entry (`Summary = "Recycled <filename>"`) → remove the file from the first group that contains it, dropping that group from `DuplicateGroups` when its `Files.Count <= 1`, then `break`. Any exception sets `StatusMessage = "Failed to delete: <message>"` and pushes no history.
12. `DeleteAllInGroup(group)` — `null` or empty group is a no-op. The confirmation (*Confirm Recycle All*, Warning) lists **every** path in the group. Each file is recycled in its own `try`/`catch`, tallying `deleted` / `failed`; only the succeeded paths go into one `RecycleFiles` history entry. The **whole group is then removed** from `DuplicateGroups` regardless of failures, and `ClosePreview()` runs.
13. `DeleteSelectedFiles()` (`async void`) — collects `(group, file)` pairs for every selected file across `DuplicateGroups`; zero selected is a silent no-op. The confirmation shows **only the count** (`"Send N selected files to the Recycle Bin?"`), not the paths. Work runs on `Task.Run` between `BeginBusy("Recycling duplicate files…", N, "files")` and an `EndBusy()` in `finally`; progress is reported every `Math.Max(1, N / 100)` items and once at the end. Afterwards only files whose recycle **succeeded** are removed from their group, groups left with `Files.Count <= 1` are dropped, one `RecycleFiles` history entry holds the succeeded set, `ClosePreview()` runs, and the count is refreshed.

**Rules — move**

14. `MoveSelectedFiles()` (`async void`) rejects a whitespace `MoveTargetPath` with `StatusMessage = "Set a move target path first."`.
15. `EnsureMoveTargetDirectory()` returns `true` when the directory exists, otherwise calls `Directory.CreateDirectory(MoveTargetPath)` — **any typed path is created without validation**. A failure sets `StatusMessage = "Cannot create target folder: <message>"` and aborts the move.
16. Zero selected files is a silent no-op. The confirmation (*Confirm Move*, `MessageBoxImage.Question`) shows the count and the target path.
17. Per file: `dest = Path.Combine(target, Path.GetFileName(src))`; if `File.Exists(dest)` the name becomes `{nameWithoutExtension}_{DateTime.Now:HHmmss}{extension}`; then `File.Move(src, dest)`. Failures are counted, never surfaced with a cause. Same busy-bar and progress cadence as delete.
18. Only files that actually moved are removed from their group and recorded as `ActionHistoryMove { Source, Destination }` pairs inside one `ActionHistoryKind.MoveFiles` entry. Groups left with `Files.Count <= 1` are dropped, `ClosePreview()` runs, the count is refreshed.
19. `BrowseMoveTarget()` opens `Microsoft.Win32.OpenFolderDialog` and, on OK, writes `dialog.FolderName` into `MoveTargetPath`. The text box itself is free-form (`UpdateSourceTrigger=PropertyChanged`).

**Rules — open in Explorer**

20. `OpenFileLocation(path)` — no-op on null/empty; otherwise `Process.Start("explorer.exe", "/select,\"<path>\"")`, which opens the containing folder with the file highlighted. The sibling `OpenFolderLocation(path)` (`explorer.exe "<path>"`) serves the folder-search results of [SPEC-007](SPEC-007-folder-search.md). Both build a single argument string by interpolation with manual quoting rather than `ProcessStartInfo.ArgumentList` — see [`../SECURITY.md`](../SECURITY.md).

**Rules — undo**

21. `PushHistory(entry)` inserts at index 0, trims the tail beyond `MaxHistoryEntries = 30`, re-raises `UndoTooltip`, and calls `SaveSettings()` — so history is persisted immediately ([SPEC-009](SPEC-009-settings-and-window-state-persistence.md)).
22. `UndoLastAction()` undoes `ActionHistory[0]`; `UndoSpecificAction(entry)` undoes a named entry, ignoring one not in the list. Both delegate to `UndoEntry`, which removes the entry and saves afterwards.
23. `UndoEntry` for `MoveFiles` replays `entry.Moves` **in reverse index order**, so the `_HHmmss` collision renames unwind in the order they were applied. A move whose `Source` already exists again is counted as failed and skipped; a missing source directory is recreated first.
24. `UndoEntry` for `RecycleFiles` and `RecycleDirectories` both call `RestoreFromRecycleBin(entry.RecycledPaths)` — late-bound `Shell.Application`, `NameSpace(10)`, matching on `GetDetailsOf(item, 1)` (the Recycle Bin's *Original Location* column) and invoking the `&Restore` verb. Failures are silent; the status line reports `restored` vs `not found in Recycle Bin`.
25. `ActionHistoryEntry.ItemCount` switches on `Kind`: `MoveFiles → Moves.Count`, `CreateShortcuts → CreatedShortcuts.Count`, and **both recycle kinds fall through to `RecycledPaths.Count`** — pinned by `ActionHistoryEntryTests.ItemCount_MoveFiles_…`, `…_RecycleFiles_…`, `…_RecycleDirectories_…`, `…_CreateShortcuts_…`.
26. `ActionHistoryKind` ordinals are frozen at `MoveFiles=0, RecycleFiles=1, RecycleDirectories=2, CreateShortcuts=3` because `System.Text.Json` serializes them as numbers into `settings.json` — pinned by `ActionHistoryEntryTests.ActionHistoryKind_Ordinals_Preserved`.

**Invariants**

- **Every destructive action in this spec is recoverable.** Deletes go to the Recycle Bin; moves are recorded as source→destination pairs. No code path here performs a permanent delete.
- **Every destructive action is confirmed.** All three delete paths and the move path show a modal `MessageBox` before touching the filesystem; there is no bypass flag.
- **Every action that changes files pushes exactly one history entry**, containing only the items that actually succeeded — never the attempted set.
- **A group never remains in `DuplicateGroups` with one file after an action**: each path prunes `Files.Count <= 1` groups. (Deleting *into* a one-file state is only possible through these commands; a fresh scan never emits such a group.)
- `DuplicateGroup.DeleteAllLabel` is `"🗑 Delete Both"` at exactly two files and `"🗑 Delete All (N)"` otherwise — pinned by `DuplicateGroupTests.DeleteAllLabel_TwoFiles_ShouldSayDeleteBoth` and `DeleteAllLabel_ThreeOrMore_ShouldShowCount`.
- `MaxHistoryEntries = 30`; the oldest entry is dropped once the cap is exceeded, which means an old action can become permanently un-undoable while its files sit in the Recycle Bin.

**Edge cases**

| Case | Behavior |
|------|----------|
| Nothing selected, Delete or Move clicked | The button is disabled (`CanExecute` requires `SelectedFileCount > 0`); the handlers also re-check and return silently. |
| Group with a single file and `✓ Newer` / `✓ Older` | Skipped — no file in it is selected. |
| Two files in a group share the same `LastModified` | `OrderBy`/`OrderByDescending` are stable, so the first in list order (which is `FilePath`-ascending from the scan) is the one kept. |
| Delete all copies in a group | Allowed. `DeleteAllInGroup` and a full selection both remove every copy — there is **no keep-one enforcement**. |
| Some files in a group fail to recycle | The failures are tallied into the status line, but `DeleteAllInGroup` removes the entire group from the list anyway — surviving files disappear from the UI until the next scan. |
| Move collision inside the same second | The `_HHmmss` suffix is not unique within one second, so a second collision throws and is counted as a silent failure. |
| Move target path does not exist | Created silently by `Directory.CreateDirectory` — including a typo'd path. |
| Move target is on another volume | `File.Move` performs a copy-and-delete; slow but correct. Failures are counted, not explained. |
| Undo a move whose source path was re-created | Counted as failed; the moved file stays where it is. |
| Undo a recycle after the Recycle Bin was emptied | `RestoreFromRecycleBin` finds no match; the status line reports `N not found in Recycle Bin (may have been emptied)`. |
| Any failure in a bulk loop | Counted (`failed++`) and never logged or attributed — the user sees a count, not a cause. |
| An action runs while a filter hides groups | The action still applies to the hidden selections (rule 2). |
| Delete or Move completes | `ClosePreview()` runs, which also sets `IsAutoPreview = false` — a side effect that silently disables auto-preview ([SPEC-005](SPEC-005-file-preview.md)). |

**Not implemented**

- **Move-by-age / by-name / by-path variants do not exist.** The `1.0.0` CHANGELOG entry still lists *"Granular move options: move by oldest, newest, filename, or path"*; those commands were removed in `e3fa45c` before that entry was written. The CHANGELOG line is stale — no such command is bound in `MainWindow.xaml` and no such method exists in `MainViewModel`.
- **The `Action` help popup contradicts the code.** Its text (`MainWindow.xaml`, the `Action` section's `HelpButtonStyle` tag) says Delete *"Permanently removes checked files from disk. No Recycle Bin — cannot be undone."* Every delete path recycles and is undoable. The popup text is wrong; the behavior above is correct. See [SPEC-010](SPEC-010-contextual-help.md).
- **No dry-run, no path allow-list, no system-path guard.** Nothing prevents targeting `C:\Windows`, a drive root, or a UNC path; one confirmation can authorize an unbounded number of items. See [`../SECURITY.md`](../SECURITY.md).
- **Failures are counted, never diagnosed.** Every bulk loop is `catch { failed++; }`. There is no log, no per-file error list, and no retry.

## Links

- Decisions: [ADR-002 — hand-rolled MVVM](../adr/ADR-002-hand-rolled-mvvm.md) (why these are `RelayCommand`s with `CommandManager`-driven `CanExecute`), [ADR-006 — persist settings on every mutation](../adr/ADR-006-persist-settings-on-every-mutation.md) (why `PushHistory` saves immediately), [ADR-007 — `System.Text.Json` settings compatibility](../adr/ADR-007-system-text-json-settings-compatibility.md) (why `ActionHistoryKind` ordinals are frozen)
- Module docs: [UI](../modules/ui.md) (commands, busy bar, COM interop), [Core](../modules/core.md) (`ScannedFile`, `DuplicateGroup`, `ActionHistoryEntry`)
- Related specs: [SPEC-001](SPEC-001-duplicate-detection.md) · [SPEC-002](SPEC-002-filtering-and-sorting.md) · [SPEC-003](SPEC-003-custom-filter-rules.md) · [SPEC-005](SPEC-005-file-preview.md) · [SPEC-008](SPEC-008-clear-subfolders.md) · [SPEC-009](SPEC-009-settings-and-window-state-persistence.md) · [SPEC-010](SPEC-010-contextual-help.md)
- Guardrails: [`../SECURITY.md`](../SECURITY.md)
- Tests: `tests/WindowsFileManager.Tests/Models/ScannedFileTests.cs` · `tests/WindowsFileManager.Tests/Models/DuplicateGroupTests.cs` · `tests/WindowsFileManager.Tests/Models/ActionHistoryEntryTests.cs`
