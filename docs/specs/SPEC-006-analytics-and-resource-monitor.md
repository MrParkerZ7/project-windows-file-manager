# SPEC-006 — Analytics and resource monitor

<!-- Sync contract: `## Current behavior & invariants` is the section a behavior-changing commit must update in the same commit. -->

- Status: Current
- Feature area: shell/UI
- Ships in: 1.0.0 — analytics from the initial commit `57de160`, live resource monitor from `98f16c5`

## What

Two independent read-only readouts. Neither changes any file.

- **Analytics dashboard** — a 320-pixel side panel that summarises the last completed scan: how many files were scanned, how many were duplicates, how many groups they formed, how long the scan took, how much space the duplicates waste and what share of the scanned set they are. Below the summary sit three visual breakdowns: a wasted-vs-total bar, the top duplicate file types by size, and a fixed six-bucket size distribution.
- **Resource monitor** — three text readouts (`RAM: …`, `CPU: …%`, `Threads: …`) that refresh every two seconds for the whole life of the process, rendered in both the global status bar and the Duplication tab's own status bar.

## Why

A duplicate scan produces two questions the group list cannot answer. *Was this worth it?* — the wasted-space and duplicate-percentage numbers say whether there is anything to clean up before the user reads a single row. *Where should I start?* — the top-types and size-distribution breakdowns point at the extensions and size bands holding the most space, which is where the same effort recovers the most disk.

The resource monitor exists because the scan is I/O- and CPU-heavy: it hashes the full content of every size-colliding file. On a large tree the app can look frozen. A live RAM/CPU/thread readout is the cheapest possible signal that it is working rather than hung, and it also makes memory regressions visible during development without attaching a profiler.

## Scope

### In

- `ScanAnalytics` and its two child types (`ExtensionStat`, `SizeBucket`) — every derived statistic, including the exact formulas and rounding.
- When analytics are computed, reset, and rendered.
- The analytics panel's visibility, its layout, and the `PercentToWidthConverter` that sizes both bar charts.
- The resource monitor's sampling timer, the CPU/RAM/thread formulas, and its two render sites.

### Out

- The scan that produces the `ScanResult` these statistics are derived from — [SPEC-001](SPEC-001-duplicate-detection.md).
- The per-scan busy bar, its count text and its ETA (`BeginBusy` / `BusyEta`), which report *progress*, not results. Those belong to the actions that raise them — [SPEC-004](SPEC-004-selection-and-file-actions.md) and [SPEC-008](SPEC-008-clear-subfolders.md).
- The `DuplicateGroup.WastedBytes` rule that `TotalWastedBytes` sums — [SPEC-001](SPEC-001-duplicate-detection.md).
- The preview panel that occupies the *other* side column — [SPEC-005](SPEC-005-file-preview.md).
- The action-history counters on the History tab (`HistoryTotalEntries`, `HistoryMove*`, `HistoryRecycle*`) — [SPEC-004](SPEC-004-selection-and-file-actions.md).
- **Non-goal:** analytics are not persisted, not exported, and not comparable across runs. They describe exactly one scan and are discarded when the next one starts.
- **Non-goal:** the resource monitor measures **this process only**. It is not a system monitor and never reports machine-wide load.

## Current behavior & invariants

`ScanAnalytics`, `ExtensionStat` and `SizeBucket` live in `src/WindowsFileManager.Core/Models/ScanAnalytics.cs` and are **fully covered** — `ScanAnalyticsTests` pins every formula named below. The panel wiring lives in `src/WindowsFileManager/ViewModels/MainViewModel.cs` and `src/WindowsFileManager/Views/MainWindow.xaml`; the resource sampler is `MainViewModel.UpdateResourceInfo()`. `MainViewModel` is `[ExcludeFromCodeCoverage]`, so the wiring and the sampler are not covered. `PercentToWidthConverter` (`src/WindowsFileManager/Helpers/Converters.cs`) is covered by `PercentToWidthConverterTests`.

**Entry points**

| Trigger | Handler | Notes |
|---------|---------|-------|
| Scan starts | `ScanAsync()` | Sets `Analytics = null` before any work |
| Scan completes successfully | `ScanAsync()` | `Analytics = ScanAnalytics.FromResult(result)` |
| `Analytics` toggle button | `IsAnalyticsVisible` (two-way `ToggleButton`) | Shows/hides the panel |
| `✕ Close` in the panel header | `CloseAnalytics_Click` (code-behind) | Sets `IsAnalyticsVisible = false` |
| Window loaded | `MainWindow_Loaded` | Saves the current value, then forces `IsAnalyticsVisible = false` because `Folder` is the first tab |
| Tab changed | `TabControl_SelectionChanged` | Hides on the `Folder` tab, restores the saved value otherwise |
| Every 2 s, always | `_resourceTimer.Tick` → `UpdateResourceInfo()` | Started in the constructor, never stopped |

**Rules — computing analytics**

1. `ScanAnalytics.FromResult(result)` is a pure static function of a `ScanResult`. It first flattens `result.DuplicateGroups.SelectMany(g => g.Files)` into `allDuplicateFiles` and sums their sizes into `totalSize`. Every statistic below is derived from those two values plus the result's own counters — **it never re-reads the filesystem**.
2. `TotalFiles = result.TotalFilesScanned`, `TotalDuplicates = result.TotalDuplicates`, `DuplicateGroups = result.DuplicateGroups.Count`, `WastedBytes = result.TotalWastedBytes`, `Duration = result.Duration` — straight copies.
3. `UniqueFiles = TotalFilesScanned - TotalDuplicates + DuplicateGroups.Count`, on the assumption that exactly one file per group is the "original" that survives a cleanup. Pinned by `ScanAnalyticsTests.UniqueFiles_ShouldCalculateCorrectly` (50 − 10 + 2 = 42).
4. `TotalSizeBytes` is the sum of the **duplicate** files' sizes only — *not* the size of everything scanned, despite the property's XML comment saying "all scanned files". Pinned by `FromResult_WithDuplicates_ShouldComputeCorrectly` (500 + 500 + 2000 × 3 = 7000 from a 100-file scan).
5. `DuplicatePercentage = TotalDuplicates / TotalFilesScanned × 100`, or `0` when `TotalFilesScanned == 0`.
6. `WastedPercentage = TotalWastedBytes / TotalSizeBytes × 100`, or `0` when `TotalSizeBytes == 0`. Because rule 4 makes the denominator the duplicates' own total, this is *"of the space the duplicates occupy, how much is redundant"* — not a share of the whole scan.
7. `TopExtensions` groups `allDuplicateFiles` by `Path.GetExtension(f.FileName).TrimStart('.').ToUpperInvariant()`, mapping an empty result to the literal `"(no ext)"`, then orders by `TotalSize` descending and takes **at most 8**. Pinned by `FromResult_ShouldComputeTopExtensions` (`PDF` before `TXT` before `(no ext)`).
8. `SizeDistribution` is **always exactly six buckets**, in this fixed order, each a half-open `[Min, Max)` interval:

   | Index | Label | Range |
   |---|---|---|
   | 0 | `< 1 KB` | `[0, 1024)` |
   | 1 | `1 KB – 100 KB` | `[1024, 102400)` |
   | 2 | `100 KB – 1 MB` | `[102400, 1048576)` |
   | 3 | `1 MB – 10 MB` | `[1048576, 10485760)` |
   | 4 | `10 MB – 100 MB` | `[10485760, 104857600)` |
   | 5 | `100 MB+` | `[104857600, long.MaxValue)` |

   Bucket membership counts `allDuplicateFiles` only. Pinned by `FromResult_ShouldComputeSizeDistribution`; the six-bucket count holds even for an empty result (`FromResult_EmptyResult_ShouldReturnZeros`).
9. `SizeBucket.BarWidth = FileCount / maxCount × 100` where `maxCount` is the largest bucket's count, or `0` for every bucket when `maxCount == 0`. So the fullest bucket is always exactly `100`. Pinned by `FromResult_SizeDistribution_BarWidthRelativeToMax` (4 files → `100`, 1 file → `25`).
10. `FormattedTotalSize`, `FormattedWastedSize` and `ExtensionStat.FormattedSize` all delegate to `ScannedFile.FormatFileSize` — the single formatter used throughout the app. Pinned by `FormattedTotalSize_ShouldFormat` / `FormattedWastedSize_ShouldFormat`.

**Rules — rendering analytics**

11. `ScanAsync` sets `Analytics = null` as part of its reset block (alongside `FilesScanned = 0`, clearing `DuplicateGroups`, and `MiniPreviewConverter.ClearCache()`), and assigns the new value only on the success path. A cancelled or failed scan therefore leaves `Analytics` at `null`.
12. The panel is a fixed `Width="320"` `Border` in `Grid.Column="1"`, wrapped in a `ScrollViewer`, whose inner `StackPanel` sets `DataContext="{Binding Analytics}"`. Its `Visibility` is bound to `IsAnalyticsVisible` alone — **not** to whether `Analytics` is non-null.
13. Six summary cards render `TotalFiles` (`N0`), `TotalDuplicates` (`N0`), `DuplicateGroups` (`N0`), `Duration` (`mm\:ss\.f`), `FormattedWastedSize`, and `DuplicatePercentage` (`F1` with a `%` suffix).
14. Both bar charts size their fill `Border` with `PercentToWidthConverter.Instance`, a `MarkupExtension` + `IMultiValueConverter` taking `[percent, containerActualWidth]`: it returns `containerWidth × percent / 100`, clamped to `[0, containerWidth]`, and `0.0` whenever there are fewer than two values, either value is not a `double`, or `containerWidth <= 0`. Pinned across `PercentToWidthConverterTests` (valid, zero, full, over-100 clamp, zero-width, short array, non-double, `ConvertBack` throws, `ProvideValue` returns the singleton).
15. The wasted-vs-total bar binds `WastedPercentage`; the size-distribution bars bind each bucket's `BarWidth`. Both take their container width from the nearest ancestor `Grid`'s `ActualWidth`.
16. `IsAnalyticsVisible` defaults to `true` in the field initialiser, but `MainWindow_Loaded` snapshots it into `_savedAnalyticsVisible` and then forces it to `false`, because `Folder` is the first tab. `TabControl_SelectionChanged` re-saves and clears it on the `Folder` tab and restores `_savedAnalyticsVisible` on any other tab.
17. `IsAnalyticsVisible` is **not** persisted — it is absent from `ProfileSettings` and from `SnapshotLiveStateInto`, so it resets to the load-time behavior every launch ([SPEC-009](SPEC-009-settings-and-window-state-persistence.md)).
18. The analytics panel and the 640-pixel Folder Action panel both occupy `Grid.Column="1"` and are both always in the visual tree; only their `Visibility` bindings (`IsAnalyticsVisible` vs `IsFolderControlActive`) keep them apart.

**Rules — resource monitor**

19. The `MainViewModel` constructor captures `Process.GetCurrentProcess()`, seeds `_lastCpuTime = TotalProcessorTime` and `_lastCheckTime = DateTime.UtcNow`, calls `UpdateResourceInfo()` once immediately, then starts a `DispatcherTimer` with `Interval = 2 s`. The timer is never stopped or disposed — it runs for the process lifetime, scanning or idle.
20. `UpdateResourceInfo()` calls `_currentProcess.Refresh()` first, then:
    - **Memory** — `WorkingSet64 / 1048576`; formatted `"RAM: {F0} MB"` below 1024 MB, otherwise `"RAM: {value/1024:F1} GB"`.
    - **CPU** — `elapsed = (now − _lastCheckTime).TotalMilliseconds`; when `elapsed > 0`, `cpuPercent = (TotalProcessorTime − _lastCpuTime).TotalMilliseconds / (Environment.ProcessorCount × elapsed) × 100`, formatted `"CPU: {F1}%"`. The value is normalised by core count, so a fully saturated 8-core machine reads `100.0%`, not `800%`. `_lastCpuTime` and `_lastCheckTime` are then advanced **unconditionally**.
    - **Threads** — `"Threads: {Threads.Count}"`.
21. The whole body is wrapped in `try { … } catch { }` with the comment *"Process may have been disposed"* — a sampling failure leaves the previous readout on screen and is never surfaced.
22. All three strings render in two places: the global status bar (`ResourceMemory` in `#1565C0`, `ResourceCpu` in `#E65100`, `ResourceThreads` in `#666666`) and the Duplication tab's own row-2 status bar, with the same colours. They are plain one-way `TextBlock` bindings.

**Invariants**

- `ScanAnalytics.FromResult` is pure: same `ScanResult` in, same analytics out, no I/O, no clock, no globals. This is what makes it fully testable and 100% covered.
- `SizeDistribution.Count == 6` for every input, including an empty `ScanResult` — the six labels and their boundaries are a fixed contract that the UI's `ItemsControl` renders without any null or count guard.
- `TopExtensions.Count <= 8` always, and is ordered by `TotalSize` descending.
- Every percentage the model produces is already scaled to `0–100`; `PercentToWidthConverter` assumes that scale and clamps anything above it.
- `BarWidth` is relative to the largest bucket, never to the total — the tallest bar is always full-width.
- The analytics model is derived from the scan result **only**. Deleting or moving files afterwards ([SPEC-004](SPEC-004-selection-and-file-actions.md)) mutates `DuplicateGroups` but never re-computes `Analytics`, so the panel keeps showing the pre-action numbers until the next scan.
- The resource monitor never writes, never allocates a background thread, and never reports anything outside this process.

**Edge cases**

| Case | Behavior |
|------|----------|
| Panel opened before any scan | `Analytics` is `null`, so the inner `StackPanel`'s `DataContext` is null and every binding renders empty. The panel's chrome, labels and six empty distribution rows still draw — there is no "no data yet" state. |
| Scan finds no duplicates | All counters are `0`, both percentages are `0` (guarded division), `TopExtensions` is empty, and all six buckets show `0` with `BarWidth = 0`. |
| Scan cancelled or failed | `Analytics` stays `null` from the reset at the top of `ScanAsync`; the previous scan's numbers are **not** restored. |
| Scan longer than 60 minutes | The `Duration` card's `mm\:ss\.f` format has no hours component, so the display wraps — a 61-minute scan reads as `01:00.0`. The underlying `TimeSpan` is correct. |
| Files with no extension | Grouped under the literal `"(no ext)"` in `TopExtensions`. Pinned by `FromResult_ShouldComputeTopExtensions`. |
| More than 8 distinct duplicate extensions | Only the 8 largest by total size are listed; the remainder are silently omitted with no "other" row. |
| A file exactly at a bucket boundary (e.g. 1024 bytes) | Lands in the **upper** bucket — the intervals are `[Min, Max)`. |
| Regex-match scan mode | Group members may have different sizes, so `TotalSizeBytes` and the distribution reflect the real per-file sizes rather than one size per group ([SPEC-001](SPEC-001-duplicate-detection.md)). |
| First CPU sample | Computed over the sub-millisecond window between the constructor's seed and the immediate `UpdateResourceInfo()` call, so the first value is not meaningful. The 2-second ticks that follow are. |
| System clock adjusted backwards | `elapsed` can go negative, the `elapsed > 0` guard skips the CPU update, and `_lastCheckTime` still advances — one sample is skipped, the next recovers. |
| Bar container not yet measured | `ActualWidth` is `0`, the converter returns `0.0`, and the bar draws empty until the layout pass gives it a width. |
| `History` tab selected | Its header is a `TextBlock`, not a `string`, so `TabControl_SelectionChanged` takes the *else* branch and **restores** the analytics panel next to the history content instead of hiding it. |

**Not implemented**

- **`TotalSizeBytes` does not match its own documentation.** The XML comment reads *"the total size of all scanned files"*; the code sums only the duplicate files. The tests pin the code, not the comment. Fixing the comment is safe; changing the computation would change `WastedPercentage`.
- **Analytics are never refreshed after an action.** There is no recompute hook on delete or move; the only way to get current numbers is to re-scan.
- **No export.** The dashboard is on-screen only — no CSV, no report, no copy-to-clipboard.
- **No history or comparison.** Nothing is stored between scans, so there is no trend view.
- **The resource monitor cannot be turned off** and has no toggle, no interval setting, and no persistence. It ticks every 2 seconds regardless of what the app is doing.

## Links

- Decisions: [ADR-001 — four-module Clean Architecture](../adr/ADR-001-clean-architecture-four-modules.md) (why `ScanAnalytics` is a pure Core model rather than view logic), [ADR-002 — hand-rolled MVVM](../adr/ADR-002-hand-rolled-mvvm.md) (why `PercentToWidthConverter` is a `MarkupExtension` singleton and `CloseAnalytics_Click` is code-behind)
- Module docs: [Core](../modules/core.md) (`ScanAnalytics`, `ExtensionStat`, `SizeBucket`), [UI](../modules/ui.md) (converters, panel layout, the dispatcher timer)
- Related specs: [SPEC-001](SPEC-001-duplicate-detection.md) · [SPEC-002](SPEC-002-filtering-and-sorting.md) · [SPEC-004](SPEC-004-selection-and-file-actions.md) · [SPEC-005](SPEC-005-file-preview.md) · [SPEC-009](SPEC-009-settings-and-window-state-persistence.md)
- Tests: `tests/WindowsFileManager.Tests/Models/ScanAnalyticsTests.cs` · `tests/WindowsFileManager.Tests/Helpers/PercentToWidthConverterTests.cs` · `tests/WindowsFileManager.Tests/Models/ScanResultTests.cs`
