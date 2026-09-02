# WindowsFileManager.Infrastructure

`src/WindowsFileManager.Infrastructure/` — the I/O adapter.

## Purpose

Infrastructure exists to hold the one place in the codebase where `System.IO` is actually called. It contains a single class, `FileSystemService`, which implements Core's `IFileSystemService` by forwarding each member to the corresponding BCL API.

Keeping this adapter thin and separate is what lets Core and Application be tested entirely against a mock, and it is why this module is deliberately outside the coverage boundary — there is nothing here to unit-test that would not just be testing `System.IO`.

## Design

One namespace, one file, one class:

```
WindowsFileManager.Infrastructure/
  Services/FileSystemService.cs    WindowsFileManager.Infrastructure.Services    60 lines
```

**Dependency rule: Infrastructure depends on Core only.** One `ProjectReference` (Core), zero `PackageReference`. It does not reference Application, and Application does not reference it — the two are siblings, joined only at the UI's composition root.

**The module must stay thin.** Every member is a one-line forward to `System.IO`, with the single exception of the two enumeration methods, which construct an `EnumerationOptions` value. There is no caching, no retry, no logging, no path normalization, no business rule. Anything with a decision in it belongs in Application, where it can be tested.

**It is excluded from coverage on purpose.** The class carries `[ExcludeFromCodeCoverage]`, the assembly is absent from the coverlet `Include` list in `tests/WindowsFileManager.Tests/coverlet.runsettings`, and the test project does not even reference this project. Two independent mechanisms therefore keep it out of the 100% threshold. That is the trade recorded in ADR-004: pure delegation is verified by integration use, not by unit tests that would assert the framework's behavior.

## Key types

| Type | File | Responsibility |
|------|------|----------------|
| `FileSystemService` | `Services/FileSystemService.cs` | The production `IFileSystemService`. Public, `[ExcludeFromCodeCoverage]`, stateless, parameterless constructor. |

## Public API

```csharp
namespace WindowsFileManager.Infrastructure.Services;

[ExcludeFromCodeCoverage]
public class FileSystemService : IFileSystemService
```

The class has no declared constructor (the implicit parameterless one), no fields, and no members beyond the eleven interface implementations. Every member is `public` and carries `/// <inheritdoc/>`.

| Member | Signature | Real-I/O behavior |
|--------|-----------|-------------------|
| `EnumerateFiles` | `IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)` | `Directory.EnumerateFiles(path, searchPattern, new EnumerationOptions { RecurseSubdirectories = searchOption == SearchOption.AllDirectories, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System })` |
| `EnumerateDirectories` | `IEnumerable<string> EnumerateDirectories(string path)` | `Directory.EnumerateDirectories(path, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System })` |
| `GetFileSize` | `long GetFileSize(string filePath)` | `new FileInfo(filePath).Length` |
| `GetLastWriteTime` | `DateTime GetLastWriteTime(string filePath)` | `File.GetLastWriteTime(filePath)` — **local time** |
| `OpenRead` | `Stream OpenRead(string filePath)` | `File.OpenRead(filePath)` — caller owns disposal |
| `DirectoryExists` | `bool DirectoryExists(string path)` | `Directory.Exists(path)` |
| `FileExists` | `bool FileExists(string filePath)` | `File.Exists(filePath)` |
| `GetFileName` | `string GetFileName(string filePath)` | `Path.GetFileName(filePath)` — pure string, no disk access |
| `ReadAllText` | `string ReadAllText(string filePath)` | `File.ReadAllText(filePath)` |
| `WriteAllText` | `void WriteAllText(string filePath, string content)` | `File.WriteAllText(filePath, content)` — creates or overwrites |
| `CreateDirectory` | `void CreateDirectory(string path)` | `Directory.CreateDirectory(path)` — creates parents, no-op if present |

### Real-I/O semantics worth knowing

**Enumeration is lazy and partially fault-tolerant.** Both enumerate methods return a lazy `IEnumerable<string>`; the walk happens as the caller iterates, so an exception surfaces mid-`foreach`, not at the call site.

**`IgnoreInaccessible = true` is the only built-in filter that swallows errors.** A directory the process cannot read is skipped silently instead of throwing `UnauthorizedAccessException`. This is why a scan over `C:\` completes at all. It also means a scan can silently see fewer files than the user expects, with no signal.

**`AttributesToSkip = FileAttributes.System` — and nothing else.** This overrides the .NET default of `Hidden | System`, so **hidden files and hidden directories ARE enumerated**. Only system-attributed entries are skipped. That is a deliberate widening relative to the framework default.

**`searchPattern` is honored, `SearchOption` is translated.** The pattern string is passed straight through; `SearchOption.AllDirectories` becomes `RecurseSubdirectories = true` and anything else becomes `false`. Application only ever passes `"*.*"` (files) or `"*"` (directories).

**Everything else throws normally.** `GetFileSize` on a missing file throws `FileNotFoundException`; `OpenRead` on a locked file throws `IOException`; `WriteAllText` to an unwritable path throws. No member catches anything. Callers in Application and the UI decide what to tolerate.

**No elevation, no impersonation.** The app runs as a plain non-elevated `WinExe` (no `app.manifest`, no `requestedExecutionLevel`), so every call runs under the user's own token.

**Times are local.** `GetLastWriteTime` returns local time, matching `ActionHistoryEntry.Timestamp`'s `DateTime.Now`. Nothing in the codebase converts to UTC for storage.

## Rules & constraints

1. **Keep it a pure adapter.** Every member is a forward. Do not add caching, retry loops, logging, path canonicalization, permission probing, or any conditional that is not already framework configuration. A branch added here is a branch that no test can reach.
2. **Never put a decision here.** If a caller needs "skip files over N bytes" or "retry once on `IOException`", that belongs in Application where `IFileSystemService` can be mocked and the branch can be covered.
3. **This module is the only place `System.IO.File` / `System.IO.Directory` may be called for scanning work.** Two known exceptions exist in the UI project and are called out honestly: `MainViewModel.FolderContainsItem` and `GetDirectorySize` use `Directory`/`File` directly, and the destructive operations (`File.Move`, `Directory.Delete`, the `Microsoft.VisualBasic.FileIO.FileSystem` recycle calls) do too. Those are pre-existing bypasses of the port, not a pattern to extend — new I/O should go through `IFileSystemService`.
4. **Do not add a `ProjectReference` to Application or the UI.** The dependency arrow points at Core only. Infrastructure is a leaf that the composition root plugs in.
5. **Adding a member means changing the port first.** A new capability is declared on `IFileSystemService` in Core (with XML docs — StyleCop's `documentInterfaces` is on and warnings are errors), then implemented here, then mocked in the affected Application tests.
6. **Keep `[ExcludeFromCodeCoverage]` on the class.** Removing it does not currently change the measured percentage — the assembly is not in the coverlet `Include` list and the test project does not reference it — but the attribute is the visible marker of intent.
7. **Thread safety: stateless, therefore safe to share.** `FileSystemService` has no fields, so one instance can be used from any number of threads. The *underlying filesystem* is not transactional: a file can vanish between `FileExists` and `OpenRead`. Callers must handle that race; the adapter does not.
8. **Disposal is the caller's job.** `OpenRead` hands back an undisposed `FileStream`. `FileHashService` wraps it in a `using`; any new caller must do the same.

## Testing

**There are no unit tests for this module, and that is the intended design.**

- The test project (`tests/WindowsFileManager.Tests/WindowsFileManager.Tests.csproj`) references Core, Application, and the UI project — **not Infrastructure**. It cannot see this class.
- The coverlet `Include` list, in `tests/WindowsFileManager.Tests/coverlet.runsettings`, is `[WindowsFileManager.Core]*,[WindowsFileManager.Application]*,[WindowsFileManager]WindowsFileManager.Helpers*,[WindowsFileManager]WindowsFileManager.ViewModels*`. `WindowsFileManager.Infrastructure` is absent, so it contributes nothing to the 100% line/branch/method threshold that `scripts/Check-Coverage.ps1` enforces.
- `FileSystemService` also carries `[ExcludeFromCodeCoverage]`, which the same file's `ExcludeByAttribute` list would honor even if the assembly were included.

**Why:** every member is a one-line delegation to a framework API. A unit test would either mock `System.IO` (impossible without a further abstraction, which is what this class already is) or hit the real disk, making the suite slow, machine-dependent, and non-deterministic — the exact properties the `IFileSystemService` seam was introduced to avoid. Correctness of the delegation is established by running the application; correctness of everything built on top is established by the 50 tests that put a `Mock<IFileSystemService>` in this class’s place — all 50 of them in Application (`DuplicateScannerServiceTests` 27, `SettingsServiceTests` 20, `FileHashServiceTests` 3). The suite’s other 167 tests use no mock at all: 122 over Core’s models, and 45 over the UI assembly’s `Helpers`/`ViewModels`, not Core or Application.

**How it is exercised in practice:** `MainViewModel.CreateDefaultScanner()` and `CreateDefaultSettings()` construct a real `FileSystemService` at startup, so every scan, folder search, and settings save in a running app goes through this class.

**If you change a member's semantics** (for example, adding `FileAttributes.Hidden` back to `AttributesToSkip`), nothing in the test suite will fail. The change must be validated by running the app and stated in [SPEC-001](../specs/SPEC-001-duplicate-detection.md) and this document in the same commit.

## Links

- [`../../ARCHITECTURE.md`](../../ARCHITECTURE.md) — system map and layer boundaries
- [`../adr/`](../adr/) — ADR-001 (four-module Clean Architecture), ADR-004 (all I/O behind `IFileSystemService`, with Infrastructure excluded from coverage), ADR-011 (100% coverage measured by coverlet.collector and enforced by `scripts/Check-Coverage.ps1`; supersedes ADR-005)
- [core.md](core.md#ifilesystemservice) — the `IFileSystemService` port this implements
- [application.md](application.md) — the consumers that run against this in production and against a mock in tests
- [ui.md](ui.md) — the composition root that instantiates it
- [`../SECURITY.md`](../SECURITY.md) — what `IgnoreInaccessible` and the hidden-file behavior mean for the trust boundary
- [`../DEV.md`](../DEV.md) — build and test commands
