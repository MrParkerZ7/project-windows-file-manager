# WindowsFileManager (WPF shell)

`src/WindowsFileManager/` — the desktop application and composition root. Assembly name `WindowsFileManager`; window title **"Folder File Control"**.

## Purpose

The UI module is the executable. It composes the concrete `FileSystemService` into the Application services, exposes every feature as a bindable ViewModel surface, and renders it with WPF. It also owns the pieces of behavior that have no home below the UI: folder search, the folder-action operations (flatten, link siblings, clear subfolders), the recycle/undo stack, preview classification, and the resource monitor.

It is the only module that references all three of the others, and the only one that talks to the shell, COM, `MessageBox`, or the Recycle Bin.

## Design

```
WindowsFileManager/
  App.xaml / App.xaml.cs          StartupUri="Views/MainWindow.xaml"; App.xaml.cs is an empty partial class
  ViewModels/                     WindowsFileManager.ViewModels
    ViewModelBase.cs        42    INotifyPropertyChanged + SetProperty
    MainViewModel.cs      4957    the entire application surface — 175 public members, 69 commands
    ExtensionFilter.cs      51    one extension-filter toggle row
    ToggleItem.cs           35    a string with an enable checkbox
  Helpers/                        WindowsFileManager.Helpers
    RelayCommand.cs         41    ICommand over delegates
    Converters.cs          146    5 value/multi-value converters
    FileTypeIconConverter.cs 56   extension -> emoji fallback icon
    MiniPreviewConverter.cs 208   thumbnails via BitmapImage or IShellItemImageFactory
    FormattedTextBehavior.cs 213  the help-popup markup grammar
    TextBoxEnterKeyBehavior.cs 67 attached Enter-key command
    ShortcutHelper.cs       34    .lnk creation via WScript.Shell
  Views/                          WindowsFileManager.Views
    MainWindow.xaml       2796    the whole UI
    MainWindow.xaml.cs     421    code-behind for what bindings cannot do
    ProfileNameDialog.xaml/.cs    modal name-entry dialog with live validation
  Assets/                         app-icon.ico + 3 MSIX PNG logos
  Package.appxmanifest            MSIX identity (see ADR-008)
```

**MVVM is hand-rolled — there is no framework and no DI container.** `ViewModelBase` supplies `INotifyPropertyChanged` and `SetProperty`; `RelayCommand` supplies `ICommand`. `MainWindow.xaml` constructs the ViewModel *in XAML*:

```xml
<Window.DataContext>
    <vm:MainViewModel />
</Window.DataContext>
```

That parameterless constructor is the **composition root**. It chains to an `internal` constructor that takes the services:

```csharp
public MainViewModel()
    : this(CreateDefaultScanner(), CreateDefaultSettings(), new FileSystemService()) { }

internal MainViewModel(DuplicateScannerService scannerService,
                       SettingsService settingsService,
                       IFileSystemService fileSystem)
```

`CreateDefaultScanner()` builds `new DuplicateScannerService(fs, new FileHashService(fs))` over one shared `FileSystemService`; `CreateDefaultSettings()` builds `new SettingsService(new FileSystemService(), %APPDATA%\WindowsFileManager\settings.json)`.

**Code-behind exists only where a binding cannot reach.** `MainWindow.xaml.cs` handles window geometry restore, `MediaElement` transport (WPF exposes no bindable Play/Pause), `TabControl.SelectionChanged` panel state, `GridViewColumnHeader` click-sorting, the three modal profile dialogs, digit-only `PreviewTextInput`, and `SubfolderItem` paging clicks. Everything else is a command binding.

**Coverage split.** Only three files in this module are inside the coverage boundary: `Converters.cs`, `RelayCommand.cs`, `ViewModelBase.cs`. Everything else in `Helpers/` and `ViewModels/` is marked `[ExcludeFromCodeCoverage]`, and the entire `Views` namespace is pattern-excluded. See [Testing](#testing).

## Key types

| Type | File | Responsibility |
|------|------|----------------|
| `App` | `App.xaml(.cs)` | Application entry. `StartupUri` only; no startup code. |
| `MainViewModel` | `ViewModels/MainViewModel.cs` | The whole application surface: 69 commands, ~100 bindable properties, the scan/search/action orchestration, preview classification, resource monitor, profile management, and the undo stack. `[ExcludeFromCodeCoverage]`. |
| `ViewModelBase` | `ViewModels/ViewModelBase.cs` | `INotifyPropertyChanged` + `SetProperty<T>` + `OnPropertyChanged`. **Covered.** |
| `RelayCommand` | `Helpers/RelayCommand.cs` | `ICommand` over `Action<object?>` + optional `Predicate<object?>`. **Covered.** |
| `ExtensionFilter` | `ViewModels/ExtensionFilter.cs` | One row of the file-type filter: `Extension`, `IsChecked`, `FileCount`, `TotalSize`, `FormattedSize`. |
| `ToggleItem` | `ViewModels/ToggleItem.cs` | An immutable `Value` string with a mutable `IsEnabled` flag — used for target paths and excluded folder names. |
| `BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`, `InverseBoolConverter`, `SubtractConverter`, `PercentToWidthConverter` | `Helpers/Converters.cs` | The five binding converters. **Covered.** |
| `FileTypeIconConverter` | `Helpers/FileTypeIconConverter.cs` | Extension → emoji, used when a thumbnail is unavailable. Singleton. |
| `MiniPreviewConverter` | `Helpers/MiniPreviewConverter.cs` | Path → 80 px `ImageSource`, via `BitmapImage` for 15 native formats and the Windows shell thumbnail extractor for everything else. Static unbounded cache. |
| `FormattedTextBehavior` | `Helpers/FormattedTextBehavior.cs` | Attached `FormattedText` property that parses the help-popup markup into `TextBlock` inlines. |
| `TextBoxEnterKeyBehavior` | `Helpers/TextBoxEnterKeyBehavior.cs` | Attached `Command` property that updates the binding then executes on Enter. |
| `ShortcutHelper` | `Helpers/ShortcutHelper.cs` | `internal static` — creates a `.lnk` via late-bound `WScript.Shell`. |
| `MainWindow` | `Views/MainWindow.xaml(.cs)` | The single window. All 2796 XAML lines. |
| `ProfileNameDialog` | `Views/ProfileNameDialog.xaml(.cs)` | Modal name entry with live duplicate validation and an OK button disabled while invalid. |

## Public API

### `ViewModelBase` (covered)

```csharp
public abstract class ViewModelBase : INotifyPropertyChanged

public    event PropertyChangedEventHandler? PropertyChanged;
protected void OnPropertyChanged([CallerMemberName] string? propertyName = null);
protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null);
```

`SetProperty` returns `false` and raises nothing when `EqualityComparer<T>.Default.Equals(field, value)`; otherwise it assigns, raises, and returns `true`.

### `RelayCommand` (covered)

```csharp
public class RelayCommand : ICommand

public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null);   // throws ArgumentNullException on null execute

public event EventHandler? CanExecuteChanged;   // add/remove forwarded to CommandManager.RequerySuggested
public bool CanExecute(object? parameter);      // _canExecute?.Invoke(parameter) ?? true
public void Execute(object? parameter);
public void RaiseCanExecuteChanged();           // CommandManager.InvalidateRequerySuggested()
```

Because `CanExecuteChanged` delegates to `CommandManager.RequerySuggested`, re-evaluation is **driven by WPF**, not by the ViewModel. A `CanExecute` predicate that reads VM state gets re-queried on input events automatically; it is not re-queried because a property changed.

### Converters (covered)

| Converter | Interface | `Convert` | `ConvertBack` |
|-----------|-----------|-----------|---------------|
| `BoolToVisibilityConverter` | `IValueConverter` | `true` → `Visible`, `false` → `Collapsed`, non-bool → `Collapsed` | `Visibility == Visible` |
| `InverseBoolToVisibilityConverter` | `IValueConverter` | `true` → `Collapsed`, `false` → `Visible`, non-bool → `Visible` | `Visibility != Visible` |
| `InverseBoolConverter` | `IValueConverter` | `!value`, non-bool → `true` | `!value`, non-bool → `false` |
| `SubtractConverter` | `IValueConverter`, singleton `Instance` | `Math.Max(0, width - double.Parse(parameter))`; returns the input unchanged if either cast fails | `NotSupportedException` |
| `PercentToWidthConverter` | `MarkupExtension` + `IMultiValueConverter`, static `Instance` | `values[0]`=percent, `values[1]`=containerWidth; returns `containerWidth * percent / 100` clamped to `[0, containerWidth]`; returns `0.0` unless there are ≥2 values, both `double`, and `containerWidth > 0` | `NotSupportedException` |

`PercentToWidthConverter` also overrides `ProvideValue` to return `Instance`, so XAML can write `{helpers:PercentToWidthConverter}` inline.

### `MainViewModel` — commands

All 69 are `public ICommand { get; }`, assigned once in the constructor as `RelayCommand`s. The `CanExecute` column is empty where the command is always executable.

**Scan and target folders (9)**

| Command | Invokes | CanExecute |
|---------|---------|-----------|
| `ScanCommand` | `ScanAsync()` | `!IsScanning && TargetPaths.Any(t => t.IsEnabled)` |
| `CancelCommand` | `Cancel()` | `IsScanning` |
| `AddFolderCommand` | `AddFolder()` — `OpenFolderDialog` | `!IsScanning` |
| `AddFolderByPathCommand` | `AddFolderByPath()` — adds `NewFolderPath.Trim()` verbatim | `!IsScanning && NewFolderPath` non-blank |
| `RemoveFolderCommand` | `RemoveFolder(p)` | `!IsScanning` |
| `SelectAllTargetsCommand` / `ClearAllTargetsCommand` | `SetAllToggles(TargetPaths, true/false)` | `TargetPaths.Count > 0` |
| `SelectAllExcludesCommand` / `ClearAllExcludesCommand` | `SetAllToggles(ExcludeFolderNames, true/false)` | `ExcludeFolderNames.Count > 0` |

**File actions and preview (5)**

| Command | Invokes | CanExecute |
|---------|---------|-----------|
| `OpenFileLocationCommand` | `OpenFileLocation(p as string)` — `explorer.exe /select,"path"` | |
| `DeleteFileCommand` | `DeleteFile(p as ScannedFile)` — confirm, then recycle | `!IsScanning` |
| `DeleteAllInGroupCommand` | `DeleteAllInGroup(p as DuplicateGroup)` | `!IsScanning` |
| `PreviewFileCommand` | `PreviewFile(p as string)` | |
| `ClosePreviewCommand` | `ClosePreview()` — **also sets `IsAutoPreview = false`** | |

**Filter and panel toggles (6)**

| Command | Invokes |
|---------|---------|
| `ShowAllTypesCommand` / `ClearAllTypesCommand` | `SetAllExtensions(true/false)` |
| `ApplyFileSizeFilterCommand` | `ApplyFilters()` — refreshes the `ICollectionView` and recounts `FilteredGroupCount` |
| `ToggleFilterCommand` | `IsFilterVisible = !IsFilterVisible` |
| `ToggleActionsCommand` | `IsActionsVisible = !IsActionsVisible` |
| `ToggleDuplicateMatchModeCommand` | `DuplicateMatchByRegex = !DuplicateMatchByRegex` |

**Selection (4)**

| Command | Invokes | CanExecute |
|---------|---------|-----------|
| `SelectAllFilesCommand` | `SelectAllFiles()` | `DuplicateGroups.Count > 0` |
| `SelectNewerFilesCommand` | `SelectNewerFiles()` — keeps the oldest per group unselected | `DuplicateGroups.Count > 0` |
| `SelectOlderFilesCommand` | `SelectOlderFiles()` — keeps the newest per group unselected | `DuplicateGroups.Count > 0` |
| `ClearFileSelectionCommand` | `ClearFileSelection()` | |

All three selectors then run `ApplyIgnoreRules()`, which deselects anything matched by a `FilterAction.Exclude` rule and reports the count in `StatusMessage`.

**Custom filter rules (8)**

| Command | Invokes | CanExecute |
|---------|---------|-----------|
| `AddFilterRuleCommand` | `AddFilterRule()` | `RulePatternText` non-blank |
| `RemoveFilterRuleCommand` | `RemoveFilterRule(p as FilterRule)` | |
| `ClearAllRulesCommand` | `ClearAllRules()` | `FilterRules.Count > 0` |
| `EnableAllRulesCommand` / `DisableAllRulesCommand` | `SetAllRulesEnabled(true/false)` | `FilterRules.Count > 0` |
| `ApplyFilterRulesCommand` | `ApplyFilterRules()` — first enabled matching rule wins per file | `DuplicateGroups.Count > 0` |
| `MoveFilterRuleUpCommand` / `MoveFilterRuleDownCommand` | `ObservableCollection.Move` + `RefreshRulePriorities()` | |

**Excluded folder names (2)**

| Command | Invokes | CanExecute |
|---------|---------|-----------|
| `AddExcludeFolderCommand` | `AddExcludeFolder()` | `NewExcludeFolderName` non-blank |
| `RemoveExcludeFolderCommand` | `RemoveExcludeFolder(p)` | |

**Folder search (8)**

| Command | Invokes | CanExecute |
|---------|---------|-----------|
| `AddFolderSearchPatternCommand` | `AddFolderSearchPattern(p as string)` — dedupes on (pattern, matchType) | |
| `RemoveFolderSearchPatternCommand` | `RemoveFolderSearchPattern(p)` | |
| `MoveSearchPatternUpCommand` / `MoveSearchPatternDownCommand` | reorder + re-prioritize | |
| `SearchFoldersCommand` | `SearchFoldersAsync()` | `!IsFolderSearching && TargetPaths.Any(t => t.IsEnabled)` |
| `StopFolderSearchCommand` | `StopFolderSearch()` | `IsFolderSearching` |
| `ClearFolderSearchCommand` | `ClearFolderSearch()` | `FolderSearchResults.Count > 0 \|\| DiscoveredSubfolders.Count > 0 \|\| DiscoveredFileTypes.Count > 0` |
| `OpenFolderLocationCommand` | `OpenFolderLocation(p as string)` — `explorer.exe "path"` | |

**Undo history (3)**

| Command | Invokes | CanExecute |
|---------|---------|-----------|
| `UndoLastActionCommand` | `UndoLastAction()` | `ActionHistory.Count > 0` |
| `UndoSpecificActionCommand` | `UndoSpecificAction(p as ActionHistoryEntry)` | |
| `ClearHistoryCommand` | `ClearHistory()` | `ActionHistory.Count > 0` |

**Folder actions (14)**

| Command | Invokes | CanExecute |
|---------|---------|-----------|
| `SelectAllFoldersCommand` | `SelectAllFolders()` | `FolderSearchResults.Count > 0` |
| `ClearFolderSelectionCommand` | `ClearFolderSelection()` | `SelectedFolderCount > 0` |
| `ScanSubfoldersCommand` | `ScanSubfolders()` | `SelectedFolderCount > 0 && !IsScanningFolders` |
| `FlattenSelectedFoldersCommand` | `FlattenSelectedFolders()` | `SelectedFolderCount > 0` |
| `LinkSiblingFoldersCommand` | `LinkSiblingFolders()` | `SelectedFolderCount > 0 && LinkSiblingsLayer >= 1` |
| `ScanFlattenFileTypesCommand` | `ScanFlattenFileTypes()` | `SelectedFolderCount > 0 && !IsScanningFlattenTypes` |
| `SelectAllFlattenFileTypesCommand` / `ClearFlattenFileTypeSelectionCommand` | bulk-toggle the flatten type list | |
| `ClearSelectedSubfoldersCommand` | `ClearSelectedSubfolders()` — recycles matching subfolders across every selected root | `DiscoveredSubfolders.Any(s => s.IsSelected)` |
| `SelectAllSubfoldersCommand` / `ClearSubfolderSelectionCommand` | bulk-toggle the subfolder list | |
| `ClearSelectedFileTypesCommand` | `ClearSelectedFileTypes()` — recycles files of the checked extensions | `DiscoveredFileTypes.Any(t => t.IsSelected)` |
| `SelectAllFileTypesCommand` / `ClearFileTypeSelectionCommand` | bulk-toggle the file-type list | |

`SelectAll*` operate on the **filtered** view; `Clear*` operate on the **full** collection.

**Bulk file operations (5)**

| Command | Invokes | CanExecute |
|---------|---------|-----------|
| `DeleteSelectedFilesCommand` | `DeleteSelectedFiles()` | `SelectedFileCount > 0` |
| `MoveSelectedFilesCommand` | `MoveSelectedFiles()` | `SelectedFileCount > 0` |
| `BrowseMoveTargetCommand` | `BrowseMoveTarget()` — `OpenFolderDialog` | |
| `SelectAllInGroupCommand` / `ClearSelectionInGroupCommand` | per-`DuplicateGroup` selection | |

**Profiles (5)**

| Command | Invokes | CanExecute |
|---------|---------|-----------|
| `CreateProfileCommand` | `CreateBlankProfile(p as string)` | |
| `CloneProfileCommand` | `CloneActiveProfile(p as string)` — deep-copies rules and patterns | |
| `SwitchProfileCommand` | `SwitchProfile(p as string)` | `p is string name && name != ActiveProfileName` (`OrdinalIgnoreCase`) |
| `RenameProfileCommand` | `RenameActiveProfile(p as string)` | |
| `DeleteProfileCommand` | `DeleteActiveProfile()` | `ProfileNames.Count > 1` |

### `MainViewModel` — public methods

```csharp
public AppSettings GetSettings();                      // => _settingsService.Load()
public void SaveWindowState(double left, double top, double width, double height, bool isMaximized);
public void SaveSettings();
public void RefreshSelectedFileCount();
```

- `GetSettings()` reloads from disk; `MainWindow_Loaded` uses it to restore window geometry.
- `SaveWindowState` only stores into private fields — it does **not** write. The write happens on the next `SaveSettings()`.
- `SaveSettings()` returns immediately when `_isSwitchingProfile` is set, otherwise snapshots live state into the active profile, copies `ActiveProfileName`, `ActionHistory`, and the window fields into `_settings`, and calls `_settingsService.Save`. It is called from ~27 sites.
- `RefreshSelectedFileCount()` recomputes `SelectedFileCount` with a full `DuplicateGroups.SelectMany(g => g.Files).Count(f => f.IsFileSelected)`.

### `MainViewModel` — bindable state (grouped)

| Group | Properties |
|-------|-----------|
| Targets and scan | `TargetPaths` (`ObservableCollection<ToggleItem>`), `NewFolderPath`, `IncludeSubdirectories`, `ExcludeFolderNames`, `NewExcludeFolderName`, `IsScanning`, `FilesScanned`, `StatusMessage`, `LastResult`, `Analytics` |
| Results | `DuplicateGroups` (`ObservableCollection<DuplicateGroup>`), `FilteredDuplicateGroups` (`ICollectionView`), `SelectedDuplicateGroup`, `FilteredGroupCount`, `TotalGroupCount`, `SelectedFileCount`, `HasSelectedFiles` |
| Filters and sorting | `ExtensionFilters`, `SizeUnits` (`B/KB/MB/GB`), `MinFileSizeText`, `SelectedSizeUnit`, `MinDuplicateCount`, `SortOptions` (10 fixed labels), `SelectedSortOption`, `IsFilterVisible`, `DuplicateMatchByRegex`, `DuplicateMatchRegex` |
| Filter rules | `FilterRules`, `FilterActions`, `FilterTargets`, `RulePatternText`, `RuleIsRegex`, `RuleIgnoreCase`, `RuleAction`, `RuleTarget` |
| Preview | `IsPreviewVisible`, `IsAutoPreview`, `IsMiniPreview`, `IsAutoPlay`, `MediaVolume`, `PreviewType`, `PreviewImage`, `PreviewMediaUri`, `PreviewText`, `PreviewFileName`, `PreviewFileSize` |
| Folder search | `FolderSearchPatterns`, `FolderMatchTypes` (**six** values), `FolderSearchResults`, `NewFolderSearchPattern`, `NewFolderSearchMatchType`, `FolderPatternAddStatus`, `IsFolderSearching`, `FolderSearchStatus`, `FolderSearchCount`, `SelectedFolderCount`, `AreAllFoldersSelected`, `FolderSearchIncludeSubdirectories`, `FolderSearchMaxDepthText` |
| Folder actions | `DiscoveredSubfolders`, `FilteredSubfolders`, `SubfolderFilter`, `DiscoveredFileTypes`, `FilteredFileTypes`, `FileTypeFilter`, `DiscoveredFlattenFileTypes`, `FilteredFlattenFileTypes`, `FlattenFileTypeFilter`, `FlattenRemoveEmptyFolders`, `LinkSiblingsLayer`, `LinkSiblingsPrefix`, `ClearSubfolderStatus`, `IsScanningFolders`, `IsScanningFlattenTypes`, `MoveTargetPath`, `IsActionsVisible` |
| Busy bar | `IsBusy`, `BusyStatus`, `BusyCurrent`, `BusyTotal`, `BusyCountText`, `BusyIsIndeterminate`, `BusyEta` |
| History | `ActionHistory`, `UndoTooltip`, `HistoryTotalEntries`, `HistoryMoveOperationCount`, `HistoryMoveItemCount`, `HistoryRecycleFileOperationCount`, `HistoryRecycleFileItemCount`, `HistoryRecycleDirOperationCount`, `HistoryRecycleDirItemCount` |
| Profiles | `ProfileNames`, `ActiveProfileName`, `ProfileOperationStatus` |
| Panels and monitor | `IsAnalyticsVisible`, `IsFolderControlActive`, `IsHistoryActive`, `ResourceMemory`, `ResourceCpu`, `ResourceThreads` |

**How a scan is issued.** `ScanAsync()` builds `ScanOptions` with only four fields set — `TargetPaths` (enabled only), `IncludeSubdirectories`, `ExcludeFolderNames` (enabled only), and `MatchRegex` (only when `DuplicateMatchByRegex` is on *and* the pattern is non-blank) — then runs `_scannerService.Scan(...)` inside `Task.Run` with `progress: count => FilesScanned = count`. It catches `OperationCanceledException`, `DirectoryNotFoundException`, and `ArgumentException` into `StatusMessage`, and clears `MiniPreviewConverter.ClearCache()` at the start of every scan.

> **Known wiring gap.** `ProfileSettings.MinimumFileSize` is persisted and copied between profiles but is **never written into `ScanOptions`**, and `ScanOptions.FileExtensions` is never set from the UI at all. Both filters are implemented and fully tested in `DuplicateScannerService`, but are unreachable from the running app. The UI's own minimum-size and extension filters (`MinFileSizeText`, `ExtensionFilters`) act **after** the scan, on the `ICollectionView`.

### Helper contracts

**`MiniPreviewConverter`** — `IValueConverter`, `[ExcludeFromCodeCoverage]`.

```csharp
public static void ClearCache();
public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);
public object? ConvertBack(...);   // NotSupportedException
```

Cache is a `static ConcurrentDictionary<string, ImageSource?>`; a `null` entry means "tried and failed". The 15 extensions in `DirectLoadExtensions` load as a frozen `BitmapImage` with `DecodePixelWidth = 80`. Everything else goes through COM: `SHCreateItemFromParsingName` → `IShellItem` → cast to `IShellItemImageFactory` → `GetImage(80×80)` → `Imaging.CreateBitmapSourceFromHBitmap`, with `DeleteObject` / `ReleaseComObject` in `finally`. Both P/Invokes carry `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]`.

**`FileTypeIconConverter`** — singleton `Instance`; maps ~70 extensions to an emoji, defaulting to `📄`. `ConvertBack` throws. Used as the XAML fallback when `MiniPreviewConverter` yields `null`.

**`FormattedTextBehavior`** — attached property `FormattedText` (get/set via `GetFormattedText`/`SetFormattedText`) applied to a `TextBlock`. The grammar, used by every help popup's `Tag`:

| Markup | Renders as |
|--------|-----------|
| `<b>…</b>` | Bold |
| `<h>…</h>` | SemiBold, foreground `#0D47A1` |
| `<w>…</w>` | SemiBold, foreground `#C62828` on background `#FFEBEE` |
| `<link=URL>text</link>` | `Hyperlink`; click runs `Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })`, failures swallowed |
| `\n` | `LineBreak` |

Unclosed and unknown tags degrade to literal text.

**`TextBoxEnterKeyBehavior`** — attached `Command` property. On `Key.Enter` it calls `UpdateSource()` on the `Text` binding first, then executes the command if `CanExecute(null)`.

**`ShortcutHelper`** — `internal static void CreateFolderShortcut(string shortcutPath, string targetFolderPath)`. Late-bound `WScript.Shell` via `Type.GetTypeFromProgID`; throws `InvalidOperationException` if the ProgID is unavailable; releases both COM objects in `finally`.

**`ProfileNameDialog`**

```csharp
public ProfileNameDialog(string title, string headline, string prompt, string initialValue, IEnumerable<string> reservedNames);
public string EnteredName { get; }   // NameInput.Text.Trim()
```

Modal, `Owner` set by the caller, validates against `reservedNames` live, disables OK while invalid, handles Enter/Escape, and selects all on load.

## Rules & constraints

**Layering**

1. **The UI is the composition root and the only place that may construct `FileSystemService`.** Two call sites exist: `CreateDefaultScanner()` and `CreateDefaultSettings()`. New services take `IFileSystemService` in their constructor and get wired here.
2. **Keep logic out of code-behind.** `MainWindow.xaml.cs` is for things a binding cannot express: window geometry, `MediaElement` transport, `TabControl` panel state, `GridViewColumnHeader` sorting, modal dialogs, `PreviewTextInput` filtering, paging clicks. Business decisions belong on the ViewModel behind a command.
3. **Prefer pushing new logic down.** Folder search, `FolderContainsItem`, `GetDirectorySize`, and the folder-action operations live in `MainViewModel` today and use `System.IO` directly rather than `IFileSystemService`. That is why they are untestable and why the class carries `[ExcludeFromCodeCoverage]`. New algorithmic work should go to Application behind the port instead of growing this file.

**Coverage**

4. **Anything you add to `Helpers/` or `ViewModels/` is inside the coverage boundary unless you mark it.** The coverlet `Include` list covers both namespaces. A new converter or VM primitive needs tests to reach 100% line/branch/method, or the build fails.
5. **`[ExcludeFromCodeCoverage]` is a real escape hatch and must not be used to dodge the gate.** It is in the test project's `ExcludeByAttribute` list, so applying it to genuine logic would let untested code ship green. Reserve it for what it already marks: WPF-hosted types, COM/shell interop, and the `MainViewModel` god-object.

**Threading and dispatcher**

6. **Long operations use `async void` handlers.** `ScanAsync`, `SearchFoldersAsync`, `FlattenSelectedFolders`, `LinkSiblingFolders`, `ClearSelected*`, `DeleteSelectedFiles`, and `MoveSelectedFiles` are all `async void`. Only `ScanAsync` and `SearchFoldersAsync` have broad catches — an unhandled exception in the others crashes the process. New long operations must catch their own exceptions.
7. **Everything bound to the UI is mutated on the dispatcher thread.** Background work runs in `Task.Run`, results are marshalled back via `Progress<int>` (which captures the UI `SynchronizationContext`) or `await` continuations; several `ConfigureAwait(true)` calls are deliberate because `ObservableCollection` is touched afterwards.
8. **`BeginBusy` / `EndBusy` are re-entrant** via `_busyDepth`. Every `BeginBusy` needs a matching `EndBusy`, normally in a `finally`.
9. **`MiniPreviewConverter`'s cache is static and unbounded.** It is cleared only at scan start. It survives profile switches and is shared across every view.

**State and persistence**

10. **`SaveSettings()` writes the whole document on every mutation** and is called from ~27 places. Two guards prevent storms: `_isSwitchingProfile` short-circuits during a profile swap, and `_isBulkFolderSelectionUpdate` suppresses per-item notifications during `BulkSetFolderSelection`. Respect both when adding a bulk operation.
11. **`AreAllFoldersSelected`'s setter has a side effect** — it calls `BulkSetFolderSelection(value)`. Recomputation paths deliberately assign the backing field and call `OnPropertyChanged` directly to avoid re-triggering it.
12. **`ClosePreviewCommand` also turns off `IsAutoPreview`.** That is intentional and persisted; do not "fix" it without updating [SPEC-005](../specs/SPEC-005-file-preview.md).

**Destructive operations**

13. **Every destructive action confirms and records.** `DeleteFile`, `DeleteAllInGroup`, `DeleteSelectedFiles`, `ClearSelectedSubfolders`, `ClearSelectedFileTypes`, `FlattenFolder`, and `MoveSelectedFiles` each show a `MessageBox` and push an `ActionHistoryEntry` for undo. Deletion goes to the **Recycle Bin** via `Microsoft.VisualBasic.FileIO.FileSystem` with `RecycleOption.SendToRecycleBin`. A new destructive operation must do both — nothing structural enforces it.
14. **Two existing exceptions are honest gaps, not precedents to follow.** `RemoveEmptyDirectoriesRecursive` calls `Directory.Delete` — a permanent delete that is not recorded in `ActionHistory` and therefore not undoable. Shortcut cleanup during undo uses `File.Delete`, also permanent (but only on paths the app itself created).
15. **`ActionHistory` is capped at `MaxHistoryEntries = 30`**, newest first, and is persisted globally (not per profile).
16. **Every user-supplied regex needs a `MatchTimeout`.** `MainViewModel.MatchesFilter` uses `Regex.IsMatch(..., TimeSpan.FromSeconds(1))` inside `try { } catch { return false; }` — note that a catastrophic pattern therefore silently makes every file "not match", with no user feedback.

**Known documentation defects in the UI itself**

17. The Action help popup in `MainWindow.xaml` claims Delete is permanent with no Recycle Bin and cannot be undone. The code recycles and *is* undoable. Fix the popup text, not the behavior.
18. `TabControl_SelectionChanged` matches on the tab's **string** header. The History tab's header is a `TextBlock`, not a string, so `IsHistoryActive` is effectively never set to `true`.
19. `ApplyFilterRules` computes `var rulesHighToLow = FilterRules.Reverse().ToList();` and never uses it — dead code.

## Testing

Four test classes under `tests/WindowsFileManager.Tests/Helpers/` cover this module. They are the only tests that touch UI code.

| Test class | Covers |
|------------|--------|
| `ConverterTests` | `BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`, `InverseBoolConverter`, `SubtractConverter` — both directions, non-bool/non-parseable inputs, and the `NotSupportedException` paths |
| `PercentToWidthConverterTests` | `PercentToWidthConverter` — normal case, both clamps, too-few values, wrong types, zero container width, `ProvideValue`, `ConvertBack` |
| `RelayCommandTests` | `Execute`, `CanExecute` with and without a predicate, the `ArgumentNullException` on a null `execute`, `RaiseCanExecuteChanged`, and `CanExecuteChanged` add/remove |
| `ViewModelBaseTests` | `SetProperty` changed vs unchanged, the returned bool, and `[CallerMemberName]` propagation |

**Nothing is mocked** in these tests — the covered types have no dependencies. Note that the test project sets `<UseWPF>true</UseWPF>` because these types derive from WPF interfaces (`IValueConverter`, `ICommand`, `MarkupExtension`); the tests instantiate the types directly and never start an `Application` or open a window.

**What is not tested, and why**

| Excluded | Mechanism | Reason |
|----------|-----------|--------|
| `MainViewModel` | `[ExcludeFromCodeCoverage]` | 4957 lines of WPF-, dialog-, shell-, and disk-coupled orchestration. It calls `MessageBox`, `OpenFolderDialog`, `Process.Start`, `DispatcherTimer`, and `System.IO` directly. |
| `ExtensionFilter`, `ToggleItem` | `[ExcludeFromCodeCoverage]` | Thin `ViewModelBase` property bags with no logic of their own |
| `MiniPreviewConverter`, `ShortcutHelper` | `[ExcludeFromCodeCoverage]` | COM / shell interop |
| `FormattedTextBehavior`, `TextBoxEnterKeyBehavior`, `FileTypeIconConverter` | `[ExcludeFromCodeCoverage]` | Require a live WPF visual tree or are pure presentation lookup |
| `MainWindow`, `ProfileNameDialog` | `[ExcludeFromCodeCoverage]` **and** the `[WindowsFileManager]*Views*` pattern | Window code-behind |
| `App.xaml.cs`, `AssemblyInfo.cs`, `*.g.cs`, `*.g.i.cs` | `ExcludeByFile` | Entry point and XAML-generated code |

One stale entry: the exclude pattern `[WindowsFileManager]*Helpers.Win32Api*` refers to a type that does not exist anywhere in the tree.

**Consequence:** the measured 100% for this module is 100% of `Converters.cs` + `RelayCommand` + `ViewModelBase`. Every feature behavior in `MainViewModel` is verified by running the application, not by the suite. Behavior changes there must be validated manually and reflected in the relevant spec in the same commit.

## Links

- [`../../ARCHITECTURE.md`](../../ARCHITECTURE.md) — system map and layer boundaries
- [`../adr/`](../adr/) — ADR-001 (four-module Clean Architecture), ADR-002 (hand-rolled MVVM instead of a framework), ADR-005 (100% coverage enforced by coverlet.msbuild), ADR-006 (persist settings on every mutation), ADR-008 (MSIX packaging on AnyCPU), ADR-009 (TreatWarningsAsErrors with StyleCop), ADR-010 (WPF on .NET 8, and why `dotnet watch` is unusable here)
- [core.md](core.md) — the models bound to the views
- [application.md](application.md) — the services this module composes and calls
- [infrastructure.md](infrastructure.md) — the `IFileSystemService` implementation wired in here
- [`../specs/SPEC-001-duplicate-detection.md`](../specs/SPEC-001-duplicate-detection.md) · [`SPEC-002`](../specs/SPEC-002-filtering-and-sorting.md) · [`SPEC-003`](../specs/SPEC-003-custom-filter-rules.md) · [`SPEC-004`](../specs/SPEC-004-selection-and-file-actions.md) · [`SPEC-005`](../specs/SPEC-005-file-preview.md) · [`SPEC-006`](../specs/SPEC-006-analytics-and-resource-monitor.md) · [`SPEC-007`](../specs/SPEC-007-folder-search.md) · [`SPEC-008`](../specs/SPEC-008-clear-subfolders.md) · [`SPEC-009`](../specs/SPEC-009-settings-and-window-state-persistence.md) · [`SPEC-010`](../specs/SPEC-010-contextual-help.md)
- [`../SECURITY.md`](../SECURITY.md) — destructive-operation guards, shell/COM trust boundaries, `Process.Start` argument handling
- [`../DEV.md`](../DEV.md) — running and debugging the app
