# Local Development Guide

> How to go from a clean clone to a running app, a green test suite, and a signed MSIX.
> For what the app *does*, see [`../README.md`](../README.md). For how it is *structured*, see
> [`../ARCHITECTURE.md`](../ARCHITECTURE.md). For the rules that govern destructive operations and
> secrets, see [`SECURITY.md`](SECURITY.md).

This guide covers only the build / test / release loop. It does not repeat the feature tour.

---

## Prerequisites

| Requirement | Version / detail | Needed for | Notes |
|---|---|---|---|
| Windows | 10 or 11 | everything | WPF is Windows-only. All projects target `net8.0-windows*`; the UI, Application, and Tests projects target `net8.0-windows10.0.22621.0`. |
| .NET SDK | 8.0 (verified **8.0.422** on the reference machine) | build, test, run, publish | `dotnet format` and `dotnet test` ship in-box with the SDK — nothing extra to install. |
| Git | any recent | clone | — |
| Windows 10 SDK | any version providing `makeappx.exe` + `signtool.exe` (x64) | **MSIX packaging only** | Not needed to build, test, or run the app. Not installed on the reference machine — see [Troubleshooting](#troubleshooting). |
| Windows App Certification Kit | ships with the Windows SDK (`appcert.exe`) | **Store certification checks only** | CI runs this; local runs are optional. |
| Elevated PowerShell | — | `scripts/New-DevCertificate.ps1` only | The script imports into `Cert:\LocalMachine\TrustedPeople`, which requires admin. |

There is **no** Node, Python, Docker, or database dependency. The app makes no network calls;
only CI reaches the network.

### The SDK is not on PATH on this machine

On the reference machine the `dotnet` on `PATH` (`C:\Program Files\dotnet\dotnet.exe`) is a
**runtime-only host with no SDK**. Every SDK command fails with:

```
The command could not be loaded, possibly because:
  * You intended to execute a .NET application:
      The application '--version' does not exist.
  * You intended to execute a .NET SDK command:
      No .NET SDKs were found.
```

The real SDK lives at `D:\_env_storeage\dotnet` (`dotnet --list-sdks` there reports
`8.0.422 [D:\_env_storeage\dotnet\sdk]`). Put it in front of `PATH` for the session before
running anything:

```powershell
# PowerShell
$env:PATH = "D:\_env_storeage\dotnet;$env:PATH"
dotnet --version    # 8.0.422
```

```bash
# Git Bash
export PATH="/d/_env_storeage/dotnet:$PATH"
dotnet --version    # 8.0.422
```

Every command in this guide assumes that has been done (or that you prefix the absolute path,
e.g. `D:\_env_storeage\dotnet\dotnet.exe build -c Release`). Machines with a normal SDK install
need none of this.

---

## First run

```bash
git clone <repo-url>
cd project-windows-file-manager

dotnet restore
dotnet build -c Release
dotnet run --project src/WindowsFileManager
```

- `dotnet build` compiles all five projects (Core, Application, Infrastructure, UI, Tests).
  **Analyzers and StyleCop run during the build and every warning is an error** — see
  [Code style](#code-style).
- `dotnet run --project src/WindowsFileManager` launches the WPF app (`OutputType=WinExe`).
  The window title is **"Folder File Control"**, not "Windows File Manager".
- Solution platforms are `Debug|Any CPU` and `Release|Any CPU` only. There is no x64/x86
  solution platform; `win-x64` appears only as the publish/packaging runtime identifier.

On first use the app writes `%APPDATA%\WindowsFileManager\settings.json`. Settings are saved on
**every** mutation, not on window close — see
[`specs/SPEC-009-settings-and-window-state-persistence.md`](specs/SPEC-009-settings-and-window-state-persistence.md).
Deleting that file resets the app to defaults; it is a safe thing to do while developing.

### `dotnet watch` does not work here

`dotnet watch run` does **not** work for this WPF app — the repo records this in
[`../CLAUDE.md`](../CLAUDE.md) (Project Notes, 2026-04-14) and the decision to stay on plain
WPF-on-.NET-8 is captured in [ADR-010 — WPF on .NET 8 for the desktop shell](adr/README.md).

The consequence for your inner loop: **there is no hot reload**. After editing any C# or XAML
file you must stop the running app, rebuild, and relaunch:

```bash
# stop the app window, then
dotnet build -c Debug
dotnet run --project src/WindowsFileManager
```

Because the loop is manual, prefer driving behavior changes through the test suite (which *is*
fast) and using the app only to confirm the UI wiring.

---

## Test loop

```bash
# Full gate — runs the suite AND enforces 100% coverage. Fails the build below threshold.
dotnet test

# Fast loop — runs the suite only, no coverage instrumentation, no threshold.
dotnet test -p:CollectCoverage=false

# One class / one test while iterating
dotnet test -p:CollectCoverage=false --filter FullyQualifiedName~DuplicateScannerServiceTests
```

Current state (measured 2026-08-30):

| Metric | Value |
|---|---|
| Tests | **217** — 0 failed, 0 skipped |
| Line coverage | **100%** |
| Branch coverage | **100%** |
| Method coverage | **100%** |
| Scope | `WindowsFileManager.Core`, `WindowsFileManager.Application`, and `WindowsFileManager` (ViewModels + Helpers) |
| Excluded | `WindowsFileManager.Infrastructure`, Views, generated code |

Stack: xUnit 2.8.1 + Moq 4.20.70 + FluentAssertions 6.12.0 on Microsoft.NET.Test.Sdk 17.10.0.
`xunit.runner.json` sets `parallelizeAssembly: false` and `parallelizeTestCollections: false`,
so the suite runs fully serially — test order is deterministic and shared-state bugs will not
hide behind parallelism.

### Where the threshold is configured

The **authoritative** coverage gate is an MSBuild property block in
`tests/WindowsFileManager.Tests/WindowsFileManager.Tests.csproj`, applied by `coverlet.msbuild`
6.0.2 on every `dotnet test`:

```xml
<CollectCoverage>true</CollectCoverage>
<CoverletOutput>./coverage/coverage.cobertura.xml</CoverletOutput>
<Threshold>100</Threshold>
<ThresholdType>line,branch,method</ThresholdType>
<ThresholdStat>total</ThresholdStat>
<Include>[WindowsFileManager.Core]*,[WindowsFileManager.Application]*,[WindowsFileManager]WindowsFileManager.Helpers*,[WindowsFileManager]WindowsFileManager.ViewModels*</Include>
<Exclude>[WindowsFileManager]*Views*,[WindowsFileManager]*Helpers.Win32Api*,[WindowsFileManager]XamlGeneratedNamespace*</Exclude>
<ExcludeByFile>**/AssemblyInfo.cs,**/App.xaml.cs,**/*.g.cs,**/*.g.i.cs</ExcludeByFile>
<ExcludeByAttribute>GeneratedCodeAttribute,CompilerGeneratedAttribute,ExcludeFromCodeCoverageAttribute</ExcludeByAttribute>
```

Because `Include` never names `WindowsFileManager.Infrastructure`, that module is excluded by
omission. The report lands at `tests/WindowsFileManager.Tests/coverage/coverage.cobertura.xml`
(gitignored via the `coverage/` rule).

Two things about coverage in this repo are easy to trip over:

- `tests/WindowsFileManager.Tests/coverlet.runsettings` **enforces nothing.** It declares
  `ThresholdType` and `ThresholdStat` but no `Threshold` value, and its `Include` omits
  `ViewModels`. Both CI workflows pass `--collect:"XPlat Code Coverage" --settings …runsettings`,
  but the gate that actually fails a build is the csproj block above.
- The test project references **`coverlet.msbuild` only — `coverlet.collector` is not
  referenced**, even though CI uploads `tests/**/TestResults/**/coverage.cobertura.xml`, the path
  the collector would produce. Do not "fix" a build by editing the runsettings file; edit the
  csproj.
- `[WindowsFileManager]*Helpers.Win32Api*` in `Exclude` is stale — no `Win32Api` type exists in
  the tree.

### When a new file drops coverage

`dotnet test` fails with a coverlet threshold error naming the metric and the assembly. Pick one
of these, in order of preference:

| Option | When it is right | How |
|---|---|---|
| Write the tests | Default. The code is logic and belongs under the gate. | Add a test class under `tests/WindowsFileManager.Tests/{Models,Services,Helpers}/`. Cover every branch — the gate is `line,branch,method`, so an untested `if` fails it. |
| Put the I/O behind `IFileSystemService` | The code only failed to be testable because it touches the real disk. | Add the member to `src/WindowsFileManager.Core/Services/IFileSystemService.cs`, implement it in Infrastructure, mock it with Moq in tests. This is the project's standard seam. |
| Move it to Infrastructure | The type is a thin, untestable wrapper over `System.IO` / COM / P-Invoke with no logic. | `src/WindowsFileManager.Infrastructure/Services/` — outside `Include`, so not gated. Keep it genuinely thin; logic that moves here escapes all testing. |
| `[ExcludeFromCodeCoverage]` | Last resort: WPF code-behind, interop shims, a class the runtime constructs (e.g. `MainViewModel`, which already carries the attribute). | Attribute the type. **This is a gate bypass** — it makes the build pass without adding a single test. Use it for plumbing, never for business logic; a reviewer should question every new one. |

Do not lower `<Threshold>`, and do not weaken an existing test to make a change fit. The 100%
bar is the project's contract — see [ADR-005 in the decision index](adr/README.md).

---

## Code style

```bash
dotnet format                             # fix formatting in place
dotnet format --verify-no-changes --no-restore   # exactly what CI runs (fails, changes nothing)
```

Three layers enforce style, and all three are fatal:

| Layer | Configured in | Effect |
|---|---|---|
| `dotnet format` | `.editorconfig` | CI gate (`ci.yml`). No local pre-commit hook exists — run it yourself. |
| Roslyn / .NET analyzers | `Directory.Build.props` (`EnableNETAnalyzers`, `AnalysisLevel=latest`, `EnforceCodeStyleInBuild=true`) | Runs during **every** build. |
| StyleCop.Analyzers 1.1.118 | `Directory.Build.props` + `stylecop.json` | Runs during **every** build. |

`Directory.Build.props` sits at the repo root and applies to all five projects, including the
test project. It sets `TreatWarningsAsErrors=true` and `CodeAnalysisTreatWarningsAsErrors=true`:
**any analyzer warning is a build error.** `ci.yml` re-asserts it with
`/p:TreatWarningsAsErrors=true`.

### Conventions that are enforced

| Convention | Source | Severity |
|---|---|---|
| File-scoped namespaces (`namespace Foo;`) | `csharp_style_namespace_declarations = file_scoped:warning` | error (warnings-as-errors) |
| Braces required on all blocks — including one-line `if` and lambdas | `csharp_prefer_braces = true:warning` | error |
| `using` directives outside the namespace, `System.*` first | `csharp_using_directive_placement`, `dotnet_sort_system_directives_first`, `stylecop.json` ordering rules | error |
| Interfaces prefixed `I` | `dotnet_naming_rule.interface_should_begin_with_i.severity = warning` | error |
| Types PascalCase | `dotnet_naming_rule.types_should_be_pascal_case.severity = warning` | error |
| Private fields `_camelCase` | `dotnet_naming_rule.private_fields_should_be_underscore_camel.severity = suggestion` | suggestion — not fatal, but follow it |
| 4-space indent, CRLF, UTF-8, final newline, no trailing whitespace | `.editorconfig` `[*]` + `stylecop.json` `newlineAtEndOfFile: require` | `dotnet format` gate |
| 2-space indent for `.csproj` / `.props` / `.targets` / `.xml` / `.json`; 4 for `.xaml` | `.editorconfig` | `dotnet format` gate |

`<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` are set in all five
csproj files — nullable-reference warnings are errors too.

### Reading the common failures

| Failure | What it means | Fix |
|---|---|---|
| `SA1503` | A block is missing braces. The repo hits this most often on **inline lambdas** (`x => DoThing();` written without a body block) — recorded in [`../CLAUDE.md`](../CLAUDE.md) Project Notes. | Add the braces. Do not add a suppression. |
| A style diagnostic on namespaces | The file uses a block-scoped `namespace Foo { … }`. | Convert to `namespace Foo;`. `dotnet format` does this for you. |
| A naming diagnostic on a new interface or type | Missing `I` prefix, or non-PascalCase type name. | Rename. |
| A nullable warning (`CS86xx`) | Nullable reference violation. | Fix the nullability; do not add `!` reflexively. |
| Format check passes locally but fails in CI | Line endings. `.editorconfig` pins `end_of_line = crlf`. | Run `dotnet format` and commit the result. |

`stylecop.json` turns on `documentInterfaces` and `documentExposedElements`, but `.editorconfig`
sets `SA1600`/`SA1601`/`SA1602` to `none` — so **XML doc comments on members are not enforced**
in practice. Write them where they earn their keep.

`.editorconfig` carries **14 deliberate StyleCop suppressions**, each with a rationale comment:
`SA1101 SA1309 SA1633 SA1200 SA1202 SA1600 SA1601 SA1602 SA1000 SA1204 SA0001 SA1118 SA1008
SA1009`. Adding a fifteenth is a decision, not a shortcut — fix the finding first, and if the
rule genuinely conflicts with the codebase's conventions, document why in the same commit.

---

## Adding code

The dependency direction is `UI → Application → Core ← Infrastructure`. Nothing may point back
up. Place new code accordingly:

| You are adding | Goes in | Namespace | Registration | Tests required |
|---|---|---|---|---|
| A data model / enum | `src/WindowsFileManager.Core/Models/` | `WindowsFileManager.Core.Models` | none — project references already wired | **Yes** — `tests/WindowsFileManager.Tests/Models/<Name>Tests.cs`. In the coverage `Include`. |
| An abstraction over I/O | `src/WindowsFileManager.Core/Services/` | `WindowsFileManager.Core.Services` | add the member to `IFileSystemService` and implement it in Infrastructure | Interface itself is not executable; the consumers that use it are gated. |
| Business logic / a service | `src/WindowsFileManager.Application/Services/` | `WindowsFileManager.Application.Services` | constructor-inject its dependencies (see wiring below) | **Yes** — `tests/WindowsFileManager.Tests/Services/<Name>Tests.cs` with Moq. In the coverage `Include`. |
| A real `System.IO` / COM / P-Invoke implementation | `src/WindowsFileManager.Infrastructure/Services/` | `WindowsFileManager.Infrastructure.Services` | wire it where the consumer is constructed | **No** — not in `Include`, so not gated. Keep it logic-free. |
| A ViewModel | `src/WindowsFileManager/ViewModels/` | `WindowsFileManager.ViewModels` | expose it from `MainViewModel` or bind it in XAML | **Yes** — `Include` covers `WindowsFileManager.ViewModels*`. Note `MainViewModel` itself is `[ExcludeFromCodeCoverage]`; a new ViewModel without that attribute is fully gated. |
| A converter / attached behavior / helper | `src/WindowsFileManager/Helpers/` | `WindowsFileManager.Helpers` | reference it from XAML | **Yes** — `Include` covers `WindowsFileManager.Helpers*`. Existing examples: `tests/.../Helpers/ConverterTests.cs`, `PercentToWidthConverterTests.cs`, `RelayCommandTests.cs`, `ViewModelBaseTests.cs`. |
| A View / window / code-behind | `src/WindowsFileManager/Views/` | `WindowsFileManager.Views` | `App.xaml` `StartupUri` for a startup window; otherwise construct it from code-behind | **No** — excluded by `[WindowsFileManager]*Views*`. Keep code-behind to what bindings genuinely cannot do. |
| A whole new project | `src/` or `tests/` | — | add it to `WindowsFileManager.sln`; it inherits `Directory.Build.props` automatically | Decide explicitly whether to add it to the coverage `Include` in the test csproj. |

### How wiring works (there is no DI container)

`App.xaml.cs` is an empty `partial class App`. `MainWindow.xaml` constructs the ViewModel in
markup:

```xml
<Window.DataContext><vm:MainViewModel /></Window.DataContext>
```

That calls `MainViewModel`'s parameterless constructor, which chains to
`MainViewModel(DuplicateScannerService, SettingsService, IFileSystemService)` via private
`CreateDefaultScanner()` / `CreateDefaultSettings()` factories. **"Registering" a new service
therefore means adding it to that constructor chain**, not editing a container configuration.

Commands are hand-rolled MVVM: `ViewModelBase` provides `SetProperty`/`OnPropertyChanged`, and
`RelayCommand` implements `ICommand` over `Action<object?>` with `CanExecuteChanged` forwarded to
`CommandManager.RequerySuggested`. See [ADR-002 in the decision index](adr/README.md) for why no
MVVM framework is used.

### Test conventions

- Mirror the source folder: `Models/` → `Models/`, `Services/` → `Services/`, `Helpers/` →
  `Helpers/`. One test class per production type, named `<Type>Tests`.
- AAA (Arrange / Act / Assert), FluentAssertions for assertions, Moq for `IFileSystemService`.
- Cover branches, not just lines. `ThresholdType` includes `branch`, so an unexercised `else`,
  an unhit `catch`, or an untaken ternary arm fails the build.
- Behavior-pinning tests are load-bearing here — e.g. the enum-ordinal tests described in the
  next section. Do not delete or weaken one to make a change fit.

---

## Settings and back-compat rules

`settings.json` in `%APPDATA%\WindowsFileManager\` is read by `SettingsService.Load()` with
`System.Text.Json`. Any change to a serialized model is a compatibility event. Two rules are
mandatory.

**1. `[JsonIgnore]` every computed property on a serialized model.** Getter-only properties must
carry `[System.Text.Json.Serialization.JsonIgnore]` or they are written into the file and
re-read on load. Existing examples: `FilterRule.Priority`, `FilterRule.DisplaySummary`,
`FolderSearchPattern.Priority`. `FilterRuleTests` asserts both that the serialized JSON contains
neither name and that a legacy blob still containing `DisplaySummary` deserializes cleanly.

**2. Never renumber an enum.** `System.Text.Json` serializes enums as **integers** by default, so
an ordinal is a persisted value. Renaming a member is safe; reordering or inserting one silently
reinterprets every existing settings file. Two tests pin this and will fail if you break it:

| Enum | Ordinals | Pinned by |
|---|---|---|
| `ActionHistoryKind` | `MoveFiles`=0, `RecycleFiles`=1, `RecycleDirectories`=2, `CreateShortcuts`=3 | `ActionHistoryEntryTests.ActionHistoryKind_Ordinals_Preserved` |
| `FolderMatchType` | `Include`=0, `Match`=1, `Contains`=2, `Exclude`=3, `Mismatch`=4, `NotContain`=5 | `FolderSearchPatternTests.FolderMatchType_Ordinals_Preserved` |
| `FilterAction` | `Include`=0, `Exclude`=1 | indirectly, via the `FilterRule` and `SettingsService` legacy-JSON tests |
| `FilterTarget` | `Filename`=0, `Filepath`=1 | as above |

Append new members at the end. The precedent is `FilterAction.Select` → `FilterAction.Contains`,
renamed for clarity with ordinal 0 unchanged.

**What already protects old files** (do not undo any of it):

- `System.Text.Json` ignores unknown JSON properties, so a removed or `[JsonIgnore]`d member in
  an old file does not break `Load()`.
- Absent properties fall back to the C# property initializers.
- `SettingsService.Load()` migrates the legacy **flat** schema into a single `"Default"` profile
  when `Profiles.Count == 0`, with per-token type checks that fall back to each property's
  default.
- Two levels of exception tolerance: whole-file `JsonException` → defaults
  (`SettingsService.cs:45`); migration `JsonException` → keep the fields populated so far and
  abandon every field still unread (`SettingsService.cs:125`). There is **no** per-element
  tolerance: `ReadObjectList` (`SettingsService.cs:153-168`) has no per-item `try`/`catch`, so one
  malformed `FilterRules` entry throws straight out to the migration handler and
  `FolderSearchPatterns` — read after it — is left empty.
  `SettingsServiceTests.Load_LegacyFilterRulesWithTypeMismatch_ShouldSwallowJsonException`
  (`SettingsServiceTests.cs:218`) pins exactly this: `TargetPaths` survives, `FilterRules` does not.

**Checklist when you change a serialized model:** add `[JsonIgnore]` to any new computed
property · append (never insert) enum members · give every new property a sensible initializer ·
add a round-trip test · re-read [`specs/SPEC-009-settings-and-window-state-persistence.md`](specs/SPEC-009-settings-and-window-state-persistence.md)
and update it in the same commit if behavior changed.

---

## MSIX and certificate workflow

There is **no** Windows Application Packaging project and no `WindowsPackageType=MSIX` build in
the CI path. The MSIX is assembled by hand in `.github/workflows/msix-pipeline.yml`. The
`dotnet publish … -p:WindowsPackageType=MSIX` one-liner survives in exactly one place — the
"Next steps" banner printed by `scripts/New-DevCertificate.ps1` (line 50). It is not what produces
the shipped package, and the property is **inert** here: `src/WindowsFileManager/WindowsFileManager.csproj`
declares no `WindowsPackageType`, there is no Windows App SDK reference, and there is no `.wapproj`
(recorded in [`../CLAUDE.md`](../CLAUDE.md)). Use the two-step publish-then-pack sequence under
[Reproducing the package locally](#reproducing-the-package-locally) instead.
See [ADR-008 in the decision index](adr/README.md).

### The pipeline (authoritative)

```
security-scan (ubuntu, semgrep p/default + p/csharp --error)
        ↓  needs
build-and-package (windows)
    dotnet restore
    dotnet build -c Release --no-restore
    dotnet test  -c Release --no-build --collect:"XPlat Code Coverage" --settings …/coverlet.runsettings
    dotnet publish src/WindowsFileManager -c Release -r win-x64 --self-contained true -o publish-output
    msix-layout\  ← publish-output\*  +  Package.appxmanifest → AppxManifest.xml  +  Assets\*.png
    makeappx.exe pack /d msix-layout /p output\WindowsFileManager.msix /o
    [signtool sign …]      ← only when CERTIFICATE_PFX exists AND push to main
    upload artifact: msix-package
        ↓  needs
wack-validation (windows)
    appcert.exe test -appxpackagepath <msix> -reportoutputpath wack-report.xml
    upload artifact: wack-report
```

A Semgrep finding fails `security-scan` (`--error`) and blocks every downstream job.

### Reproducing the package locally

Needs the Windows 10 SDK on the machine (see [Troubleshooting](#troubleshooting)).

```powershell
# 1. Publish self-contained, exactly as CI does
dotnet publish src\WindowsFileManager -c Release -r win-x64 --self-contained true -o publish-output

# 2. Build the layout by hand
New-Item -ItemType Directory -Path msix-layout -Force | Out-Null
Copy-Item publish-output\* msix-layout -Recurse -Force
Copy-Item src\WindowsFileManager\Package.appxmanifest msix-layout\AppxManifest.xml -Force
New-Item -ItemType Directory -Path msix-layout\Assets -Force | Out-Null
Copy-Item src\WindowsFileManager\Assets\*.png msix-layout\Assets -Force

# 3. Pack (locate the newest x64 makeappx.exe under the Windows Kit, as CI does)
& "<windows-kit>\bin\<version>\x64\makeappx.exe" pack /d msix-layout /p output\WindowsFileManager.msix /o

# 4. Sign (optional locally; CI signs only on push to main)
& "<windows-kit>\bin\<version>\x64\signtool.exe" sign /fd SHA256 `
    /tr http://timestamp.digicert.com /td SHA256 `
    /f .\certificate.pfx /p <password> output\WindowsFileManager.msix
```

### Certificates

`scripts/New-DevCertificate.ps1` (run **elevated**) creates a self-signed code-signing
certificate, exports it to `.\certificate.pfx`, and imports it into
`Cert:\LocalMachine\TrustedPeople` so the package can be sideloaded:

```powershell
.\scripts\New-DevCertificate.ps1                       # uses the built-in defaults
.\scripts\New-DevCertificate.ps1 -Password '<your-own>'  # override the default password
```

Two rules that break things when ignored:

- **`-Subject` MUST equal the manifest `Publisher`.** Both are `CN=WindowsFileManager` today
  (`src/WindowsFileManager/Package.appxmanifest` → `Identity Publisher`). Change one without the
  other and `signtool` or the install will fail. The script's own header states this.
- **The script's default `-Password` is a literal committed in the repo.** Pass your own
  password for anything you intend to keep, and never upload a PFX generated with the default to
  GitHub Secrets.

For a Store submission, replace the self-signed certificate with one from a trusted CA and set
`Publisher` to that certificate's exact subject.

### GitHub Secrets

| Secret | Content | Consumed at |
|---|---|---|
| `CERTIFICATE_PFX` | base64 of the `.pfx` | `msix-pipeline.yml` — decode + sign steps |
| `CERTIFICATE_PASSWORD` | the PFX password | `msix-pipeline.yml` — signtool `/p` |

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes(".\certificate.pfx")) | Set-Clipboard
```

Signing runs only when both of these hold: the secret is present **and**
`github.event_name == 'push' && github.ref == 'refs/heads/main'`. Pull-request runs produce an
unsigned MSIX by design — do not remove that branch condition. `.gitignore` covers `*.pfx`,
`*.cer`, `*.pem`, `*.key`, `*.msix`, and `.env`; keep it that way. More in
[`SECURITY.md`](SECURITY.md).

### Reading a WACK report

1. Actions → the pipeline run → **Artifacts** → download `wack-report`.
2. Open `wack-report.xml` and search for `RESULT="FAIL"`.
3. Each failing `<TEST>` element carries its own description and remediation text — fix the
   cause in the manifest or the published payload, not the report.

The WACK job runs `if`-less after `build-and-package`, so a failed `appcert.exe` exit code fails
the job; the report artifact is still uploaded (`if: always()`).

---

## Version numbers

Three places carry a version and **nothing keeps them in sync** — no CI step validates them:

| Where | Format | Notes |
|---|---|---|
| `src/WindowsFileManager/Package.appxmanifest` → `Identity Version` | 4-part `x.y.z.0` | currently `1.0.0.0` |
| `CHANGELOG.md` | Keep a Changelog + SemVer | — |
| csproj `<Version>` / `<AssemblyVersion>` | — | **not set anywhere**; `AssemblyInfo.cs` carries only `[assembly: ThemeInfo(...)]` |

Bump the manifest and the changelog together in the release commit.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `No .NET SDKs were found` / `dotnet build` unrecognized | The `dotnet` on `PATH` is a runtime-only host. | Prepend `D:\_env_storeage\dotnet` to `PATH`, or call `D:\_env_storeage\dotnet\dotnet.exe` directly. See [Prerequisites](#the-sdk-is-not-on-path-on-this-machine). |
| `dotnet test` fails with a coverlet threshold error | New or changed code is not fully covered — the gate is `line,branch,method` at 100%. | Add tests, or apply one of the options in [When a new file drops coverage](#when-a-new-file-drops-coverage). Never lower `<Threshold>`. |
| Build fails on a warning such as `SA1503` | `TreatWarningsAsErrors=true` in `Directory.Build.props`. | Fix the code (add braces, rename, resolve nullability). Suppressions belong in `.editorconfig` only with a documented rationale. |
| `dotnet format --verify-no-changes` fails in CI, passes locally | Line endings or an unformatted file. `.editorconfig` pins CRLF. | Run `dotnet format` locally and commit the diff. |
| Editing code changes nothing in the running app | There is no hot reload; `dotnet watch` does not work for this WPF app. | Stop the app, `dotnet build`, `dotnet run --project src/WindowsFileManager`. |
| `makeappx.exe` / `signtool.exe` / `appcert.exe` not found | The Windows 10 SDK is not installed. Verified absent on the reference machine (`C:\Program Files (x86)\Windows Kits\10\` does not exist). | Install the Windows 10 SDK, or let CI produce the package — building, testing, and running the app do not need it. |
| `signtool` fails, or the MSIX will not install | The signing certificate's Subject does not match `Publisher` in `Package.appxmanifest`, or the cert is not in `TrustedPeople`. | Regenerate with `-Subject` equal to the manifest `Publisher`, and run `New-DevCertificate.ps1` elevated so the `TrustedPeople` import succeeds. |
| CI is green but the MSIX is unsigned | `CERTIFICATE_PFX` is unset, or the run is not a push to `main` — the signing steps are conditioned on both. | Expected on PRs. Add the secret and push to `main` for a signed package. |
| `msix-pipeline` fails before building | The `security-scan` job runs Semgrep with `--error`; every downstream job has `needs: security-scan`. | Read the SARIF in the Security tab, fix the finding. |
| CI dependency-audit step fails | `dotnet list package --vulnerable --include-transitive` reported a vulnerable package. | Upgrade the offending package. Reproduce locally with the same command. |
| The `coverage-report` CI artifact is empty | CI uploads `tests/**/TestResults/**/coverage.cobertura.xml`, the path the **collector** writes, but the test project references `coverlet.msbuild` only. | Read `tests/WindowsFileManager.Tests/coverage/coverage.cobertura.xml` from a local `dotnet test` instead. |
| An `internal` type in `WindowsFileManager.Application` is not visible from a test | The Application csproj declares `InternalsVisibleTo("WindowsFileManager.Application.Tests")` — an assembly that does not exist. The real test project is `WindowsFileManager.Tests`. | Make the type `public`, or add the correct `InternalsVisibleTo`. (Core already grants access to `WindowsFileManager.Tests`.) |
| The app "forgets" settings after being killed | Settings are written on every mutation, so this should not happen — but a hard kill during a write can truncate `settings.json`. | Delete `%APPDATA%\WindowsFileManager\settings.json`; `Load()` falls back to defaults. |

---

## See also

| Document | Read it for |
|---|---|
| [`README.md`](README.md) | The docs portal — index of everything in this folder |
| [`../README.md`](../README.md) | The feature tour and what the app does |
| [`../CLAUDE.md`](../CLAUDE.md) | Agent instructions, conventions, project notes |
| [`../ARCHITECTURE.md`](../ARCHITECTURE.md) | Module map, dependency direction, build outputs |
| [`CONTEXT.md`](CONTEXT.md) | Why the project exists, key user flows, non-goals |
| [`SECURITY.md`](SECURITY.md) | Trust boundaries, destructive-operation rules, secret handling |
| [`GLOSSARY.md`](GLOSSARY.md) | Terms used across the code and docs |
| [`adr/README.md`](adr/README.md) | Why the build, test, and packaging setup is the way it is |
| [`specs/_index.md`](specs/_index.md) | Current-truth behavior contract per feature |
| [`modules/`](modules/) | Per-module mechanics |
