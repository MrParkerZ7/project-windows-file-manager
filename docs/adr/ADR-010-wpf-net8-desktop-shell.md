# ADR-010: WPF on .NET 8 for the desktop shell (and why `dotnet watch` is not usable)

## Status

Accepted — 2026-04-04 (commit `57de160` "Initial commit: WPF duplicate file finder with multi-folder support")

## Context

The product is a Windows-only desktop file manager. Its UI requirements are unusually specific for a utility:

- Dense, grouped result lists with per-group templates, filtering, and multi-key sorting
- An inline preview panel that switches presentation by file kind — image, video, audio, text, info card
- Video and audio playback with transport controls
- Per-row shell thumbnails for arbitrary file types
- Direct Recycle Bin and shell COM access (`Shell.Application`, `WScript.Shell`, `IShellItemImageFactory`)
- A single packaged `.exe` suitable for Microsoft Store distribution

Candidate stacks were WPF, WinForms, WinUI 3, and MAUI. WinForms lacks the templating and binding model the
result lists need. WinUI 3 and MAUI both add packaging and interop friction for full-trust Win32/COM work.

## Decision

WPF on `net8.0-windows10.0.22621.0`
([`../../src/WindowsFileManager/WindowsFileManager.csproj`](../../src/WindowsFileManager/WindowsFileManager.csproj)):

```xml
<OutputType>WinExe</OutputType>
<TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
<UseWPF>true</UseWPF>
<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
```

What that choice buys, concretely:

| Requirement | What WPF supplies in-box |
|---|---|
| Kind-switching preview | `DataTemplate` + `DataTrigger` on a `PreviewType` string — no per-kind view classes |
| Filter + sort on the result list | `CollectionViewSource.GetDefaultView(...)` with a `Filter` predicate and `SortDescription`s |
| Media playback | `MediaElement` |
| Image decode | `System.Windows.Media.Imaging.BitmapImage` with `DecodePixelWidth` |
| Recycle Bin | `Microsoft.VisualBasic.FileIO.FileSystem` (reachable from the windowed TFM) |
| Shell thumbnails / shortcuts / restore | direct COM interop, no wrapper library |
| MVVM | `INotifyPropertyChanged` + `ICommand` are the native binding contract ([ADR-002](ADR-002-hand-rolled-mvvm.md)) |

The `net8.0-windows10.0.22621.0` moniker (rather than plain `net8.0-windows`) is what makes the Windows SDK
surface available, and `TargetPlatformMinVersion=10.0.17763.0` matches the MSIX manifest's
`MinVersion` ([ADR-008](ADR-008-msix-packaging-anycpu-store.md)).

The whole UI project carries **no `PackageReference`** — only project references. Every capability above is
either in-box or reached by direct P/Invoke / late-bound COM.

## Consequences

### Positive

- No third-party UI dependency anywhere in the application. Nothing to version-track, and nothing for the
  dependency-vulnerability gate to flag from the UI layer.
- The app is packageable as a full-trust Win32 application for the Store without a bridge or wrapper
  ([ADR-008](ADR-008-msix-packaging-anycpu-store.md)).
- MVVM is the platform's native pattern, so the hand-rolled `ViewModelBase` / `RelayCommand` are 40-line
  classes rather than an adaptation layer ([ADR-002](ADR-002-hand-rolled-mvvm.md)).
- Full-trust COM interop is available directly — shell thumbnail extraction, `.lnk` creation, and Recycle Bin
  restore all work with plain interop declarations.

### Negative

**The concrete development-loop cost, recorded on 2026-04-14 in
[`../../CLAUDE.md`](../../CLAUDE.md) § Project Notes:**

> **[2026-04-14]** `dotnet watch run` does NOT work for WPF apps (watch/hot-reload is web-only). Use
> `dotnet run` from `src/WindowsFileManager/` to launch the app during development.

So there is **no file-watch or hot-reload dev mode for this project**. The inner loop for any UI change is:
edit → stop the app → `dotnet run --project src/WindowsFileManager` → wait for the build → re-navigate to the
state under test (re-select folders, re-run a scan, re-open the preview). For a UI-heavy application whose
interesting states require a completed scan, that is the dominant cost of iterating on the UI. The repository's
documented workflow is `dotnet run` in both [`../../CLAUDE.md`](../../CLAUDE.md) (line 263) and
[`../../README.md`](../../README.md) Quick Start; nothing in the repo provides a watch alternative. (Visual
Studio's XAML Hot Reload exists under the debugger, but it is not part of this repo's `dotnet`-CLI workflow
and is not documented anywhere in the tree.)

Other costs:

- **Windows-only, permanently.** All five projects target `net8.0-windows*`, and the app additionally depends
  on WPF, `Microsoft.VisualBasic.FileIO`, and shell COM. There is no cross-platform path from here.
- **No headless UI test path.** WPF Views cannot be exercised in the xUnit process, so `*Views*` is excluded
  from coverage and `MainWindow`, `ProfileNameDialog`, and every attached behaviour are
  `[ExcludeFromCodeCoverage]`. Verification of UI behaviour is manual
  ([ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md)).
- **Code-behind is unavoidable** for what bindings cannot express: window geometry restore against
  `SystemParameters.VirtualScreen*`, `MediaElement` transport (no bindable Play/Pause),
  `TabControl.SelectionChanged` panel state, `GridViewColumnHeader` click sorting, digit-only
  `PreviewTextInput`, modal dialog flows, and subfolder paging. `MainWindow.xaml.cs` is 421 lines of it,
  outside the ViewModel and outside coverage.
- **Shell thumbnail extraction runs third-party code in-process.** `IShellItemImageFactory.GetImage` invokes
  whatever thumbnail handler is registered for the file type, inside the application's process. See
  [`../SECURITY.md`](../SECURITY.md).
- Long-running work must be marshalled by hand (`Task.Run` + `ConfigureAwait(true)`, `Progress<int>` capturing
  the UI `SynchronizationContext`, `Dispatcher.BeginInvoke` for deferred `MediaElement` operations) — WPF
  gives no async-safe command primitive ([ADR-002](ADR-002-hand-rolled-mvvm.md)).

### Neutral

- `RuntimeIdentifiers=win-x64` exists for publish only; the solution itself builds `Any CPU`
  ([ADR-008](ADR-008-msix-packaging-anycpu-store.md)).
- The test project also sets `UseWPF=true` — it references the UI assembly to cover its ViewModels and Helpers,
  even though no View is instantiated.
- The window title is "Folder File Control", which is also the MSIX `DisplayName`; the assembly and repository
  name remain `WindowsFileManager`.
- `App.xaml.cs` is an empty `partial class App`; startup is `StartupUri="Views/MainWindow.xaml"` with no
  bootstrapper.

## Links

- [ADR-002](ADR-002-hand-rolled-mvvm.md) — the MVVM layer built on this platform
- [ADR-008](ADR-008-msix-packaging-anycpu-store.md) — how this shell is packaged and shipped
- [ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md) — why Views sit outside the coverage gate
- [ADR-001](ADR-001-clean-architecture-four-modules.md) — the layers underneath the shell
- [`../DEV.md`](../DEV.md) — the actual run/debug loop for this project
- [`../specs/SPEC-005-file-preview.md`](../specs/SPEC-005-file-preview.md) ·
  [`../specs/SPEC-010-contextual-help.md`](../specs/SPEC-010-contextual-help.md)
- [`../SECURITY.md`](../SECURITY.md) — in-process shell interop and COM trust boundaries
- Source: [`../../src/WindowsFileManager/WindowsFileManager.csproj`](../../src/WindowsFileManager/WindowsFileManager.csproj) ·
  [`../../src/WindowsFileManager/Views/MainWindow.xaml.cs`](../../src/WindowsFileManager/Views/MainWindow.xaml.cs)
