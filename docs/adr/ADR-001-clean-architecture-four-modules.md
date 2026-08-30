# ADR-001: Clean Architecture with four modules (Core / Application / Infrastructure / UI)

## Status

Accepted — 2026-04-04 (commit `7b59636` "Restructure to modular monorepo with Clean Architecture layers")

## Context

The initial commit (`57de160`, 2026-04-04, "Initial commit: WPF duplicate file finder with multi-folder
support") shipped everything in one WPF project: models, scan logic, and UI in a single assembly. Nothing in
it could be unit-tested — the scan logic called `System.IO` directly, and the only class holding it also held
`INotifyPropertyChanged` plumbing and WPF types.

Two goals were incompatible with that shape:

1. A meaningful automated test suite. Testing "find duplicates" required a real directory tree on disk.
2. A coverage gate with teeth (later formalised in [ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md)).
   Code that touches the filesystem cannot reach 100% without either real I/O or a seam.

Later the same day the repository was restructured into four projects.

## Decision

Split into four projects under `src/`, with dependencies flowing **UI → Application → Core ← Infrastructure**.
No project depends on Infrastructure except the UI, which composes the graph at the outermost edge.

| Project | TFM | References | Holds |
|---|---|---|---|
| `WindowsFileManager.Core` | `net8.0-windows` | *(none — zero `PackageReference`)* | Models (`ScannedFile`, `DuplicateGroup`, `ScanOptions`, `ScanResult`, `FilterRule`, `ProfileSettings`, `AppSettings`, …) and the `IFileSystemService` interface |
| `WindowsFileManager.Application` | `net8.0-windows10.0.22621.0` | Core | Business logic: `DuplicateScannerService`, `FileHashService`, `SettingsService` |
| `WindowsFileManager.Infrastructure` | `net8.0-windows` | Core | `FileSystemService` — the only real-I/O implementation |
| `WindowsFileManager` (WPF UI) | `net8.0-windows10.0.22621.0` | Core, Application, Infrastructure | Views, ViewModels, Helpers |

Core declares `InternalsVisibleTo` for `WindowsFileManager.Application` and `WindowsFileManager.Tests`
([`../../src/WindowsFileManager.Core/WindowsFileManager.Core.csproj`](../../src/WindowsFileManager.Core/WindowsFileManager.Core.csproj) lines 10–11).

Composition is not a container. `MainWindow.xaml` constructs the ViewModel declaratively
(`<Window.DataContext><vm:MainViewModel /></Window.DataContext>`), and `MainViewModel`'s parameterless
constructor chains to the injectable one via two static factories:

```csharp
public MainViewModel()
    : this(CreateDefaultScanner(), CreateDefaultSettings(), new FileSystemService())
```

(`MainViewModel.cs:606-607`, factories at `:4943-4955`). Infrastructure is therefore referenced from exactly
one place, and every test constructs the services with a `Mock<IFileSystemService>` instead.

## Consequences

### Positive

- Application services are constructor-injected and fully mockable — the entire 217-test suite runs with no
  disk I/O, no fixtures, and no temp directories.
- Core carries **zero package references**, so the dependency-vulnerability gate
  ([`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) lines 40–48) has nothing to flag in the
  layer that holds the domain model.
- Infrastructure can be excluded from the coverage denominator without hiding business logic, which is what
  makes the 100% gate achievable ([ADR-004](ADR-004-ifilesystemservice-io-abstraction.md),
  [ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md)).
- The scan algorithm is a pure function of `ScanOptions` + an `IFileSystemService` — it is testable, and it is
  reusable by any future front end.

### Negative

- Four `.csproj` files, four `AssemblyInfo` surfaces, and a five-project solution for one desktop application.
  Every new model type requires a decision about which project it belongs in.
- **There is no composition root.** There is no DI container and no service locator; wiring is two static
  factory methods plus a `new FileSystemService()` in a parameterless constructor that XAML invokes. The
  layering is real; the container is not. Anyone looking for a composition root will not find one.
  ([`../../CLAUDE.md`](../../CLAUDE.md) line 104 states the same: "there is **no DI container**".)
- **`WindowsFileManager.Application.csproj:14` declares `InternalsVisibleTo("WindowsFileManager.Application.Tests")`
  — an assembly that does not exist.** The real test project is `WindowsFileManager.Tests`, so Application
  internals are *not* visible to the tests that cover it. The line is dead configuration that reads as if it
  were doing something.
- **The boundary leaks for folder search.** Folder search — a headline user feature — is implemented in the UI
  layer (`MainViewModel.SearchFoldersAsync` / `SearchFoldersRecursive`), not in Application, and its
  `FolderContainsItem` helper calls `System.IO.Directory` / `File` directly instead of going through
  `IFileSystemService`. The rule holds for duplicate detection and settings; it does not hold everywhere.
  See [`../specs/SPEC-007-folder-search.md`](../specs/SPEC-007-folder-search.md).
- Cross-layer refactors touch several projects at once; a change to a Core model ripples through Application,
  the UI, and the test project in a single commit.

### Neutral

- Target-framework moniker drift is deliberate but uneven: Core and Infrastructure target `net8.0-windows`,
  while Application, the UI, and the test project target `net8.0-windows10.0.22621.0`. Nothing in the build
  reconciles them.
- All four projects build as `Any CPU`; `win-x64` appears only at publish/packaging time
  ([ADR-008](ADR-008-msix-packaging-anycpu-store.md)).
- `Directory.Build.props` at the repo root applies analyzers and warning policy to all five projects uniformly
  ([ADR-009](ADR-009-treat-warnings-as-errors.md)).

## Links

- [ADR-002](ADR-002-hand-rolled-mvvm.md) — the UI layer's MVVM approach
- [ADR-004](ADR-004-ifilesystemservice-io-abstraction.md) — the seam that makes the layering testable
- [ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md) — the coverage scope that follows these boundaries
- [ADR-010](ADR-010-wpf-net8-desktop-shell.md) — why the outermost layer is WPF
- [`../../ARCHITECTURE.md`](../../ARCHITECTURE.md) · [`../modules/`](../modules/) · [`../CONTEXT.md`](../CONTEXT.md)
- Source: [`../../src/WindowsFileManager.Core/`](../../src/WindowsFileManager.Core/) ·
  [`../../src/WindowsFileManager.Application/`](../../src/WindowsFileManager.Application/) ·
  [`../../src/WindowsFileManager.Infrastructure/`](../../src/WindowsFileManager.Infrastructure/) ·
  [`../../src/WindowsFileManager/`](../../src/WindowsFileManager/)
