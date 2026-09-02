# CLAUDE.md

> AI agent instructions for this .NET 8 WPF application.

---

## Quick Reference

```
BUILD:    dotnet build -c Release
FORMAT:   dotnet format
TEST:     dotnet test                                       (fast local loop — no coverage)
TEST+COV: the full gate is TWO commands — measure, then enforce:
            1. dotnet test -c Release --collect:"XPlat Code Coverage" --settings tests/WindowsFileManager.Tests/coverlet.runsettings
            2. ./scripts/Check-Coverage.ps1                 (this is what fails below 100%)
COVERAGE: 100% line, branch, method — measured by coverlet.collector, enforced by scripts/Check-Coverage.ps1
MSIX:     two steps — publish, then pack (see below; there is no one-line MSIX publish here)
```

`dotnet` must be on `PATH` first — with a portable SDK install it is not. Prepend it for the session:
`$env:PATH = "D:\_env_storeage\dotnet;$env:PATH"` (SDK 8.0.422).

**MSIX packaging** — this project has **no** `WindowsPackageType` property, no Windows App SDK reference, and no `.wapproj`, so `-p:WindowsPackageType=MSIX` is inert here. The real path (source of truth: `.github/workflows/msix-pipeline.yml`) is:

```
1. dotnet publish src/WindowsFileManager/WindowsFileManager.csproj -c Release -r win-x64 --self-contained true -o publish-output
2. copy publish-output\* to msix-layout\, copy Package.appxmanifest -> msix-layout\AppxManifest.xml, copy Assets\*.png -> msix-layout\Assets\
3. makeappx.exe pack /d msix-layout /p output\WindowsFileManager.msix /o
4. (optional) signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f certificate.pfx ...
```

---

## Project Structure (Modular Monorepo)

```
project-windows-file-manager/
├── WindowsFileManager.sln         # 5 projects (4 src + 1 test)
├── Directory.Build.props          # Shared analyzers (StyleCop, .NET Analyzers), TreatWarningsAsErrors
├── .editorconfig                  # Code style rules
├── stylecop.json                  # StyleCop config
├── .gitignore
├── CHANGELOG.md                   # Keep a Changelog
├── README.md
├── docs/                          # See § Documentation Map below
├── scripts/
│   ├── Check-Coverage.ps1         # Coverage gate — reads the Cobertura report, fails below 100% line/branch/method
│   └── New-DevCertificate.ps1     # Self-signed dev cert for MSIX signing
├── .github/workflows/
│   ├── ci.yml                     # "Quality Gate" — restore → format → build → test+collect → coverage threshold → dep-audit → coverage artifact
│   └── msix-pipeline.yml          # Semgrep SAST → publish+MakeAppx+sign → WACK
├── src/
│   ├── WindowsFileManager.Core/           # Models + Interfaces (zero dependencies)
│   │   ├── Models/
│   │   │   ├── ScannedFile.cs · DuplicateGroup.cs · ScanOptions.cs · ScanResult.cs · ScanAnalytics.cs
│   │   │   ├── FilterRule.cs           # Duplicate-list filter rule (FilterAction, regex, priority)
│   │   │   ├── FolderSearchPattern.cs  # + FolderMatchType enum (6 values)
│   │   │   ├── FolderSearchResult.cs   # One folder-search hit
│   │   │   ├── SubfolderItem.cs        # Discovered subfolder + SubfolderLocation (NOT under ViewModels/)
│   │   │   ├── ProfileSettings.cs      # One named profile's workflow state
│   │   │   ├── ActionHistoryEntry.cs   # + ActionHistoryKind enum + ActionHistoryMove
│   │   │   └── AppSettings.cs          # Persisted prefs: profiles, ActionHistory, window state
│   │   └── Services/IFileSystemService.cs
│   ├── WindowsFileManager.Application/    # Business logic (depends on Core)
│   │   └── Services/                      # DuplicateScannerService · FileHashService · SettingsService
│   ├── WindowsFileManager.Infrastructure/ # Real I/O implementations (depends on Core)
│   │   └── Services/FileSystemService.cs  # [ExcludeFromCodeCoverage]
│   └── WindowsFileManager/               # WPF UI (depends on all)
│       ├── App.xaml / App.xaml.cs
│       ├── AssemblyInfo.cs
│       ├── Package.appxmanifest       # MSIX identity + 4-part Version (see § Release Notes)
│       ├── Assets/                    # app-icon.ico, Square150x150Logo.png, Square44x44Logo.png, StoreLogo.png
│       ├── ViewModels/
│       │   ├── MainViewModel.cs       # [ExcludeFromCodeCoverage] — all window state + commands
│       │   ├── ViewModelBase.cs
│       │   ├── ExtensionFilter.cs
│       │   └── ToggleItem.cs          # Enable/disable wrapper for paths & exclusions
│       ├── Views/
│       │   ├── MainWindow.xaml / .xaml.cs      # 3 tabs: Folder · Duplication · 🕘 History
│       │   └── ProfileNameDialog.xaml / .xaml.cs
│       └── Helpers/
│           ├── RelayCommand.cs · Converters.cs # The measured UI types (with ViewModelBase)
│           ├── FormattedTextBehavior.cs  # Rich text markup parser (<b>,<h>,<w>,<link>)
│           ├── FileTypeIconConverter.cs · MiniPreviewConverter.cs · TextBoxEnterKeyBehavior.cs
│           └── ShortcutHelper.cs         # .lnk folder shortcuts via WScript.Shell COM
└── tests/
    └── WindowsFileManager.Tests/
        ├── WindowsFileManager.Tests.csproj  # coverlet.collector only — no coverage properties live here
        ├── coverlet.runsettings             # Single source of coverage scope (Include/Exclude), read by the collector
        ├── GlobalUsings.cs · xunit.runner.json
        ├── Models/                # Core model tests
        ├── Services/              # Application service tests with Moq
        └── Helpers/               # UI-layer tests (Converters, RelayCommand, ViewModelBase)
```

**Dependency flow:** `UI → Application → Core ← Infrastructure`

---

## Architecture: Clean Architecture + MVVM

- **Core**: Pure models + interfaces, no dependencies — shareable across modules
- **Application**: Business logic services depending only on Core interfaces
- **Infrastructure**: Real file system implementation, excluded from coverage
- **UI (WindowsFileManager)**: WPF Views, ViewModels, Helpers — the composition root

**Composition:** there is **no DI container**. `MainWindow.xaml` instantiates the view model declaratively (`<Window.DataContext><vm:MainViewModel /></Window.DataContext>`), and `MainViewModel`'s public parameterless constructor chains to an `internal` constructor taking `(DuplicateScannerService, SettingsService, IFileSystemService)` — that internal seam is what tests inject mocks through. Adding a DI container would be a design change, not a fill-in-the-blank.

---

## Key Conventions

- **Naming**: PascalCase methods/properties, `_camelCase` private fields, `I` prefix interfaces
- **Nullable**: Enabled project-wide (`<Nullable>enable</Nullable>`)
- **File-scoped namespaces**: Required (`namespace Foo;`)
- **Testing**: xUnit + Moq + FluentAssertions, AAA pattern
- **Coverage exclusions**: Views, Infrastructure, generated code (via the `<Include>`/`<Exclude>` filters in [`tests/WindowsFileManager.Tests/coverlet.runsettings`](tests/WindowsFileManager.Tests/coverlet.runsettings) — the single source of coverage scope; the test `.csproj` carries none). The `<Include>` filter *does* name `WindowsFileManager.ViewModels*` and `WindowsFileManager.Helpers*`, but the WPF/COM-bound types inside them carry `[ExcludeFromCodeCoverage]` (`ExcludeByAttribute` honours it): `MainViewModel`, `ExtensionFilter`, `ToggleItem`, `FileTypeIconConverter`, `FormattedTextBehavior`, `MiniPreviewConverter`, `ShortcutHelper`, `TextBoxEnterKeyBehavior`. **The measured UI types are `ViewModelBase`, `RelayCommand`, and `Converters.cs` only** — which is why 100% is attainable with no `tests/.../ViewModels/` folder. Adding a testable type to ViewModels/ or Helpers/ without that attribute puts it under the 100% gate.
- **Interface abstraction**: All I/O through `IFileSystemService` for mock-friendly testing
- **ToggleItem pattern**: Target paths and exclude folders use `ToggleItem` wrapper (string + IsEnabled) for temporary enable/disable
- **FilterRule INotifyPropertyChanged**: `FilterRule.IsEnabled` notifies UI for bulk enable/disable operations
- **Save on change**: `SaveSettings()` called on every mutation (add/remove/reorder rules, paths, exclusions) — not just on window close
- **`[JsonIgnore]` on computed properties**: Getter-only properties on serialized models (e.g., `DisplaySummary`, `Priority`) must have `[System.Text.Json.Serialization.JsonIgnore]` to prevent serialization/deserialization issues with old settings files
- **Enum rename safety**: `System.Text.Json` serializes enums as integers by default. When renaming enum values (e.g., `Select` → `Contains`), keep the same ordinal position to maintain backward compatibility with existing settings

---

## Reusable Features

### Contextual Help Button (`?` Popup)
- **Style**: `HelpButtonStyle` in `Window.Resources` — 16px `?` circle with click-to-open popup
- **Behavior**: `Helpers/FormattedTextBehavior.cs` — parses `Tag` markup into styled `Inline` elements
- **Markup tags**: `<b>bold</b>`, `<h>heading</h>`, `<w>warning</w>`, `<link=URL>text</link>`
- **Links**: `<link>` tag creates clickable `Hyperlink` that opens URL in default browser (e.g., regex101.com)
- **Spec**: [docs/specs/SPEC-010-contextual-help.md](docs/specs/SPEC-010-contextual-help.md)

### Inline WrapPanel Layout Pattern
- **Pattern**: All filter/action/exclude sections use `WrapPanel` with grouped `StackPanel` sub-panels
- **Spacing**: Each sub-panel has `Margin="0,2,0,2"` for vertical gap when wrapping to a second row
- **Structure**: `Title (?) | controls | actions` — separated by `Border Width="1"` dividers
- **Sections using this**: Base Filters, Custom Rules, Exclude Folders, Folder Search toolbar
- **Overflow**: Wraps to multiple rows automatically — no scrollbars, no collapsible panels

### Window State Persistence
- **Saves on close**: window position, size, and maximized state to `settings.json` via `AppSettings` (also saved on every settings change)
- **Restores on load**: `MainWindow.Loaded` reads settings, validates position is on-screen via `SystemParameters.VirtualScreen*`
- **Fallback**: if saved position is off-screen (monitor unplugged/changed), defaults to `CenterScreen`
- **Maximized**: uses `RestoreBounds` to save normal size even when closing maximized
- **Spec**: [docs/specs/SPEC-009-settings-and-window-state-persistence.md](docs/specs/SPEC-009-settings-and-window-state-persistence.md)

### Tab-Aware Sidebar Panels
- **Pattern**: Right sidebar (Column 1) swaps content based on active tab
- **`Folder` tab**: Shows "Folder Action" panel (scan subfolders, clear, selection summary, results)
- **`Duplication` tab**: Shows "Analytics" panel (restores previous visibility state)
- **Implementation**: `TabControl_SelectionChanged` in code-behind saves/restores `IsPreviewVisible` + `IsAnalyticsVisible`, sets `IsFolderControlActive`
- **Event bubbling fix**: Must check `e.Source == tabControl` — nested ListView selections bubble up to TabControl handler

### Folder Search with 6 Match Types
- **Match types**: `Include` (partial name), `Match` (exact name), `Contains` (child item inside folder), `Exclude` (NOT partial), `Mismatch` (NOT exact), `NotContain` (NOT containing the child item) — the full `FolderMatchType` enum, all six exposed via `MainViewModel.FolderMatchTypes`
- **AND logic**: All enabled patterns must pass for a folder to appear in results
- **No patterns = no filter**: Returns all folders when no patterns are active
- **Contains wildcard**: `*.py`, `*.sln` matches file extensions; `.git`, `package.json` matches exact child names
- **Model**: `FolderSearchPattern` with `FolderMatchType` enum, `Priority`, move up/down reordering

### Clear Subfolders (Folder Action Sidebar)
- **Scan**: Discovers all unique subfolder names across selected search results with occurrence counts
- **Include Subdirectories**: Toggle to scan recursively into nested subfolders or only immediate children
- **Expandable items**: Each subfolder name expands to show parent folder locations
- **Filter**: Text search to narrow subfolder list when many names exist
- **Select + Clear**: Check subfolders to delete, confirmation dialog, auto re-scan after clearing

### Flatten Folders
- **Behavior**: `FlattenFolder(rootPath, moves, extensionFilter, removeEmpty)` moves every nested file up to the selected folder's root; files already at the root are skipped
- **Conflict rename**: an existing destination name gets ` (2)`, ` (3)`, … appended before the extension until free
- **Optional file-type filter**: `ScanFlattenFileTypesCommand` builds the extension set; a null filter means all types
- **Optional cleanup**: `FlattenRemoveEmptyFolders` (default `true`) removes the emptied subdirectories bottom-up
- **Undoable**: every move is recorded as an `ActionHistoryMove` under an `ActionHistoryKind.MoveFiles` entry

### Link Sibling Folders
- **Behavior**: `LinkSiblingFoldersCommand` creates a `.lnk` in each selected folder pointing at its siblings
- **Mechanism**: `Helpers/ShortcutHelper.CreateFolderShortcut(shortcutPath, targetFolderPath)` — late-bound `WScript.Shell` COM, sets `TargetPath` + `WorkingDirectory`, always `FinalReleaseComObject` in `finally`
- **Undoable**: recorded as `ActionHistoryKind.CreateShortcuts`; undo deletes the created `.lnk` files

### Profiles (Named Setting Bundles)
- **Model**: `Core/Models/ProfileSettings.cs`; persisted via `AppSettings.Profiles` + `AppSettings.ActiveProfileName`
- **Commands**: `CreateProfile` / `CloneProfile` / `SwitchProfile` / `RenameProfile` / `DeleteProfile`, surfaced in the profile bar docked to the top of `MainWindow.xaml`
- **Naming UI**: `Views/ProfileNameDialog.xaml` (a second `Window`)
- **Switch semantics**: the active profile is captured back into `_settings` before switching, so in-flight edits are never lost

### Action History + Undo
- **Model**: `Core/Models/ActionHistoryEntry.cs` — `ActionHistoryKind` = `MoveFiles` · `RecycleFiles` · `RecycleDirectories` · `CreateShortcuts`, plus `ActionHistoryMove` (source → destination pairs)
- **Storage**: `AppSettings.ActionHistory`, trimmed to `MaxHistoryEntries = 30` (oldest dropped)
- **UI**: the third tab (`🕘 History`) with per-kind analytics counters
- **Commands**: `UndoLastAction` / `UndoSpecificAction` / `ClearHistory` — `ClearHistory` clears the record only, it never touches files
- **Undo per kind**: moves are moved back; recycled items are restored by matching original paths against the Recycle Bin via late-bound `Shell.Application` COM (`RestoreFromRecycleBin`) — an emptied bin simply reports the items as not found; created shortcuts are deleted

---

## Quality Gates (CI + Local)

| Gate | Tool | Command |
|------|------|---------|
| Format | dotnet format | `dotnet format --verify-no-changes` |
| Build | dotnet build + TreatWarningsAsErrors | `dotnet build -c Release` (the flag is redundant — `Directory.Build.props` already sets it) |
| Lint | StyleCop 1.1.118 + Roslyn analyzers | Runs during build |
| Test | xUnit + Moq + FluentAssertions | `dotnet test` (no coverage — see the Coverage row for the gate) |
| Coverage | coverlet.collector 6.0.2 measures, `scripts/Check-Coverage.ps1` enforces 100% line/branch/method | `dotnet test -c Release --collect:"XPlat Code Coverage" --settings tests/WindowsFileManager.Tests/coverlet.runsettings` **then** `./scripts/Check-Coverage.ps1` |
| Security | Semgrep SAST (MSIX pipeline, `semgrep/semgrep` container on ubuntu-latest) | `semgrep scan --config p/default --config p/csharp --error --sarif --output semgrep-results.sarif .` (SARIF uploaded to GitHub Security) |
| Dependency | NuGet vulnerability audit (ci.yml gate 4) | `dotnet list package --vulnerable --include-transitive` |

- **TreatWarningsAsErrors**: `true` in `Directory.Build.props` — all warnings are build errors
- **Coverage threshold**: enforced by [`scripts/Check-Coverage.ps1`](scripts/Check-Coverage.ps1) — reads the Cobertura report, prints a per-module table, exits 1 below 100% line, branch or method (`-Threshold` and `-ReportPath` override the defaults). `dotnet test` on its own no longer fails on low coverage
- **Coverage scope**: [`tests/WindowsFileManager.Tests/coverlet.runsettings`](tests/WindowsFileManager.Tests/coverlet.runsettings) — the single source of Include/Exclude; must be passed via `--settings`
- **Report location**: `tests/**/TestResults/<guid>/coverage.cobertura.xml` (the collector's own path; the CI artifact glob follows it)
- **Fast local loop**: plain `dotnet test` with no `--collect`. `-p:CollectCoverage=false` is now meaningless — that property belonged to `coverlet.msbuild`, which is gone
- **CI**: both `ci.yml` and `msix-pipeline.yml` run the pair — a "Test with coverage" / "Run tests" step, then a "Coverage threshold" step running the script under `shell: pwsh`. `--no-build` is safe again now that nothing instruments at build time

---

## Release Notes

- **Format**: Keep a Changelog (`CHANGELOG.md`)
- **Versioning**: Semantic Versioning (MAJOR.MINOR.PATCH)
- **Auto-generate**: From conventional commits, review before commit
- **Publish**: **not automated.** Neither workflow triggers on a tag — `ci.yml` and `msix-pipeline.yml` both run only on push/PR to `main` (plus `workflow_dispatch` on the MSIX one). The `.msix` is downloaded from the `msix-package` artifact of a `main` run and uploaded to Partner Center by hand. A tag-driven GitHub Release would be new work, not an existing capability.
- **Version files**: `src/WindowsFileManager/Package.appxmanifest` (Identity Version, 4-part: `x.y.z.0`)
- **Process**: [docs/DEV.md](docs/DEV.md) § MSIX and certificate workflow — how the package is built, signed, and reproduced locally; [CHANGELOG.md](CHANGELOG.md) is the release record itself

---

## Documentation Map

Where each kind of knowledge lives. Read the one that matches the question — do not duplicate content across them.

| Question | Home | Notes |
|----------|------|-------|
| How does this feature behave **today**? | [docs/specs/](docs/specs/) — `SPEC-NNN-<feature>.md` | The current-truth behavior contract per feature. Global `SPEC-NNN` numbering, never renumbered. |
| **Why** was it built this way? | [docs/adr/](docs/adr/) — `ADR-NNN-*.md` | One record per load-bearing decision; frozen once accepted. |
| How does this **code** work? | [docs/modules/](docs/modules/) | Per-module mechanics (Core / Application / Infrastructure / UI). A feature spans modules; a module doc covers one. |
| What is the overall **structure**? | [ARCHITECTURE.md](ARCHITECTURE.md) | Module map, dependency rules, build outputs, ADR summary. |
| What is this project **for**? | [docs/CONTEXT.md](docs/CONTEXT.md) | Problem, domain primer, key user flows, constraints and non-goals. |
| What must I **not** get wrong? | [docs/SECURITY.md](docs/SECURITY.md) | Guardrails for destructive file operations, path handling, settings file. |
| How do I **build / run / test**? | [docs/DEV.md](docs/DEV.md) | The local loop, including the `PATH` prelude for a portable SDK. |
| What does this **term** mean? | [docs/GLOSSARY.md](docs/GLOSSARY.md) | Domain vocabulary used across the code. |
| What **shipped when**? | [CHANGELOG.md](CHANGELOG.md) | Keep a Changelog format. |
| Where do I **start** as a user? | [README.md](README.md) | Feature list, prerequisites, project structure. |

**Sync rule:** a change to a feature's behavior updates that feature's spec in `docs/specs/` **in the same commit**. A spec that lags the code is a defect, not a backlog item. New load-bearing decisions get an ADR; new code mechanics go in the module doc; none of it belongs in this file except as a pointer.

---

## Project Notes

- **[2026-09-02]** Coverage **measurement** moved from `coverlet.msbuild` to the `coverlet.collector` data collector, and **enforcement** moved out of the build into [`scripts/Check-Coverage.ps1`](scripts/Check-Coverage.ps1) — see [ADR-011](docs/adr/ADR-011-coverage-via-collector-and-script.md), which supersedes [ADR-005](docs/adr/ADR-005-coverage-enforcement-coverlet-msbuild.md). **Why:** `coverlet.msbuild` instruments during the build and raced — whole modules intermittently came out uninstrumented and reported **0%** while all 217 tests passed, failing the gate in **~20% of runs from a clean tree** (~40% under load). Reproduced across 6.0.2, 6.0.4 and 10.0.1 — **no version fixed it**; the collector, which instruments at runtime, passed **20/20**. **Consequences for this file:** the enforcement statement in the 2026-08-30 note below is superseded (the mechanism changed, not the bar); the test `.csproj` no longer carries any coverage properties; `coverlet.runsettings` is now actually read and is the single source of scope (its `Include` gained `ViewModels`, matching what was really enforced); the Cobertura report moved to `tests/**/TestResults/<guid>/`; and the full local gate is now **two** commands — `dotnet test … --collect:"XPlat Code Coverage" --settings …` then `./scripts/Check-Coverage.ps1`. `-p:CollectCoverage=false` is meaningless from here on. **Unchanged:** the 100% bar, the `Include`/`Exclude` filters, `[ExcludeFromCodeCoverage]`, 217 tests, and 100% actual line/branch/method coverage.
- **[2026-08-30]** Coverage is now **100% line / 100% branch / 100% method across Core, Application, and the UI assembly's ViewModels + Helpers** (217 tests, 0 failed, 0 skipped) — the `~44%` figure in the 2026-04-16 note below is superseded. Attainable because the WPF/COM-bound UI types carry `[ExcludeFromCodeCoverage]` (see § Key Conventions → Coverage exclusions); the threshold is enforced by `coverlet.msbuild` in the test `.csproj`.
- **[2026-08-30]** Documentation restructured to the AI-native 3-layer standard: `ARCHITECTURE.md` at root plus `docs/{README,CONTEXT,SECURITY,DEV,GLOSSARY}.md`, `docs/adr/`, `docs/specs/`, `docs/modules/` — see § Documentation Map. The three `D:\Programing\claude-prompt-solution-architect\...` spec pointers were dead (that repo is a frozen legacy predecessor and is not on disk) and now point at in-repo specs.
- **[2026-04-16]** Folder Control moved to first tab. Action section moved from inline to right sidebar panel (same position as Analytics). Tab switching saves/restores panel states.
- **[2026-04-16]** `SelectionChanged` event bubbles from nested ListView to TabControl — must check `e.Source == tabControl` to avoid hiding panels on file click.
- **[2026-04-16]** Coverage enforcement moved from `coverlet.runsettings` (XPlat Code Coverage) to `coverlet.msbuild` in test `.csproj` for threshold enforcement. Current coverage ~44% — needs `automate-test` to reach 100%. *(Superseded — see the 2026-08-30 note above: coverage is now 100% line/branch/method.)*
- **[2026-04-16]** `TreatWarningsAsErrors` enabled — all StyleCop/Roslyn warnings are now build errors. Inline lambdas without braces trigger SA1503.
- **[2026-04-15]** `Stop-Process -Force` kills the app without triggering `Window.Closing`, so settings were lost. Fixed by saving settings on every mutation, not just on close.
- **[2026-04-15]** Filter UI refactored from collapsible panels to always-visible inline WrapPanel rows. No more expand/collapse toggles — everything visible at a glance, wraps responsively.
- **[2026-04-15]** `FilterAction.Select` renamed to `FilterAction.Contains` for user clarity. Backward compatible with old settings (enum ordinal 0 unchanged).
- **[2026-04-14]** `dotnet watch run` does NOT work for WPF apps (watch/hot-reload is web-only). Use `dotnet run` from `src/WindowsFileManager/` to launch the app during development.
