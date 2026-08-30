# WindowsFileManager.Core

`src/WindowsFileManager.Core/` — the innermost layer.

## Purpose

Core holds the domain vocabulary of the application: the shapes that describe a scan request, a scan result, a group of duplicate files, a user-authored filter rule, a persisted profile, and a reversible action. It also declares `IFileSystemService`, the single port through which every filesystem operation in the system passes.

It contains no behavior that touches the disk, the network, the clock as a dependency, or the UI. Everything here is data, computed properties over that data, and change notification.

## Design

Two namespaces, one file per public type:

```
WindowsFileManager.Core/
  Models/        WindowsFileManager.Core.Models    — 12 files, 20 public types
  Services/      WindowsFileManager.Core.Services  — IFileSystemService (the port)
```

**Dependency rule: Core depends on nothing.** `WindowsFileManager.Core.csproj` declares zero `ProjectReference` and zero `PackageReference` elements of its own, so Core takes no runtime dependency. The one `PackageReference` it still receives is StyleCop.Analyzers 1.1.118, injected into every project by the root `Directory.Build.props` — a build-time analyzer carrying `PrivateAssets=all`, not a runtime dependency. Its only using-directives resolve to the base class library (`System.ComponentModel`, `System.Text.Json.Serialization`, `System.IO.Path`, `System.Linq`). The TFM is `net8.0-windows` but `UseWPF` is not set, so no WPF assembly is referenced.

Three design choices shape the models:

1. **Change notification without a framework.** Models the UI binds to and mutates (`ScannedFile`, `FilterRule`, `FolderSearchPattern`, `FolderSearchResult`, `SubfolderItem`) implement `INotifyPropertyChanged` by hand and raise the event **only on an actual value change** (`if (_field != value)`). Each type notifies for the specific properties the UI edits, not for every property.

2. **Derived values are computed properties, not stored fields.** `DuplicateGroup.Count`, `WastedBytes`, `FormattedWastedSize`, `ScanAnalytics.FormattedTotalSize`, `SubfolderItem.PagedLocations` and friends recompute on read. Nothing needs to be kept in sync, and the JSON writer is told to skip the ones that would pollute the settings file.

3. **Serialization stability is a first-class constraint.** These types are the on-disk schema of `%APPDATA%\WindowsFileManager\settings.json`. Enum members are written by `System.Text.Json` as **ordinals**, so member order is load-bearing; display-only members carry `[JsonIgnore]`. See [Rules & constraints](#rules--constraints).

`InternalsVisibleTo` is granted to `WindowsFileManager.Application` and `WindowsFileManager.Tests`.

## Key types

| Type | File | Responsibility |
|------|------|----------------|
| `ScannedFile` | `Models/ScannedFile.cs` | One file discovered by a scan: path, name, size, hash, last-modified, plus the `IsFileSelected` checkbox state. Owns the canonical `FormatFileSize` formatter. |
| `ScanOptions` | `Models/ScanOptions.cs` | The scan request: target paths, recursion flag, minimum size, extension allow-list, excluded folder names, optional name-match regex. |
| `ScanResult` | `Models/ScanResult.cs` | The scan response: counts, wasted bytes, duplicate groups, duration. |
| `DuplicateGroup` | `Models/DuplicateGroup.cs` | A set of files considered duplicates of each other, keyed by `Hash`, with derived count/label/extension/wasted-space. |
| `ScanAnalytics` | `Models/ScanAnalytics.cs` | Dashboard projection built from a `ScanResult` by the static `FromResult`. Ships with `ExtensionStat` and `SizeBucket`. |
| `FilterRule` | `Models/FilterRule.cs` | One user-authored selection rule: pattern, regex/case flags, `FilterAction`, `FilterTarget`. |
| `FolderSearchPattern` | `Models/FolderSearchPattern.cs` | One folder-search criterion: pattern text plus a `FolderMatchType`. |
| `FolderSearchResult` | `Models/FolderSearchResult.cs` | One folder matched by a search: paths, matched pattern, selection state, recursive size. |
| `SubfolderItem` / `SubfolderLocation` | `Models/SubfolderItem.cs` | A repeated subfolder name found across search results, with the full client-side filter + paging model the UI binds to. |
| `ActionHistoryEntry` / `ActionHistoryMove` | `Models/ActionHistoryEntry.cs` | One reversible operation on the undo stack, discriminated by `ActionHistoryKind`. |
| `ProfileSettings` | `Models/ProfileSettings.cs` | One named workflow profile — 22 persisted properties covering every per-tab setting. |
| `AppSettings` | `Models/AppSettings.cs` | The root persisted document: profiles, active profile name, global action history, global window geometry. |
| `IFileSystemService` | `Services/IFileSystemService.cs` | The filesystem port. 11 members. The only I/O abstraction in the system. |

## Public API

### Enums (ordinals are the on-disk contract)

| Enum | Members in declaration order | Serialized as |
|------|------------------------------|---------------|
| `ActionHistoryKind` | `MoveFiles`, `RecycleFiles`, `RecycleDirectories`, `CreateShortcuts` | 0, 1, 2, 3 |
| `FolderMatchType` | `Include`, `Match`, `Contains`, `Exclude`, `Mismatch`, `NotContain` | 0, 1, 2, 3, 4, 5 |
| `FilterAction` | `Include`, `Exclude` | 0, 1 |
| `FilterTarget` | `Filename`, `Filepath` | 0, 1 |

`FolderMatchType` has **six** members. All six are exposed in the UI's `FolderMatchTypes` list and implemented in the search walker.

### `ScannedFile`

```csharp
public class ScannedFile : INotifyPropertyChanged

public bool     IsFileSelected { get; set; }   // the only change-notifying property
public string   FilePath       { get; set; }
public string   FileName       { get; set; }
public long     FileSize       { get; set; }
public string   Hash           { get; set; }
public DateTime LastModified   { get; set; }
public string   FormattedSize  { get; }        // => FormatFileSize(FileSize)

public static string FormatFileSize(long bytes)
```

`FormatFileSize` is the project's canonical size formatter and is reused by `ScanResult`, `DuplicateGroup`, `ScanAnalytics`, `ExtensionStat`, and the UI's `ExtensionFilter`:

| Input range | Output |
|-------------|--------|
| `bytes < 1024` | `"{bytes} B"` |
| `< 1 MB` | `"{n:F1} KB"` — `1024` → `"1.0 KB"` |
| `< 1 GB` | `"{n:F1} MB"` |
| otherwise | `"{n:F2} GB"` — `1073741824` → `"1.00 GB"` |

`FolderSearchResult.TotalSizeDisplay` and `SubfolderItem.TotalSizeDisplay` use a **different, second** formatter (a divide-by-1024 loop over KB/MB/GB/TB/PB with a `0.##` format, producing `"1 KB"` and `"1.5 GB"`). The two are not interchangeable; do not "unify" them without checking the tests that pin both.

### `ScanOptions`

```csharp
public List<string> TargetPaths           { get; set; } = new();
public bool         IncludeSubdirectories { get; set; } = true;
public long         MinimumFileSize       { get; set; } = 1;
public List<string> FileExtensions        { get; set; } = new();   // empty = all
public List<string> ExcludeFolderNames    { get; set; } = new();
public string       MatchRegex            { get; set; } = string.Empty;
```

`MatchRegex` non-empty switches the scanner into name-regex grouping; empty selects size + hash grouping. See [application.md](application.md#duplicatescannerservice).

### `ScanResult`

```csharp
public int                 TotalFilesScanned   { get; set; }
public int                 TotalDuplicates     { get; set; }
public long                TotalWastedBytes    { get; set; }
public List<DuplicateGroup> DuplicateGroups    { get; set; } = new();
public TimeSpan            Duration            { get; set; }
public string              FormattedWastedSize { get; }
```

### `DuplicateGroup`

```csharp
public string            Hash      { get; set; }
public long              FileSize  { get; set; }
public List<ScannedFile> Files     { get; set; } = new();

public int     Count               { get; }   // Files.Count
public string? FirstFilePath       { get; }   // null when Files is empty
public string  FirstFileName       { get; }   // "" when Files is empty
public string  DeleteAllLabel      { get; }   // "🗑 Delete Both" at Count == 2, else "🗑 Delete All (N)"
public string  FileExtension       { get; }   // lower-invariant extension of Files[0].FilePath
public long    WastedBytes         { get; }
public string  FormattedWastedSize { get; }
public string  FormattedFileSize   { get; }
```

`WastedBytes` has three branches and both non-trivial ones are pinned by tests:

1. `Files.Count <= 1` → `0`.
2. `Files.Sum(FileSize) > 0` → `sum - Files.Max(FileSize)` — "all copies except the largest". For hash mode all sizes are equal so this equals `FileSize * (Count - 1)`; for regex mode (mixed sizes) it is the honest sum-minus-largest.
3. Otherwise (every individual size is `0`) → fallback `FileSize * (Count - 1)`, which keeps legacy and hand-built fixtures meaningful.

### `ScanAnalytics`

```csharp
public static ScanAnalytics FromResult(ScanResult result)
```

Computes, from the duplicate files only (not every scanned file):

| Property | Rule |
|----------|------|
| `TotalFiles` | `result.TotalFilesScanned` |
| `TotalDuplicates` | `result.TotalDuplicates` |
| `UniqueFiles` | `TotalFilesScanned - TotalDuplicates + DuplicateGroups.Count` — one file per group counts as the surviving "original" |
| `DuplicateGroups` | `result.DuplicateGroups.Count` |
| `WastedBytes` | `result.TotalWastedBytes` |
| `TotalSizeBytes` | sum of `FileSize` over **duplicate** files only |
| `DuplicatePercentage` | `TotalDuplicates / TotalFilesScanned * 100`, or `0` when nothing was scanned |
| `WastedPercentage` | `TotalWastedBytes / TotalSizeBytes * 100`, or `0` when `TotalSizeBytes == 0` |
| `TopExtensions` | duplicates grouped by uppercased extension without the dot (blank → `"(no ext)"`), ordered by `TotalSize` descending, `Take(8)` |
| `SizeDistribution` | **always exactly 6** buckets, even for an empty result |

The six buckets are fixed and half-open `[min, max)`: `< 1 KB`, `1 KB – 100 KB`, `100 KB – 1 MB`, `1 MB – 10 MB`, `10 MB – 100 MB`, `100 MB+`. Each bucket's `BarWidth` is `count / maxCount * 100`, or `0` when the largest bucket is empty.

`ExtensionStat` carries `Extension`, `FileCount`, `TotalSize`, `FormattedSize`. `SizeBucket` carries `Label`, `FileCount`, `BarWidth`.

### `FilterRule`

```csharp
public class FilterRule : INotifyPropertyChanged

[JsonIgnore] public int    Priority      { get; set; }   // display order only
             public string Pattern       { get; set; } = string.Empty;
             public bool   IsEnabled     { get; set; } = true;   // the only notifying property
             public bool   IsRegex       { get; set; }           // default false
             public bool   IgnoreCase    { get; set; } = true;
             public FilterAction Action  { get; set; } = FilterAction.Include;
             public FilterTarget Target  { get; set; } = FilterTarget.Filename;
[JsonIgnore] public string DisplaySummary { get; }
```

`DisplaySummary` renders `{Action} | {Target} | "{Pattern}"` with an optional ` [Regex, IgnoreCase]` suffix. Both `Priority` and `DisplaySummary` are `[JsonIgnore]`d — they must not appear in a written settings file, and an older file that still contains them must still deserialize.

### `FolderSearchPattern`

```csharp
             public string         Pattern   { get; set; } = string.Empty;
[JsonIgnore] public int            Priority  { get; set; }                     // notifies
             public bool           IsEnabled { get; set; } = true;              // notifies
             public FolderMatchType MatchType { get; set; } = FolderMatchType.Match;  // notifies
```

Default `MatchType` is `Match` (exact name equality), not `Include`.

### `FolderSearchResult`

```csharp
public bool   IsSelected       { get; set; }   // notifies
public string FullPath         { get; set; }
public string FolderName       { get; set; }
public string ParentPath       { get; set; }
public string MatchedPattern   { get; set; }
public long   TotalSize        { get; set; }   // notifies TWICE
public string TotalSizeDisplay { get; }
```

Setting `TotalSize` raises `PropertyChanged` for `TotalSize` **and then** for `TotalSizeDisplay`, in that order. A test asserts the exact sequence; dropping the second raise silently stops the size column from updating.

### `SubfolderItem` / `SubfolderLocation`

```csharp
public const int PageSize = 50;

public string  Name             { get; set; }
public int     Count            { get; set; }
public long    TotalSize        { get; set; }
public string  TotalSizeDisplay { get; }
public List<SubfolderLocation> Locations { get; set; } = new();
public bool    IsSelected       { get; set; }
public string  Display          { get; }   // "{Name} ({Count})"

public string  LocationFilter   { get; set; }   // null coerced to ""; setting it resets CurrentPage to 0
public int     CurrentPage      { get; }        // 0-based
public int     TotalPages       { get; }        // 1 when there are no results
public int     FilteredCount    { get; }
public IEnumerable<SubfolderLocation> PagedLocations { get; }
public string  PageStatus       { get; }
public bool    CanGoNextPage    { get; }
public bool    CanGoPrevPage    { get; }

public void NextPage();   // no-op at the last page
public void PrevPage();   // no-op at the first page
```

`LocationFilter` matches `FullPath` **or** `ParentPath`, `OrdinalIgnoreCase`. `PageStatus` is `"No matches"` when empty, otherwise `"Page X of Y · N result(s)"` with correct singular/plural. Any paging change raises seven `PropertyChanged` events (`CurrentPage`, `TotalPages`, `FilteredCount`, `PagedLocations`, `PageStatus`, `CanGoNextPage`, `CanGoPrevPage`).

`SubfolderLocation` is a two-string record-like class: `ParentPath`, `FullPath`.

### `ActionHistoryEntry`

```csharp
public ActionHistoryKind        Kind             { get; set; }
public List<ActionHistoryMove>  Moves            { get; set; } = new();
public List<string>             RecycledPaths    { get; set; } = new();
public List<string>             CreatedShortcuts { get; set; } = new();
public string                   Summary          { get; set; } = string.Empty;
public DateTime                 Timestamp        { get; set; } = DateTime.Now;
public int                      ItemCount        { get; }
```

`ItemCount` switches on `Kind`: `MoveFiles` → `Moves.Count`, `CreateShortcuts` → `CreatedShortcuts.Count`, **default (both recycle kinds)** → `RecycledPaths.Count`. The switch deliberately ignores the other collections; tests populate the wrong ones to prove it.

`ActionHistoryMove` is `{ Source, Destination }`.

### `ProfileSettings` — defaults

Every default is pinned by `ProfileSettingsTests.Constructor_ShouldSetDefaults`:

| Property | Default | Property | Default |
|----------|---------|----------|---------|
| `Name` | `"Default"` | `Volume` | `0.5` |
| `TargetPaths` | empty | `MoveTargetPath` | `""` |
| `IncludeSubdirectories` | `true` | `ExcludeFolderNames` | empty |
| `MinimumFileSize` | `1` | `DisabledTargetPaths` | empty |
| `IsMiniPreview` | `true` | `DisabledExcludeFolderNames` | empty |
| `IsAutoPreview` | `true` | `FilterRules` | empty |
| `IsAutoPlay` | `false` | `FolderSearchPatterns` | empty |
| `SelectedSortOption` | `"Size (largest)"` | `FolderSearchMaxDepth` | `null` |
| `LinkSiblingsLayer` | `1` | `FolderSearchResultPaths` | empty |
| `LinkSiblingsPrefix` | `""` | `SelectedFolderSearchResultPaths` | empty |
| `DuplicateMatchByRegex` | `false` | `DuplicateMatchRegex` | `""` |

`FolderSearchMaxDepth` is nullable: **`null` means unlimited recursion**, `1` means direct children only.

### `AppSettings`

```csharp
public List<ProfileSettings>    Profiles          { get; set; } = new();
public string                   ActiveProfileName { get; set; } = "Default";
public List<ActionHistoryEntry> ActionHistory     { get; set; } = new();
public double?                  WindowLeft        { get; set; }
public double?                  WindowTop         { get; set; }
public double?                  WindowWidth       { get; set; }
public double?                  WindowHeight      { get; set; }
public bool                     IsMaximized       { get; set; }
```

Window geometry and the action history are **global**, not per-profile — switching profiles does not move the window or clear the undo stack.

### `IFileSystemService`

The one port. Eleven members, all synchronous:

```csharp
IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
long                GetFileSize(string filePath);
DateTime            GetLastWriteTime(string filePath);
Stream              OpenRead(string filePath);
bool                DirectoryExists(string path);
string              GetFileName(string filePath);
bool                FileExists(string filePath);
string              ReadAllText(string filePath);
void                WriteAllText(string filePath, string content);
void                CreateDirectory(string path);
IEnumerable<string> EnumerateDirectories(string path);
```

The real implementation is [`FileSystemService`](infrastructure.md); tests substitute a Moq double.

## Rules & constraints

**Dependency**

1. **Core takes no dependencies.** No `ProjectReference`, no runtime `PackageReference` (the StyleCop analyzer inherited from `Directory.Build.props` is build-time only — see [Design](#design)), no reference to Application/Infrastructure/UI types. If a model appears to need a service, the service belongs in Application and the model stays a shape.
2. **No I/O in Core.** The only `System.IO` use is `Path.GetExtension` in `DuplicateGroup.FileExtension` and `ScanAnalytics.FromResult` — pure string manipulation, no disk access.

**Serialization (the settings file is the contract)**

3. **Never reorder or insert enum members mid-list.** `System.Text.Json` writes enums as ordinals by default. Reordering `FolderMatchType` silently reinterprets every saved search pattern. Two tests exist purely to fail if the ordinals move: `ActionHistoryEntryTests.ActionHistoryKind_Ordinals_Preserved` and `FolderSearchPatternTests.FolderMatchType_Ordinals_Preserved`. Append new members at the end.
4. **Computed and display-only members carry `[JsonIgnore]`.** Today: `FilterRule.Priority`, `FilterRule.DisplaySummary`, `FolderSearchPattern.Priority`. A new computed property on a persisted model needs the attribute, or it lands in the settings file and drifts.
5. **Deserialization is tolerant by design.** Unknown JSON properties are ignored by `System.Text.Json`, and absent properties fall back to the C# initializers — which is why `IsEnabled` returns to `true` and `MinimumFileSize` to `1` when a legacy file omits them. Do not add `[JsonRequired]` or a strict-mode option without accounting for every settings file already on disk.

**Change notification**

6. **Raise only on actual change.** Every notifying setter is guarded by an inequality check. Raising unconditionally causes binding storms in the UI, which persists settings on nearly every mutation.
7. **`FolderSearchResult.TotalSize` must keep raising both events**, and `SubfolderItem` paging must keep raising all seven. These are contracts the views depend on and tests assert.

**Thread safety**

8. **None of these types is thread-safe.** No locks, no `Interlocked`, no immutable collections. The scan runs on a background thread but only mutates `ScannedFile.Hash` on files it owns exclusively (`DuplicateScannerService.GroupBySizeAndHash`); everything the UI binds to is mutated on the dispatcher thread. A future parallel scan would need to respect this, or these models would need real synchronization.
9. **`ActionHistoryEntry.Timestamp` defaults to `DateTime.Now`** (local time, evaluated at construction). It is not injected, so a test that needs a fixed clock must set the property explicitly.

**Error handling**

10. Core throws nothing of its own. There is no validation, no argument-guard, no custom exception type. `DuplicateGroup.FirstFilePath` returns `null` rather than throwing on an empty group; `FirstFileName` and `FileExtension` return `""`. Validation belongs to the layer above.

## Testing

Ten test classes under `tests/WindowsFileManager.Tests/Models/` cover this module. Everything is a plain in-memory object graph — **nothing in Core needs a mock**, because nothing in Core has a dependency.

| Test class | What it pins |
|------------|--------------|
| `ScannedFileTests` | `FormatFileSize` at every boundary; `IsFileSelected` raises `PropertyChanged` only on a real change |
| `ScanOptionsTests` | The default option values |
| `ScanResultTests` | Defaults and `FormattedWastedSize` |
| `DuplicateGroupTests` | All three `WastedBytes` branches, `DeleteAllLabel` at `Count == 2` and otherwise, the empty-group null/empty returns |
| `ScanAnalyticsTests` | `UniqueFiles` arithmetic, both percentage zero-guards, `Take(8)`, the always-six size buckets including for an empty result |
| `FilterRuleTests` | `Priority`/`DisplaySummary` absent from written JSON; a legacy blob that *contains* `DisplaySummary` still deserializes; `FilterAction` ordinal `0` → `Include` |
| `FolderSearchPatternTests` | The six `FolderMatchType` ordinals (a `[Theory]` with one case per member); defaults |
| `FolderSearchResultTests` | The exact two-event order when `TotalSize` changes; the PB-capable display formatter |
| `SubfolderItemTests` | Filter/paging: filter resets the page, `TotalPages == 1` when empty, `PageStatus` singular/plural, `NextPage`/`PrevPage` bounds, the seven paging events |
| `SubfolderLocationTests`, `AppSettingsTests`, `ProfileSettingsTests`, `ActionHistoryEntryTests` | Defaults, `ItemCount`'s switch (including that it ignores the non-matching collections), enum ordinals |

**Coverage:** Core is fully inside the boundary — `[WindowsFileManager.Core]*` is in the test project's `Include` list and the 100% line/branch/method threshold applies to it. No type in Core is marked `[ExcludeFromCodeCoverage]`.

**Consequence for new code:** every branch you add here needs a test in the same change or `dotnet test` fails the build with a coverlet threshold error. That includes the `else` arm of a ternary and the fallback arm of a `switch` expression.

## Links

- [`../../ARCHITECTURE.md`](../../ARCHITECTURE.md) — system map and layer boundaries
- [`../adr/`](../adr/) — ADR-001 (four-module Clean Architecture), ADR-004 (all I/O behind `IFileSystemService`), ADR-005 (100% coverage enforced by coverlet.msbuild), ADR-007 (System.Text.Json settings, enum-ordinal stability, `[JsonIgnore]` on computed properties)
- [application.md](application.md) — the services that consume these models
- [infrastructure.md](infrastructure.md) — the real `IFileSystemService` implementation
- [ui.md](ui.md) — how the models are bound and mutated
- [`../specs/SPEC-001-duplicate-detection.md`](../specs/SPEC-001-duplicate-detection.md) — `ScanOptions`/`ScanResult`/`DuplicateGroup` in behavior terms
- [`../specs/SPEC-003-custom-filter-rules.md`](../specs/SPEC-003-custom-filter-rules.md) — `FilterRule` semantics
- [`../specs/SPEC-007-folder-search.md`](../specs/SPEC-007-folder-search.md) — `FolderSearchPattern` / `FolderMatchType` semantics
- [`../specs/SPEC-009-settings-and-window-state-persistence.md`](../specs/SPEC-009-settings-and-window-state-persistence.md) — the persisted schema
- [`../GLOSSARY.md`](../GLOSSARY.md) — domain terms used above
