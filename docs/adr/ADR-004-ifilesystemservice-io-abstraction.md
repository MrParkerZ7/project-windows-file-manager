# ADR-004: All I/O behind `IFileSystemService`, with Infrastructure excluded from coverage

## Status

Accepted — 2026-04-04 (commit `7b59636`; `IFileSystemService.cs` and `FileSystemService.cs` added in the
Clean Architecture restructure)

## Context

The business logic worth testing — duplicate detection ([ADR-003](ADR-003-three-stage-duplicate-detection.md))
and settings load/migrate/save — is inseparable from filesystem calls if written directly against
`System.IO`. Testing it that way means creating real directory trees, real files with controlled sizes and
contents, and real permission failures, then cleaning them up. That is slow, flaky on CI, and impossible to
drive into edge cases like `UnauthorizedAccessException` mid-recursion.

A 100% line/branch/method coverage gate ([ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md)) makes
this worse: every `catch (IOException)` branch has to be *reached* by a test, and there is no reliable way to
provoke one against a real disk on a hosted runner.

## Decision

Define one interface in Core and route the testable layers through it.

**`IFileSystemService`**
([`../../src/WindowsFileManager.Core/Services/IFileSystemService.cs`](../../src/WindowsFileManager.Core/Services/IFileSystemService.cs))
— 11 members: `EnumerateFiles(path, searchPattern, SearchOption)`, `GetFileSize`, `GetLastWriteTime`,
`OpenRead`, `DirectoryExists`, `GetFileName`, `FileExists`, `ReadAllText`, `WriteAllText`, `CreateDirectory`,
`EnumerateDirectories`. Its own doc comment states the purpose: *"Abstraction over file system operations for
testability."*

**`FileSystemService`**
([`../../src/WindowsFileManager.Infrastructure/Services/FileSystemService.cs`](../../src/WindowsFileManager.Infrastructure/Services/FileSystemService.cs))
is the single implementation, and is the only place in Application or Infrastructure that touches `System.IO`
directly. It sets `IgnoreInaccessible = true` and `AttributesToSkip = FileAttributes.System` on its
`EnumerationOptions` (lines 17–21).

**Infrastructure is excluded from coverage by two independent mechanisms**, both present today:

1. The coverage `Include` list in
   [`../../tests/WindowsFileManager.Tests/coverlet.runsettings`](../../tests/WindowsFileManager.Tests/coverlet.runsettings)
   (line 20) names only `[WindowsFileManager.Core]*`, `[WindowsFileManager.Application]*`,
   `[WindowsFileManager]WindowsFileManager.Helpers*`, and `[WindowsFileManager]WindowsFileManager.ViewModels*`.
   `WindowsFileManager.Infrastructure` is simply not in scope.
2. `FileSystemService` additionally carries `[ExcludeFromCodeCoverage]` (line 10), and
   `ExcludeFromCodeCoverageAttribute` is listed in `ExcludeByAttribute` (runsettings line 23).

Both filters lived in the test `.csproj` until 2026-09-02, when
[ADR-011](ADR-011-coverage-via-collector-and-script.md) replaced `coverlet.msbuild` with the
`coverlet.collector` data collector and moved them — unchanged — into `coverlet.runsettings`, now the single
source of coverage scope. The exclusion itself is unaffected.

Consumers take the interface by constructor: `DuplicateScannerService(IFileSystemService, FileHashService)`,
`FileHashService(IFileSystemService)`, `SettingsService(IFileSystemService, string settingsPath)`.

## Consequences

### Positive

- The 50 tests that exercise the seam run against `Mock<IFileSystemService>` — no disk, no fixtures, no
  cleanup, no ordering dependency. They live in 3 of the suite's 20 test classes:
  `DuplicateScannerServiceTests` (27), `SettingsServiceTests` (20), and `FileHashServiceTests` (3). The
  remaining 167 of the 217 tests cover Core models and UI helpers directly and need no mock at all.
  `xunit.runner.json` disables assembly and collection parallelism, so runs are deterministic.
- Failure branches that are otherwise unreachable become ordinary tests: the exclusion walker's
  `catch (UnauthorizedAccessException)` / `catch (IOException)` paths, `DirectoryNotFoundException` on a
  missing target, and a mid-scan cancellation are all driven by mock setups.
- `SettingsService` is testable end-to-end — including the legacy flat-JSON migration and the corrupt-file
  fallback — because `ReadAllText` / `WriteAllText` / `FileExists` / `CreateDirectory` are all on the seam
  ([ADR-007](ADR-007-system-text-json-settings-compatibility.md)).
- Excluding Infrastructure keeps the 100% number honest about *business logic* rather than being met by
  trivially testing thin `System.IO` wrappers.

### Negative

- **Infrastructure has zero tests.** `FileSystemService`'s real behaviour is asserted by nothing. Its two
  non-obvious choices — `IgnoreInaccessible = true` (permission errors are silently skipped rather than
  surfaced) and `AttributesToSkip = FileAttributes.System` (system files skipped, **hidden files are not**) —
  are invisible to the suite. A change to either would break user-visible scan results with a green build.
- **Mocks can lie.** Tests pin what Application does with the *interface contract*, not what the real
  filesystem does. Any divergence between `IFileSystemService`'s implied semantics and `System.IO`'s actual
  semantics (path casing, long paths, reparse points, concurrent modification) passes undetected.
- **The abstraction is a convention, not an enforcement.** UI-layer code bypasses it routinely:
  `MainViewModel.FolderContainsItem` uses `System.IO.Directory` / `File` directly, `FlattenFolder` uses
  `File.Move`, `RemoveEmptyDirectoriesRecursive` uses `Directory.Delete`, and deletion goes through
  `Microsoft.VisualBasic.FileIO.FileSystem`. Nothing structural stops a new call site from doing the same —
  and because `MainViewModel` is `[ExcludeFromCodeCoverage]`, a bypass there costs nothing at the gate.
- **`[ExcludeFromCodeCoverage]` is a general escape hatch.** It is in `ExcludeByAttribute`, so the same
  attribute that legitimately marks Infrastructure can be applied to real business logic to get past the 100%
  threshold. See [ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md) § Negative.
- Every new I/O need means widening the interface and re-stubbing it across the existing mock setups.

### Neutral

- The interface has grown by accretion to 11 members. `GetFileName` is pure path manipulation with no I/O and
  needs no abstraction; it sits on the interface because it was convenient to mock alongside the rest.
- The exclusion is coverage-only. Infrastructure is still compiled, still analyzed, and still subject to
  `TreatWarningsAsErrors` ([ADR-009](ADR-009-treat-warnings-as-errors.md)).
- `Stream OpenRead(string)` is the only member returning a disposable; `FileHashService` disposes it with
  `using`.

## Links

- [ADR-001](ADR-001-clean-architecture-four-modules.md) — the layering this seam enforces
- [ADR-003](ADR-003-three-stage-duplicate-detection.md) — the primary consumer
- [ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md) — the coverage gate this exclusion serves
- [ADR-007](ADR-007-system-text-json-settings-compatibility.md) — settings persistence through the same seam
- [`../SECURITY.md`](../SECURITY.md) — what the real implementation actually touches
- [`../modules/`](../modules/) — Core and Infrastructure module documentation
- Source: [`../../src/WindowsFileManager.Core/Services/IFileSystemService.cs`](../../src/WindowsFileManager.Core/Services/IFileSystemService.cs) ·
  [`../../src/WindowsFileManager.Infrastructure/Services/FileSystemService.cs`](../../src/WindowsFileManager.Infrastructure/Services/FileSystemService.cs)
