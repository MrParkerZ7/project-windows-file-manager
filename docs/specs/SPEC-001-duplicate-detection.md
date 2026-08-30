# SPEC-001 — Duplicate detection

<!-- Sync contract: `## Current behavior & invariants` is the section a behavior-changing commit must update in the same commit. -->

- Status: Current
- Feature area: duplicate scanning
- Ships in: **1.0.0** (size + content-hash detection). Match-by-name-regex mode ships in **Unreleased** — added by commit `cd541cc`, after the 1.0.0 baseline commit `53bfad1`.

## What

The user picks one or more folders, presses **Scan**, and the app reports groups of files that are duplicates of each other, ordered so the group wasting the most disk space is first. A live counter shows how many files have been examined, and **Cancel** stops the scan at any point.

Two matching modes exist and are mutually exclusive — the toolbar shows one or the other:

- **Base Filters mode (default)** — a file is a duplicate of another only if their contents are byte-identical. Size is used as a cheap pre-filter; only same-size files are hashed.
- **Match by Name Regex mode** — files are grouped by what a user-supplied regular expression captures from their *file name*. Size and content are never read. Two files of different size and different content group together if their captures are equal.

The scan itself also honors folder-level options: recurse into subfolders or not, and skip subfolders whose *name* appears in an exclude list (`node_modules`, `.git`, …).

## Why

Disk space disappears into copies a user cannot see: the same photo pulled off a camera twice, a project folder duplicated as a manual backup, an export re-run into a second location. Filename comparison finds none of them (copies get renamed) and eye-balling folders does not scale. Hashing every file would be correct but too slow on a large tree, so the size pre-filter buys correctness at a fraction of the I/O.

The regex mode exists for the *opposite* problem — files that are semantically the same item (a `.jpg` and the `.png` re-export of the same asset, an invoice saved twice under different suffixes) but not byte-identical. Content hashing can never find those, so the user supplies the identity rule themselves.

## Scope

### In

- Enumerating candidate files across one or more target folders, with recursion and folder-name exclusions.
- Deciding which files belong to the same duplicate group (size + SHA-256 content hash, or name-regex captures).
- Computing per-group and per-scan wasted-space figures.
- Progress reporting and cancellation during a scan.
- The result ordering the UI receives (groups by wasted space descending; files inside a group by path ascending).

### Out

- Narrowing or re-ordering the *results* after a scan — extension filters, minimum size, minimum duplicate count, and sort order are [SPEC-002](SPEC-002-filtering-and-sorting.md). Those are display filters over an already-finished scan and never re-read the filesystem.
- Choosing *which* file in a group to act on, and the delete/move/reveal actions — [SPEC-004](SPEC-004-selection-and-file-actions.md).
- The statistics dashboard derived from a finished scan — [SPEC-006](SPEC-006-analytics-and-resource-monitor.md).
- Finding *folders* by name/content rules — [SPEC-007](SPEC-007-folder-search.md). Folder search is a separate walk with separate semantics.
- Persisting the scan configuration — [SPEC-009](SPEC-009-settings-and-window-state-persistence.md).
- Any similarity matching beyond exact byte equality or regex-capture equality. There is no fuzzy/perceptual/near-duplicate detection and none is scaffolded.

## Current behavior & invariants

**Entry points**

| Trigger | Handler | Notes |
|---------|---------|-------|
| **Scan** button (`ScanCommand`) | `MainViewModel.ScanAsync()` | `async void`; builds `ScanOptions`, runs the engine on `Task.Run` |
| **Cancel** button (`CancelCommand`) | `MainViewModel.Cancel()` | cancels the scan's `CancellationTokenSource` |
| **🔄 Use Regex** / **🔄 Use Base Filters** (`ToggleDuplicateMatchModeCommand`) | flips `MainViewModel.DuplicateMatchByRegex` | the two toolbars are mutually exclusive in XAML via `BoolToVisibility` / `InverseBoolToVisibility` |
| Engine | `DuplicateScannerService.Scan(ScanOptions, Action<int>?, CancellationToken)` | synchronous; the only public method |
| Hashing | `FileHashService.ComputeHash(string)` | `IFileSystemService.OpenRead` → `SHA256.HashData(stream)` → `Convert.ToHexString` (uppercase hex) |
| All filesystem access | `IFileSystemService` | the sole I/O seam; concrete impl `WindowsFileManager.Infrastructure.Services.FileSystemService` |

**Rules**

1. **Guards run before anything else.** `TargetPaths.Count == 0` throws `ArgumentException("At least one target path is required.")`. Then *every* path is checked with `DirectoryExists`; the first failure throws `DirectoryNotFoundException($"Directory not found: {path}")`. Both guards complete before any enumeration, so a scan of three folders where the third does not exist enumerates nothing. Pinned by `Scan_EmptyTargetPaths_ShouldThrow`, `Scan_DirectoryNotFound_ShouldThrow`, `Scan_MultipleFolders_OneNotFound_ShouldThrow`.
2. **Timing starts after the guards.** The `Stopwatch` is started once the paths validate, and stopped *after grouping but before the final sort* — `ScanResult.Duration` therefore excludes the wasted-space ordering pass.
3. **Enumeration takes one of two paths.** If `ExcludeFolderNames` is non-empty **and** `IncludeSubdirectories` is true, a private recursive walker (`EnumerateFilesExcluding`) yields the top-directory files, then recurses into `EnumerateDirectories` skipping any directory whose `Path.GetFileName` is in an `OrdinalIgnoreCase` set. Otherwise a single `EnumerateFiles(path, "*.*", AllDirectories | TopDirectoryOnly)` per target. Exclusions are therefore *bypassed* when `IncludeSubdirectories` is false — inert in practice, because that mode visits no subdirectory at all.
4. **Overlapping targets are de-duplicated exactly once, at enumeration.** The per-target sequences are concatenated and passed through `.Distinct(StringComparer.OrdinalIgnoreCase)`, so a file reachable from both `C:\root` and `C:\root\sub` is counted and hashed once. Pinned by `Scan_OverlappingPaths_ShouldDeduplicateFiles`.
5. **Per-file collection applies two filters, in order.** For each enumerated path: `cancellationToken.ThrowIfCancellationRequested()`; drop the file if `GetFileSize < MinimumFileSize`; drop it if `FileExtensions` is non-empty and the extension (leading dot trimmed, compared `OrdinalIgnoreCase`) is not listed. Survivors become a `ScannedFile` carrying path, name, size and `GetLastWriteTime`. Pinned by `Scan_MinimumFileSize_ShouldFilterSmallFiles`, `Scan_FileExtensionFilter_ShouldFilterByExtension`, `Scan_FileExtensionFilter_CaseInsensitive`.
6. **`TotalFilesScanned` counts kept files, not enumerated files.** The counter increments only after both filters pass.
7. **Progress fires every 100 kept files, plus one unconditional final call.** 250 kept files produce `100, 200, 250`; 2 kept files produce just `2`. Pinned by `Scan_ProgressCallback_ShouldThrottleEvery100Files` and `Scan_ProgressCallback_ShouldReport`.
8. **Grouping mode is chosen by `MatchRegex`.** Non-whitespace → `GroupByNameRegex`; otherwise → `GroupBySizeAndHash`.
9. **Size + hash mode (`GroupBySizeAndHash`).** Group the collected files by `FileSize` and keep only groups of ≥ 2 — this is the cheap filter, and a lone file of unique size is never opened. Per size-group: `ThrowIfCancellationRequested()`, then hash **every** file in that group, then re-group by `Hash` and keep groups of ≥ 2. Same size + different content yields no group (`Scan_SameSizeDifferentContent_ShouldNotBeDuplicates`). Files inside a group are ordered by `FilePath` ascending (`Scan_DuplicateFiles_ShouldBeSortedByPath`). The group's `FileSize` is the shared size; its `Hash` is the shared content hash.
10. **Regex mode (`GroupByNameRegex`).** The pattern is compiled once as `new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))` and matched against **`FileName` only** — never the full path, never the content. Non-matching files are silently skipped (`Scan_RegexMode_FilesNotMatching_ShouldBeSkipped`). The group key is the concatenation of capture groups 1..n when the pattern has any, else the full match value (`Scan_RegexMode_GroupsFilesByCaptures_IgnoringSize`, `Scan_RegexMode_NoCaptureGroups_UsesFullMatchAsKey`). Keys are bucketed in an **`Ordinal` (case-sensitive)** dictionary; buckets of < 2 are dropped. The group's `Hash` is the key and its `FileSize` is the **max** of its members' sizes. Case-insensitivity is the caller's job via an inline `(?i)` flag — pinned by `Scan_RegexMode_CaseSensitiveByDefault`, where `ABC-1.txt` and `abc-1.txt` do *not* group under pattern `(abc)`.
11. **Regex failures surface as `ArgumentException`, both of them.** An uncompilable pattern → `ArgumentException("Invalid duplicate-match regex: …")`. A `RegexMatchTimeoutException` on any single file → `ArgumentException("Duplicate-match regex timed out on '<file>'. …")`, which aborts the whole scan. Pinned by `Scan_RegexMode_InvalidPattern_ShouldThrowArgumentException` and `Scan_RegexMode_CatastrophicBacktracking_ShouldThrowArgumentException` (pattern `(a+)+X` over a 60-character name).
12. **Wasted space per group** (`DuplicateGroup.WastedBytes`): 0 when the group holds ≤ 1 file; otherwise `sum(FileSize) − max(FileSize)` when that sum is greater than zero; falling back to `FileSize × (Count − 1)` when every member's individual size is zero. Both branches are pinned — `WastedBytes_ShouldCalculateCorrectly` (fallback: 1000 × 2 → 2000) and `WastedBytes_MixedSizes_UsesSumMinusLargest` (100 + 200 + 1000, max 1000 → 300). In hash mode the two formulas agree because all sizes are equal; in regex mode they do not, and sum-minus-largest is the one that applies.
13. **Result assembly.** Groups are ordered by `WastedBytes` **descending** (`Scan_MultipleDuplicateGroups_ShouldSortByWastedSpace`); `TotalDuplicates` is the sum of every group's `Count`; `TotalWastedBytes` is the sum of every group's `WastedBytes`.
14. **UI-side scan orchestration** (`MainViewModel.ScanAsync`): `CanScan()` is `!IsScanning && TargetPaths.Any(t => t.IsEnabled)`. The run clears `DuplicateGroups`, clears the thumbnail cache (`MiniPreviewConverter.ClearCache()`), nulls `Analytics`, sets `StatusMessage = "Scanning..."` and calls `SaveSettings()` before starting. `ScanOptions` is populated from **enabled** target paths and **enabled** exclude names only. `MatchRegex` is supplied only when `DuplicateMatchByRegex` is true **and** `DuplicateMatchRegex` is non-whitespace — otherwise the app falls back to size + hash. The progress callback assigns `FilesScanned`.
15. **Three exception classes are caught and shown as status text, not dialogs.** `OperationCanceledException` → `"Scan cancelled."`; `DirectoryNotFoundException` and `ArgumentException` (including both regex failures) → the exception's own message. `finally` always resets `IsScanning` and disposes the token source.

**Invariants**

- A `DuplicateGroup` always contains at least two files. Both grouping paths drop buckets of size < 2 before constructing a group.
- In size + hash mode, two files are in the same group **iff** their full contents are byte-identical. The hash is SHA-256 over the entire stream, uppercase hex; it is never truncated and never sampled.
- A file is hashed only if at least one other kept file has exactly its byte size. A unique-sized file is never opened.
- In regex mode neither size nor content is read — grouping depends on the file *name* alone. `DuplicateGroup.Hash` in that mode holds the regex key, not a content hash.
- `ScanResult.TotalFilesScanned` ≤ number of enumerated paths, always, because both filters can only remove.
- Cancellation is observed per enumerated file, per size-group before hashing, and per file during regex matching. It is **not** observed inside the hashing of one file, nor inside a single `Regex.Match` — the 1-second `MatchTimeout` is what bounds the latter, and the code comment above `GroupByNameRegex` records this as the reason it exists.
- The engine never writes: no file is created, moved, or deleted by a scan.
- The engine touches the filesystem only through `IFileSystemService`, which is what makes the 30+ scanner tests possible against a `Mock<IFileSystemService>` with no disk.

**Edge cases**

| Case | Behavior |
|------|----------|
| Empty directory | `ScanResult` with all counters zero and no groups; no exception (`Scan_EmptyDirectory_ShouldReturnEmptyResult`) |
| No duplicates found | Empty `DuplicateGroups`, `TotalFilesScanned` still reports every kept file (`Scan_NoDuplicates_ShouldReturnEmptyGroups`) |
| Token already cancelled before the call | `OperationCanceledException` on the first file, before any hash is computed (`Scan_Cancellation_ShouldThrow`) |
| `IncludeSubdirectories = false` | `EnumerateFiles` is called exactly once per target with `SearchOption.TopDirectoryOnly` (`Scan_TopDirectoryOnly_ShouldNotRecurse`) |
| Excluded folder name | The walker never calls `EnumerateDirectories` inside it — asserted with `Times.Never` in `Scan_WithExcludeFolderNames_ShouldSkipExcludedSubfolders` |
| `UnauthorizedAccessException` / `IOException` while listing subdirectories in exclusion mode | That branch of the walk stops (`yield break`); the exception does not propagate and the scan completes (`Scan_WithExcludeFolderNames_UnauthorizedAccess_ShouldYieldBreak`, `..._IOException_ShouldYieldBreak`) |
| Duplicates split across two different target folders | Grouped together — grouping is by content, not by folder (`Scan_MultipleFolders_ShouldFindCrossFolderDuplicates`) |
| Zero-byte file | Excluded by the default `MinimumFileSize = 1`. If the minimum is lowered, `ComputeHash` still returns a valid non-empty hash for an empty stream (`ComputeHash_EmptyFile_ShouldReturnHash`) |
| A group whose files all report size 0 | `WastedBytes` falls back to `FileSize × (Count − 1)` rather than returning 0 |
| Regex mode, pattern matches only one file | No group — a bucket of one is dropped like any other |

**Not implemented**

- **`ScanOptions.MinimumFileSize` and `ScanOptions.FileExtensions` are unreachable from the running app.** Both are fully implemented and tested in `DuplicateScannerService`, but `MainViewModel.ScanAsync` populates only `TargetPaths`, `IncludeSubdirectories`, `ExcludeFolderNames` and `MatchRegex`. Every scan from the UI therefore runs with the defaults — minimum size 1 byte, no extension restriction. `ProfileSettings.MinimumFileSize` (default 1) *is* persisted and *is* copied when a profile is cloned (`MainViewModel.CloneProfile`), but nothing ever reads it into a scan. The minimum-size and extension controls the user actually sees are post-scan display filters and belong to [SPEC-002](SPEC-002-filtering-and-sorting.md).
- **The regex group-key separator does not match its own comment.** `BuildRegexKey`'s comment states captures are "joined with SOH (0x01) so ('ab','c') and ('a','bc') stay distinct", but the code is `string.Join("", parts)` — an empty separator. Those two capture tuples therefore collide into one group. No test pins either behavior, so this is a latent discrepancy, not a contract.
- **Regex-mode timeout aborts the entire scan.** The 1-second budget is per *match*, so a pathological pattern costs up to 1 second per file until the first timeout, at which point the whole scan throws. There is no per-file skip-and-continue path.
- **No cap on hashed file size, and no exclusion of sensitive locations.** Every size-colliding file is streamed end-to-end. There is no allow-list of scannable roots and no block on system paths — see [SPEC-004](SPEC-004-selection-and-file-actions.md) and [`../SECURITY.md`](../SECURITY.md) for the destructive-action side of that surface.
- **No near-duplicate / perceptual matching.** Exact bytes or regex captures are the only two identity rules; nothing else is scaffolded.

## Links

- Decisions: [ADR-003 — Three-stage duplicate detection](../adr/ADR-003-three-stage-duplicate-detection.md) · [ADR-004 — All I/O behind `IFileSystemService`](../adr/ADR-004-ifilesystemservice-io-abstraction.md) · [ADR-001 — Clean Architecture with four modules](../adr/ADR-001-clean-architecture-four-modules.md)
- Module docs: [WindowsFileManager.Application](../modules/application.md) · [WindowsFileManager.Core](../modules/core.md) · [WindowsFileManager (WPF UI)](../modules/ui.md)
- Related specs: [SPEC-002 — Filtering and sorting](SPEC-002-filtering-and-sorting.md) · [SPEC-003 — Custom filter rules](SPEC-003-custom-filter-rules.md) · [SPEC-004 — Selection and file actions](SPEC-004-selection-and-file-actions.md) · [SPEC-006 — Analytics and resource monitor](SPEC-006-analytics-and-resource-monitor.md) · [SPEC-009 — Settings and window-state persistence](SPEC-009-settings-and-window-state-persistence.md)
- Background: [`../CONTEXT.md`](../CONTEXT.md)
- Tests: `tests/WindowsFileManager.Tests/Services/DuplicateScannerServiceTests.cs` · `tests/WindowsFileManager.Tests/Services/FileHashServiceTests.cs` · `tests/WindowsFileManager.Tests/Models/DuplicateGroupTests.cs` · `tests/WindowsFileManager.Tests/Models/ScanOptionsTests.cs` · `tests/WindowsFileManager.Tests/Models/ScanResultTests.cs`
