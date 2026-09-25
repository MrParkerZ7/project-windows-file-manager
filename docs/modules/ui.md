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
    BroadsheetSlots.cs      74    typed template slots: TrailingContent, Placeholder (T-003)
    ShortcutHelper.cs       34    .lnk creation via WScript.Shell
  Themes/                         resource dictionaries, merged in App.xaml (added by T-001)
    Broadsheet.Tokens.xaml  244   the design system 3a is drawn in - 41 Colors + 41 brushes
                                  (incl. T-003's six interaction overlays), 12 frame metrics, type
                                  scale, radii, space, 4 font families (incl. the glyph face)
    Broadsheet.Styles.xaml 1380   the component library (T-003): 49 keyed styles + 2 templates,
                                  one dictionary in four sections; consumed by nothing yet
    Legacy.Palette.xaml     277   the 76 colours actually in use today, verbatim, each with its
                                  use count and the Broadsheet key it is destined for
    Legacy.Metrics.xaml      31   the 7 radii, 4 thicknesses, 16 font sizes and 2 families in use
    Converters.xaml          14   the 5 converter instances, app-scoped by T-002
    Legacy.HelpButton.xaml   60   HelpButtonStyle - the ? circle and its Popup, app-scoped by T-002
    Legacy.Shell.xaml        18   aggregator: the one App.xaml merge entry T-002 is allowed
  Views/                          WindowsFileManager.Views
    MainWindow.xaml          91    composition root - names the nine controls and the layout they sit in
    MainWindow.xaml.cs      128    window geometry, tab state, and one event wire
    ProfileNameDialog.xaml/.cs    modal name-entry dialog with live validation
    Chrome/                       WindowsFileManager.Views.Chrome        (T-002)
      ProfileBar.xaml         49    profile selector + new/clone/rename/delete
      AppStatusBar.xaml       83    busy dot, status, progress, count, ETA, RAM/CPU/threads
      ScanScopeBar.xaml      192    target folders + exclude folder names, 50/50
    Screens/                      WindowsFileManager.Views.Screens       (T-002)
      FoldersScreen.xaml     307    folder-search controls + sortable results
      DuplicatesScreen.xaml 1006    filters, custom rules, group list, action bar
      HistoryScreen.xaml     124    the global operation log
    Panels/                       WindowsFileManager.Views.Panels        (T-002)
      AnalyticsPanel.xaml    162    the analytics dashboard
      FolderActionPanel.xaml 561    subfolder paging, file-type filters, flatten
      PreviewPanel.xaml      219    image/video/audio preview + transport
    Support/                      WindowsFileManager.Views.Support       (T-002)
      NumericInputFilter.cs        digits-only filter shared by the two integer TextBoxes
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

**Code-behind exists only where a binding cannot reach**, and since T-002 it lives beside the markup it serves rather than in one file. `MainWindow.xaml.cs` keeps only what the composition root owns: window geometry restore, `TabControl.SelectionChanged` panel state, and one event wire. The rest moved with its region — `MediaElement` transport (WPF exposes no bindable Play/Pause) to `PreviewPanel`, `GridViewColumnHeader` click-sorting to `FoldersScreen`, the three modal profile dialogs to `ProfileBar`, `SubfolderItem` paging clicks to `FolderActionPanel`, and digit-only `PreviewTextInput` to `Views/Support/NumericInputFilter`, which `FoldersScreen` and `FolderActionPanel` both forward to. Everything else is a command binding.

**One coupling could not be expressed in markup.** The Duplication list's `SelectionChanged` used to stop the preview players by naming `VideoPlayer`/`AudioPlayer` directly. Those are sibling controls now, so `DuplicatesScreen` raises `GroupSelectionChanged` and `MainWindow` — the only place that legitimately knows about both — calls `PreviewPanel.StopMedia()`. The trigger is deliberately a `SelectionChanged` and not a property change.

**The nine controls stay out of the UI Automation tree.** A `UserControl` creates a `UserControlAutomationPeer`, so the split would have inserted nine unnamed `Custom` nodes into the tree that Inspect, Narrator and NVDA walk - nodes the `Grid`/`Border` each control replaced never had. Each control therefore overrides `OnCreateAutomationPeer` to return no peer, which keeps the accessibility tree exactly as it was: measured on the pre-split and post-split builds, the Control view is identical node-for-node on the Folder, Duplication and History tabs. **T-011 owns replacing this** - when it names a region with `AutomationProperties.Name` or `HeadingLevel`, that region's override must be removed in the same change, or the name is discarded silently along with the peer.

**Coverage split.** Only three files in this module are inside the coverage boundary: `Converters.cs`, `RelayCommand.cs`, `ViewModelBase.cs`. Everything else in `Helpers/` and `ViewModels/` is marked `[ExcludeFromCodeCoverage]`, and the entire `Views` namespace is pattern-excluded. See [Testing](#testing).

## The Broadsheet component library

`Themes/Broadsheet.Styles.xaml` (T-003) is the 3a IDE frame read as a parts list: one keyed style per part that recurs across the artboard, built on the token dictionary, so the tasks that rebuild each surface (T-004…T-011) **compose** the frame instead of restyling it. It lands **keyed and unconsumed** — nothing references a style yet, so the running app is pixel-identical to the build before it (measured, see T-003's build log). App.xaml merges it once, after the tokens.

**The catalogue.** Keys follow T-001's reserved shape `Broadsheet.Style.<Part>[.<Variant>]`; two shared control templates are `Broadsheet.Template.*`.

| Section | Keys | Target | Consumed by |
|---|---|---|---|
| Frame | `FocusVisual` · `.Inset` · `.TabUnderline` · `.Splitter.Vertical` · `.Splitter.Horizontal` | `Control` | every interactive style (T-011a's focus-visual contract); T-011 audits |
| | `ToolWindow.Header` · `.HeaderTitle` · `.HeaderActions` | `Border` · `TextBlock` · `StackPanel` | T-005…T-008 |
| | `Rule.Hairline` · `.Row` · `.Vertical` | `Border` | T-005, T-007 |
| | `Band.Title` · `.TabStrip` · `.Toolbar` · `.SubToolbar` · `.ActionBar` (34 · 38 · 36 · 32 · 40 px) | `Border` | T-004, T-007…T-009 |
| | `PaneSplitter.Vertical` · `.Horizontal` | `GridSplitter` | T-005, T-008 |
| | `StatusBar` · `StatusBarItem` | `StatusBar` · `StatusBarItem` | T-009 |
| | `Tab.Editor` · `TabControl.EditorStrip` | `TabItem` · `TabControl` | T-004 |
| Text | `Text.Kicker` · `.Caption` · `.MetaValue` · `.Code` · `.Glyph` · `.GroupTitle` | `TextBlock` | T-006…T-009 |
| | `Tag.Accent` · `Tag.Neutral` | `Border` | T-007, T-008 |
| | `Toggle.Glyph` | `ToggleButton` | T-007 (and 3b's panels) |
| Buttons | `Button.Primary` · `.Secondary` · `.Ghost` · `.Icon` · `.Link` · `.LinkMuted` | `Button` | T-004, T-006…T-009 |
| Inputs | `TextBox` · `TextBox.Numeric` | `TextBox` | T-007, T-008 |
| | `ComboBox` · `ComboBoxItem` | `ComboBox` · `ComboBoxItem` | T-007, T-008 |
| | `CheckBox` · `KeepRadio` | `CheckBox` · `ToggleButton` (so a `RadioButton` too) | T-006…T-008 |
| | `Segment.Group` · `.Option` · `.OptionFirst` | `Border` · `RadioButton` | T-006 |
| | `ListBoxItem.Row` · `ListViewItem.Row` (the results list's group container) · `CopyRow` (the 30 px copy row inside it) | `ListBoxItem` · `ListViewItem` · `Border` | T-006, T-007 |

**Conventions a consumer must know** — none of them is caught by a compiler:

1. **Slots are typed attached properties** (`Helpers/BroadsheetSlots.cs`, decision D1-B): `BroadsheetSlots.TrailingContent` on the tab control carries the right-aligned Scan cluster; `BroadsheetSlots.Placeholder` on a text field carries its hint. A misspelled slot fails the XAML build. ⚠ **Content in `TrailingContent` is keyboard-reachable but not in the UI Automation tree** — WPF's `TabControlAutomationPeer` exposes only the tab items, so a Scan button placed there is invisible to Narrator and NVDA (measured). T-004 either places Scan as a sibling of the `TabControl` (T-011's requirement) or gives the tab control an automation peer that appends the trailing host's children.
2. **`ComboBox.ItemContainerStyle` is not automatic.** Write `ItemContainerStyle="{StaticResource Broadsheet.Style.ComboBoxItem}"` on every ComboBox — no implicit style ships, because one would restyle every ComboBox in the process.
3. **Do not set `Content` on a `Toggle.Glyph`.** The style supplies ▶/▼ from its `IsChecked` trigger; markup `Content` wins and freezes the glyph.
4. **`StackPanel` has no gap.** Inside `ToolWindow.HeaderActions`, give every action but the last `Margin="0,0,12,0"`, and dock the actions before the title so the title ellipsises first.
5. **Data text in a button-family control goes in a `TextBlock`.** Checkbox, radio and button templates recognise access keys (as WPF's own do), so a bare `node_modules` renders as `nodemodules`. Captions are strings; names from data are `<TextBlock Text="{Binding …}"/>`.
6. **Status-bar gaps are the consumer's margins.** `StatusBar`'s items panel is a `DockPanel` with `LastChildFill=False` and every item gets `StatusBarItem` (padding 0, centred); give every item `Margin="0,0,18,0"` except the outermost on each side, which takes none.
7. **The results list: `ListViewItem.Row` on the group container, `CopyRow` on each copy.** One `ListViewItem` holds a whole group (T-007), so the container sizes to its content; the 30 px copy row with its 6 % rule is a `Border` in the copy-row template.
8. **Reference kind follows the declared type.** Brushes and colours are `DynamicResource`; radii, thicknesses, sizes, font families, styles and templates are `StaticResource` — so a typo in a structural value throws on first use instead of silently defaulting.

**Why it is one dictionary.** The library was first built as four family files merged by one aggregator. Every XML check passed and the build was green — and at runtime each style's `FocusVisualStyle` resolved to nothing and the drop-down template threw *Cannot find resource*, because **WPF does not resolve a `{StaticResource}` from one merged dictionary into a sibling merged beside it**. References into `Broadsheet.Tokens.xaml` do resolve, because App.xaml merges the tokens directly and first. So the families are sections of one file, ordered so every internal reference points upward; `BroadsheetComponentDictionaryTests` BCL-07 keeps it that way, and `BroadsheetStyleRuntimeTests` applies every style to a live control so the same class of failure fails the suite. **A later task that `BasedOn`s or references a library style from its own dictionary must merge that dictionary in App.xaml after this one** — never beside it inside another aggregator.

**Knowing deviations from the artboard:**

- **No letter-spacing on the uppercase kickers.** WPF's `TextBlock` has no tracking property; the alternatives destroy screen-reader text.
- **The glyph face is pinned** — `Broadsheet.FontFamily.Glyph` = Segoe UI Symbol, Segoe UI. Cambria lacks most of 3a's chrome glyphs (▾ ▼ ▶ ✓ ✕ ⋯ ↶), and Segoe Fluent Icons is Windows 11 only.
- **Compact metrics.** The design system's `.btn` / `.input` ship at 36 px / 14 px; 3a overrides every one to 28 px / 12.5–13 px, and the styles carry 3a's numbers.
- **Typed attached-property slots**, not `Tag` (decision D1-B).
- **Links are underlined at rest** and darken to Accent600 on hover, as the design system draws `<a>`; WPF has no inheritable text decoration, so the underline is a 1 px rule at the bottom of the line box (about the system's 3 px under the baseline).
- **The checked checkbox draws a tick** the artboard omits — without one, checked is indistinguishable from disabled in high contrast.
- **The placeholder uses the design system's 65 % ink** (4.8:1 on Surface), not Neutral600 (about 3.5:1).
- **Text is on T-011a's option-A steps** (the user's Fork 1 answer): secondary text — kickers, captions, the status bar, inactive tab labels, muted links — is `Neutral700`, link and ghost text `Accent700` (hover `Accent800`), because `Neutral600` and `Accent` fail AA at 11–13 px. `Accent` and `Neutral600` stay for non-text marks: the tab underline, fills, the disclosure glyph, focus rings.
- **Control boundaries clear 3:1.** The text field, the pill dropdown and the unchecked checkbox are outlined in `Neutral600`, not the design system's divider hairline (1.4:1), because a field is recognised by its boundary (WCAG 1.4.11). Hairlines that only separate stay hairlines.
- **Focus follows T-011a's contract**: a 2 px Accent ring 1 px outside buttons, fields, checkboxes and the Keep mark; an inset two-tone ring on list rows and segments, where an outer ring would be clipped; the tab underline thickening to 3 px; a 3 px Accent band on a splitter. A selected row carries a 2 px Accent left edge, since its Accent100 wash is only 1.03:1.
- **The Keep dot is ink**, as the artboard draws it, not accent.
- **Tags are two fixed keys** at `Radius.Sm`; per-file-type tinting is turn 4 and out of scope.

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
| `BroadsheetSlots` | `Helpers/BroadsheetSlots.cs` | Typed content slots for the Broadsheet templates (T-003, decision D1-B): attached `TrailingContent` (the tab strip's right-hand content) and `Placeholder` (a text field's hint). Both default to `null`, which the templates render as nothing. `[ExcludeFromCodeCoverage]`, with tests anyway. |
| `ShortcutHelper` | `Helpers/ShortcutHelper.cs` | `internal static` — creates a `.lnk` via late-bound `WScript.Shell`. |
| `MainWindow` | `Views/MainWindow.xaml(.cs)` | The single window, and since T-002 a **composition root only**: 91 XAML lines that name the nine controls below, plus the layout they sit in (the `DockPanel`, the 3-column `Grid`, the `TabControl` and its three `TabItem`s, and the three side-panel `Border`s with their `Width`/`Visibility` bindings). |
| `ProfileBar`, `AppStatusBar`, `ScanScopeBar` | `Views/Chrome/*.xaml(.cs)` | Window chrome: the profile strip, the global status bar, and the target/exclude folder scope row. |
| `FoldersScreen`, `DuplicatesScreen`, `HistoryScreen` | `Views/Screens/*.xaml(.cs)` | The content of the three tabs. Named for their 3a identity, not for today's tab headers — those header *strings* are load-bearing (see rule 18). |
| `AnalyticsPanel`, `FolderActionPanel`, `PreviewPanel` | `Views/Panels/*.xaml(.cs)` | The content of the three side panels. `PreviewPanel` exposes `StopMedia()` for the composition root. |
| `NumericInputFilter` | `Views/Support/NumericInputFilter.cs` | `static` digit-only `PreviewTextInput` filter, shared by the two integer TextBoxes that no longer live in the same file. |
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
| `<h>…</h>` | SemiBold, foreground `Legacy.Brush.Action.PrimaryDark` (`#0D47A1`) |
| `<w>…</w>` | SemiBold, foreground `Legacy.Brush.Danger.Fg` (`#C62828`) on background `Legacy.Brush.Help.WarnBg` (`#FFEBEE`) |
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

4. **Anything you add to `Helpers/` or `ViewModels/` is inside the coverage boundary unless you mark it.** The coverlet `Include` list in `tests/WindowsFileManager.Tests/coverlet.runsettings` covers both namespaces. A new converter or VM primitive needs tests to reach 100% line/branch/method, or `./scripts/Check-Coverage.ps1` fails the gate — `dotnet test` on its own will not (see [ADR-011](../adr/ADR-011-coverage-via-collector-and-script.md)).
5. **`[ExcludeFromCodeCoverage]` is a real escape hatch and must not be used to dodge the gate.** It is in that file's `ExcludeByAttribute` list, so applying it to genuine logic would let untested code ship green. Reserve it for what it already marks: WPF-hosted types, COM/shell interop, and the `MainViewModel` god-object.

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

The test classes below cover this module — those under `tests/WindowsFileManager.Tests/Helpers/`, `Views/` and `Themes/`. They are the only tests that touch UI code.

| Test class | Covers |
|------------|--------|
| `ConverterTests` | `BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`, `InverseBoolConverter`, `SubtractConverter` — both directions, non-bool/non-parseable inputs, and the `NotSupportedException` paths |
| `PercentToWidthConverterTests` | `PercentToWidthConverter` — normal case, both clamps, too-few values, wrong types, zero container width, `ProvideValue`, `ConvertBack` |
| `RelayCommandTests` | `Execute`, `CanExecute` with and without a predicate, the `ArgumentNullException` on a null `execute`, `RaiseCanExecuteChanged`, and `CanExecuteChanged` add/remove |
| `ViewModelBaseTests` | `SetProperty` changed vs unchanged, the returned bool, and `[CallerMemberName]` propagation |
| `BroadsheetSlotsTests` | `BroadsheetSlots` — the `[ExcludeFromCodeCoverage]` guard, both slots default to `null`, and both round-trip and clear on a bare `DependencyObject` (no STA needed) |
| `DesignTokenContractTests` | The token dictionary (T-001): every `Broadsheet.*` / `Legacy.*` reference resolves (the library's style keys count as declared), sanctioned prefixes, reserved categories empty in the token files, brush values equal the vendored design system, both divider forms, T-003's six interaction overlays equal the colour mixes their source declares, every brush pairs with a `Color`, Cambria pinned, the exact `App.xaml` merge list, and the legacy layer's lifetime |
| `BroadsheetComponentDictionaryTests` | The component library (T-003), read as XML: the 49-style catalogue with its target types (BCL-01), every reference resolves (02) with the kind its declared type requires (03), no colour literal (04), no implicit style and no `Generic.xaml` (05), only reserved categories (05b), no duplicate key across everything `App.xaml` merges (06), one dictionary merged once after the tokens (07), required template parts (08), `RecognizesAccessKey` in every button template (09), the glyph face (10), every 1 px style snaps (11), links inherit their band's type (12), the slots the templates bind exist on the helper (13), no font property on the tab styles themselves (14), no text in a failing ink - T-011a option A (15) |
| `BroadsheetStyleRuntimeTests` | Every library style resolved through the real `Application`, applied to a live control of its target type, its template loaded, and every setter landing with the value it declares; plus the placeholder slot's show/hide wiring, the Keep mark on both a `ToggleButton` and a `RadioButton`, and the status bar keeping a right-docked last item at the right edge — the check the XML tests cannot make, and the one that caught the cross-dictionary failure described under [The Broadsheet component library](#the-broadsheet-component-library) |
| `XamlLoadTests` | That all nine decomposed `UserControl`s parse against the shipped `App.xaml` dictionaries and **create no automation peer** (so the split adds no unnamed node to the accessibility tree), and that the shell composes exactly those nine (so a tenth cannot be added untested). Does not add coverage — the `Views` namespace is pattern-excluded — it is a regression net, not a coverage contributor. |

**Nothing is mocked** in these tests — the covered types have no dependencies. The test project sets `<UseWPF>true</UseWPF>` because these types derive from WPF interfaces (`IValueConverter`, `ICommand`, `MarkupExtension`); they are instantiated directly.

**Starting an `Application` — the one permitted exception.** This document previously stated flatly that the tests *never start an `Application` or open a window*. Since T-002 that is true of every test except two classes that share one application, and the exception is deliberate rather than an erosion.

Decomposing `MainWindow.xaml` into nine `UserControl`s created a failure mode that did not exist while everything lived in one file: a `{StaticResource}` that used to resolve against the Window's own `Resources` block must now resolve against `Application.Resources`. When it doesn't, the `XamlParseException` is raised **while that control is parsed** — so for a panel that ships `Collapsed`, it surfaces the first time a user opens the panel and never in a build, a lint, or any other test. Seven of the nine controls carry such references (21 × `HelpButtonStyle` plus the five converters), so this is the module's largest untested surface, not a corner case.

`XamlLoadTests` therefore constructs all nine controls against the real `App` — `new App(); app.InitializeComponent();`, so the test resolves against exactly the dictionaries `App.xaml` merges, in `App.xaml`'s order, and cannot drift from what ships. Since T-003, `BroadsheetStyleRuntimeTests` uses the same application for the component library, whose failures are of exactly this kind. WPF allows one `Application` per process, so both run on **`WpfAppHost`** (`tests/.../Views/WpfAppHost.cs`), which starts it once. The bounds that keep this from becoming "the tests open windows":

- **No window is shown and no message loop runs.** Controls are constructed and dropped; `Application.Run()` is never called. The host runs queued work items synchronously on its thread.
- **One STA thread**, owned by `WpfAppHost`, so every control is built on the same thread that owns the `Application` whose resources it resolves against. It is a background thread and never keeps the test process alive.
- **Scoped to resolution.** `{DynamicResource}` does not throw; `BroadsheetStyleRuntimeTests` checks each dynamic key it applies exists, and `DesignTokenContractTests` checks the rest textually.

Anything beyond that — showing a window, pumping a dispatcher, driving input — still does not belong in this suite.

**What is not tested, and why**

| Excluded | Mechanism | Reason |
|----------|-----------|--------|
| `MainViewModel` | `[ExcludeFromCodeCoverage]` | 4957 lines of WPF-, dialog-, shell-, and disk-coupled orchestration. It calls `MessageBox`, `OpenFolderDialog`, `Process.Start`, `DispatcherTimer`, and `System.IO` directly. |
| `ExtensionFilter`, `ToggleItem` | `[ExcludeFromCodeCoverage]` | Thin `ViewModelBase` property bags with no logic of their own |
| `MiniPreviewConverter`, `ShortcutHelper` | `[ExcludeFromCodeCoverage]` | COM / shell interop |
| `FormattedTextBehavior`, `TextBoxEnterKeyBehavior`, `FileTypeIconConverter` | `[ExcludeFromCodeCoverage]` | Require a live WPF visual tree or are pure presentation lookup |
| `MainWindow`, `ProfileNameDialog` | `[ExcludeFromCodeCoverage]` **and** the `[WindowsFileManager]*Views*` pattern | Window code-behind |
| `App.xaml.cs`, `AssemblyInfo.cs`, `*.g.cs`, `*.g.i.cs` | `ExcludeByFile` | Entry point and XAML-generated code |

One stale entry: the exclude pattern `[WindowsFileManager]*Helpers.Win32Api*`, in `tests/WindowsFileManager.Tests/coverlet.runsettings`, refers to a type that does not exist anywhere in the tree.

**Consequence:** the measured 100% for this module is 100% of `Converters.cs` + `RelayCommand` + `ViewModelBase`. Every feature behavior in `MainViewModel` is verified by running the application, not by the suite. Behavior changes there must be validated manually and reflected in the relevant spec in the same commit.

## Links

- [`../../ARCHITECTURE.md`](../../ARCHITECTURE.md) — system map and layer boundaries
- [`../adr/`](../adr/) — ADR-001 (four-module Clean Architecture), ADR-002 (hand-rolled MVVM instead of a framework), ADR-011 (100% coverage measured by coverlet.collector and enforced by `scripts/Check-Coverage.ps1`; supersedes ADR-005), ADR-006 (persist settings on every mutation), ADR-008 (MSIX packaging on AnyCPU), ADR-009 (TreatWarningsAsErrors with StyleCop), ADR-010 (WPF on .NET 8, and why `dotnet watch` is unusable here)
- [core.md](core.md) — the models bound to the views
- [application.md](application.md) — the services this module composes and calls
- [infrastructure.md](infrastructure.md) — the `IFileSystemService` implementation wired in here
- [`../specs/SPEC-001-duplicate-detection.md`](../specs/SPEC-001-duplicate-detection.md) · [`SPEC-002`](../specs/SPEC-002-filtering-and-sorting.md) · [`SPEC-003`](../specs/SPEC-003-custom-filter-rules.md) · [`SPEC-004`](../specs/SPEC-004-selection-and-file-actions.md) · [`SPEC-005`](../specs/SPEC-005-file-preview.md) · [`SPEC-006`](../specs/SPEC-006-analytics-and-resource-monitor.md) · [`SPEC-007`](../specs/SPEC-007-folder-search.md) · [`SPEC-008`](../specs/SPEC-008-clear-subfolders.md) · [`SPEC-009`](../specs/SPEC-009-settings-and-window-state-persistence.md) · [`SPEC-010`](../specs/SPEC-010-contextual-help.md)
- [`../SECURITY.md`](../SECURITY.md) — destructive-operation guards, shell/COM trust boundaries, `Process.Start` argument handling
- [`../DEV.md`](../DEV.md) — running and debugging the app
