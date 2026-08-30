# ADR-002: Hand-rolled MVVM (`ViewModelBase` + `RelayCommand`) instead of an MVVM framework

## Status

Accepted — 2026-04-04 (commit `57de160`; both `ViewModelBase.cs` and `RelayCommand.cs` are present in the
initial commit and have not been replaced since)

## Context

WPF data binding requires two things from a ViewModel layer: `INotifyPropertyChanged` for property change
notification, and `ICommand` for button/menu invocation. The ecosystem offers several ways to get them —
CommunityToolkit.Mvvm (source-generated `[ObservableProperty]` / `[RelayCommand]`), Prism, Caliburn.Micro,
ReactiveUI — each bringing a package reference, a version to track, and a set of conventions.

The application is a single-window desktop tool. It has one primary ViewModel, no navigation stack, no
region/shell composition, no messaging bus requirement, and no module loading. The two interfaces it actually
needs are roughly 40 lines each to implement.

## Decision

Write both, own both.

**`ViewModelBase`** ([`../../src/WindowsFileManager/ViewModels/ViewModelBase.cs`](../../src/WindowsFileManager/ViewModels/ViewModelBase.cs)) —
abstract, implements `INotifyPropertyChanged`, exposes:

- `OnPropertyChanged([CallerMemberName] string? propertyName = null)`
- `SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)` which returns `false`
  and raises nothing when `EqualityComparer<T>.Default.Equals(field, value)` — so redundant assignments do not
  produce binding churn.

**`RelayCommand`** ([`../../src/WindowsFileManager/Helpers/RelayCommand.cs`](../../src/WindowsFileManager/Helpers/RelayCommand.cs)) —
`ICommand` over `Action<object?>` plus an optional `Predicate<object?>`. `CanExecuteChanged` forwards
subscription to WPF's global `CommandManager.RequerySuggested`; `RaiseCanExecuteChanged()` calls
`CommandManager.InvalidateRequerySuggested()`.

The UI project (`WindowsFileManager.csproj`) carries **no `PackageReference` at all** — only project
references to Core, Application, and Infrastructure.

Both types are inside the coverage `Include` scope
(`[WindowsFileManager]WindowsFileManager.Helpers*`, `[WindowsFileManager]WindowsFileManager.ViewModels*`) and
are covered by `tests/WindowsFileManager.Tests/Helpers/RelayCommandTests.cs` and
`ViewModelBaseTests.cs`.

## Consequences

### Positive

- No third-party MVVM dependency to version-track, audit, or migrate. The CI dependency-vulnerability gate
  ([`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) lines 40–48) has nothing to report from
  the UI project.
- No source generator in the build, so `TreatWarningsAsErrors`
  ([ADR-009](ADR-009-treat-warnings-as-errors.md)) needed no generated-code carve-outs beyond the standard
  `*.g.cs` coverage exclusions.
- Both classes are small enough to be fully covered by tests and fully read in one sitting; there is no
  framework behaviour to reverse-engineer when a binding misbehaves.
- Bindings use plain `INotifyPropertyChanged` / `ICommand`, so migrating to a framework later is possible
  without touching XAML.

### Negative

These are the real costs versus adopting CommunityToolkit.Mvvm:

- **Every bindable property is hand-written.** No `[ObservableProperty]` generator means a private field, a
  getter, and a `SetProperty` setter for each. `MainViewModel.cs` is roughly 5,000 lines, dominated by this
  boilerplate — and it is marked `[ExcludeFromCodeCoverage]` (`MainViewModel.cs:25`), so the single largest
  file in the application sits outside the 100% gate
  ([ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md)).
- **No async command type.** A framework's `AsyncRelayCommand` would supply an awaitable execute, an
  `IsRunning` flag, and exception capture. Without it, every long-running handler is `async void` —
  `ScanAsync`, `SearchFoldersAsync`, `DeleteSelectedFiles`, `MoveSelectedFiles`, `FlattenSelectedFolders`,
  `ClearSelectedSubfolders`, `LinkSiblingFolders`. An unobserved exception in an `async void` handler crashes
  the process. `ScanAsync` and `SearchFoldersAsync` have broad `catch` blocks; the destructive-action handlers
  rely on per-item `try`/`catch` counters instead.
- **Command re-query is global, not targeted.** Because `CanExecuteChanged` delegates to
  `CommandManager.RequerySuggested`, WPF decides when to re-evaluate `CanExecute` (on input and idle), and
  `RaiseCanExecuteChanged()` invalidates *every* command in the application rather than one. This is
  acceptable at the current command count and `CanExecute` cost (`CanScan()` is a `.Any()` over the target-path
  list); it would not scale to expensive predicates.
- **No messenger, navigation, or DI plumbing.** Anything a binding cannot express falls to code-behind:
  `MainWindow.xaml.cs` is 421 lines covering window-geometry restore, `MediaElement` transport,
  `TabControl.SelectionChanged` panel state, `GridViewColumnHeader` click sorting, digit-only
  `PreviewTextInput`, modal `ProfileNameDialog` flows, and subfolder paging clicks. All of that is outside the
  ViewModel and outside test coverage.
- The `MainWindow.xaml`-declared `DataContext` means the ViewModel is constructed by XAML with its
  parameterless constructor — there is no injection point for a test or an alternative composition
  ([ADR-001](ADR-001-clean-architecture-four-modules.md)).

### Neutral

- `ViewModelBase` and `RelayCommand` themselves are 100%-covered; the cost above is concentrated in
  `MainViewModel`, not in the primitives.
- Other ViewModel-side types follow the same hand-rolled pattern without inheriting `ViewModelBase` —
  several Core models (`ScannedFile`, `FilterRule`, `FolderSearchPattern`, `FolderSearchResult`,
  `SubfolderItem`) implement `INotifyPropertyChanged` directly, because Core does not reference the UI project.
- Nothing in the build prevents a later migration; the decision is reversible at the cost of rewriting
  property declarations.

## Links

- [ADR-001](ADR-001-clean-architecture-four-modules.md) — where the UI layer sits and how it is composed
- [ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md) — why `MainViewModel` is coverage-excluded
- [ADR-009](ADR-009-treat-warnings-as-errors.md) — the analyzer policy this code is written under
- [ADR-010](ADR-010-wpf-net8-desktop-shell.md) — the platform that requires `INotifyPropertyChanged`/`ICommand`
- [`../modules/`](../modules/) — UI module documentation
- Source: [`../../src/WindowsFileManager/ViewModels/ViewModelBase.cs`](../../src/WindowsFileManager/ViewModels/ViewModelBase.cs) ·
  [`../../src/WindowsFileManager/Helpers/RelayCommand.cs`](../../src/WindowsFileManager/Helpers/RelayCommand.cs)
