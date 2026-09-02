# Module index

The solution (`WindowsFileManager.sln`) holds four production projects under `src/` and one test project under `tests/`. Dependencies point in one direction only — outward layers reference inward ones, never the reverse.

This directory is the **module-doc** home: how the code in each project actually works. It is one of four documentation kinds, each with exactly one home:

| Kind | Home | Answers |
|------|------|---------|
| Feature spec | [`../specs/`](../specs/) | how does this feature behave today, across modules? |
| Decision | [`../adr/`](../adr/) | why was it built this way? |
| Module doc | `docs/modules/` (here) | how does this code work, inside one module? |
| Project background | [`../CONTEXT.md`](../CONTEXT.md) | what is this for, and for whom? |

## The four modules

| Module | Purpose | Depends on | Depended on by | Doc |
|--------|---------|------------|----------------|-----|
| `WindowsFileManager.Core` | Domain models (scan options/results, duplicate groups, filter rules, settings, undo history) plus the `IFileSystemService` port that every I/O call goes through. | Nothing — no `ProjectReference`, no runtime `PackageReference` | Application, Infrastructure, UI, Tests | [core.md](core.md) |
| `WindowsFileManager.Application` | Use-case services: SHA256 hashing, the duplicate scan algorithm, and settings load/save with legacy migration. | Core | UI, Tests | [application.md](application.md) |
| `WindowsFileManager.Infrastructure` | The single real-I/O adapter — `FileSystemService`, which implements `IFileSystemService` over `System.IO`. | Core | UI (composition only) | [infrastructure.md](infrastructure.md) |
| `WindowsFileManager` (WPF shell) | The desktop application: `MainViewModel`, hand-rolled MVVM primitives, value converters, shell/COM helpers, and the XAML views. | Core, Application, Infrastructure | Tests (ViewModels + Helpers only) | [ui.md](ui.md) |

## Dependency rule

```
        WindowsFileManager (WPF)
          |        |         |
          |        |         +--> Infrastructure --+
          |        |                               |
          |        +--> Application ---------------+--> Core
          |                                        |
          +----------------------------------------+
```

- **Core depends on nothing.** Adding a `ProjectReference` or a runtime `PackageReference` to `WindowsFileManager.Core.csproj` breaks the rule. The one `PackageReference` Core does receive is not declared there: `Directory.Build.props` injects StyleCop.Analyzers 1.1.118 into every project, Core included — a build-time analyzer carrying `PrivateAssets=all`, so it adds nothing at runtime.
- **Application depends on Core only** — it consumes the `IFileSystemService` interface and never touches `System.IO` for real work, which is what makes it fully mockable.
- **Infrastructure depends on Core only** and is referenced solely by the UI project, which composes it into the services at startup.
- **The UI is the composition root.** There is no DI container; `MainViewModel`'s parameterless constructor wires `FileSystemService` into `FileHashService`, `DuplicateScannerService`, and `SettingsService`.
- **Tests reference Core, Application, and the UI project — not Infrastructure.** Infrastructure is deliberately outside the coverage boundary.

See [`../../ARCHITECTURE.md`](../../ARCHITECTURE.md) for the system-level view and [`../adr/`](../adr/) for the decisions behind this split (ADR-001 four-module Clean Architecture, ADR-004 all I/O behind `IFileSystemService`).

## Target frameworks

| Project | TargetFramework | Notes |
|---------|-----------------|-------|
| Core | `net8.0-windows` | No WPF, no packages |
| Application | `net8.0-windows10.0.22621.0` | No packages |
| Infrastructure | `net8.0-windows` | No packages |
| UI | `net8.0-windows10.0.22621.0` | `OutputType=WinExe`, `UseWPF=true`, `TargetPlatformMinVersion=10.0.17763.0`, `RuntimeIdentifiers=win-x64` |
| Tests | `net8.0-windows10.0.22621.0` | `UseWPF=true` (needed to load the UI assembly) |

All five enable `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`. All five inherit `Directory.Build.props`: .NET analyzers + StyleCop 1.1.118 with `TreatWarningsAsErrors=true`, so an analyzer finding is a build failure in every module. See [`../DEV.md`](../DEV.md) for the build commands and [`../adr/`](../adr/) ADR-009.

## Coverage boundary

Measured 2026-08-30: **217 tests, 0 failed, 0 skipped; 100% line / 100% branch / 100% method** on Core, Application, and the UI's ViewModels + Helpers namespaces. Infrastructure is excluded.

The boundary is defined in `tests/WindowsFileManager.Tests/coverlet.runsettings`, the single source of coverage scope, read by the `XPlat Code Coverage` data collector:

```xml
<Include>[WindowsFileManager.Core]*,[WindowsFileManager.Application]*,[WindowsFileManager]WindowsFileManager.Helpers*,[WindowsFileManager]WindowsFileManager.ViewModels*</Include>
```

The collector measures but cannot enforce, so the 100% line/branch/method threshold is applied afterwards by `scripts/Check-Coverage.ps1`. The full local gate is two commands — `dotnet test -c Release --collect:"XPlat Code Coverage" --settings tests/WindowsFileManager.Tests/coverlet.runsettings`, then `./scripts/Check-Coverage.ps1`; see [`../adr/ADR-011-coverage-via-collector-and-script.md`](../adr/ADR-011-coverage-via-collector-and-script.md).

`WindowsFileManager.Infrastructure` is absent from `Include`, so it contributes nothing to the percentage. Types marked `[ExcludeFromCodeCoverage]` are dropped via the same file's `ExcludeByAttribute`. Each module doc's `## Testing` section states exactly what is covered and what is not.
