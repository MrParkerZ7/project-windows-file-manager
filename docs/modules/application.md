# WindowsFileManager.Application

`src/WindowsFileManager.Application/` — the use-case layer.

## Purpose

Application holds the three services that do the actual work of the product: hashing a file's content, finding duplicates across a set of folders, and loading/saving the settings document. Each is a plain class with constructor-injected dependencies and no static state.

Every filesystem operation goes through `IFileSystemService`, which is why this entire module is testable with an in-memory mock and carries 100% line/branch/method coverage.

## Design

One namespace, three files:

```
WindowsFileManager.Application/
  Services/    WindowsFileManager.Application.Services
    FileHashService.cs           33 lines  — SHA256 over a stream
    DuplicateScannerService.cs  298 lines  — enumerate, filter, group, sort
    SettingsService.cs          217 lines  — JSON load/save + legacy migration
```

**Dependency rule: Application depends on Core only.** `WindowsFileManager.Application.csproj` has exactly one `ProjectReference` (Core) and zero `PackageReference`. It never references Infrastructure, and it must not — it depends on the `IFileSystemService` *interface*, and the concrete adapter is handed to it by the composition root in the UI project.

Three properties follow from that:

1. **No `System.IO` calls for real work.** The only `System.IO` types used are `Path` (pure string math), `SearchOption` (an enum passed through to the port), `Stream` (returned by the port), and the `DirectoryNotFoundException` / `IOException` types. Nothing here opens, reads, or lists anything itself.
2. **No async, no threads.** Every method is synchronous. Concurrency is the caller's problem — `MainViewModel` runs `Scan` inside `Task.Run` and passes a `CancellationToken`.
3. **No UI types.** No `INotifyPropertyChanged`, no dispatcher, no `MessageBox`. Progress is reported through a plain `Action<int>` callback that the caller is free to marshal wherever it likes.

**Known defect in the project file:** `<InternalsVisibleTo Include="WindowsFileManager.Application.Tests" />` names an assembly that does not exist — the test project is `WindowsFileManager.Tests`. Application internals are therefore **not** visible to the real test project. Nothing currently depends on it (everything under test is `public`), but the grant is dead as written.

## Key types

| Type | File | Responsibility |
|------|------|----------------|
| `FileHashService` | `Services/FileHashService.cs` | Computes the SHA256 content hash of one file, as an uppercase hex string. |
| `DuplicateScannerService` | `Services/DuplicateScannerService.cs` | The scan algorithm: enumerate → filter → group (by size+hash, or by name-regex) → sort. Owns cancellation and progress reporting. |
| `SettingsService` | `Services/SettingsService.cs` | Loads `AppSettings` from a JSON path (with two levels of fault tolerance plus a legacy flat-schema migration) and saves it back indented. |

## Public API

### `FileHashService`

```csharp
public FileHashService(IFileSystemService fileSystem)

public string ComputeHash(string filePath)
```

| Member | Parameters | Returns | Behavior |
|--------|-----------|---------|----------|
| `ComputeHash` | `string filePath` | `string` | `fileSystem.OpenRead(filePath)` → `SHA256.HashData(stream)` → `Convert.ToHexString(...)`. The stream is disposed by a `using` declaration. |

- Output is **uppercase hex** (`Convert.ToHexString` has no lowercase overload here).
- Deterministic and content-sensitive: same bytes → same hash; one byte different → different hash. An empty file yields a valid non-empty hash.
- **No cancellation parameter and no chunking** — the whole stream is consumed in one `HashData` call. A very large file cannot be interrupted mid-hash.
- **No size cap and no path exclusion.** Any file the scanner considers a size-collision candidate is read in full. See [`../SECURITY.md`](../SECURITY.md).
- Exceptions from `OpenRead` (access denied, file locked, file vanished) propagate to the caller unchanged.

### `DuplicateScannerService`

```csharp
public DuplicateScannerService(IFileSystemService fileSystem, FileHashService hashService)

public ScanResult Scan(
    ScanOptions options,
    Action<int>? progress = null,
    CancellationToken cancellationToken = default)
```

| Parameter | Type | Meaning |
|-----------|------|---------|
| `options` | `ScanOptions` | The scan request. See [core.md](core.md#scanoptions). |
| `progress` | `Action<int>?` | Optional. Invoked with the running count of **kept** files. Called synchronously on the scanning thread — marshal to the UI yourself. |
| `cancellationToken` | `CancellationToken` | Checked per file during collection, per size-group during hashing, and per file during regex matching. |
| **returns** | `ScanResult` | Groups sorted by `WastedBytes` descending. |

**Throws**

| Exception | When |
|-----------|------|
| `ArgumentException("At least one target path is required.")` | `options.TargetPaths.Count == 0` |
| `DirectoryNotFoundException($"Directory not found: {path}")` | any target path fails `DirectoryExists` — checked for **all** paths before any enumeration starts |
| `ArgumentException("Invalid duplicate-match regex: …")` | `MatchRegex` fails to compile |
| `ArgumentException("Duplicate-match regex timed out on '<file>'. …")` | a single match exceeds the 1-second `RegexMatchTimeout`; the whole scan aborts |
| `OperationCanceledException` | the token is cancelled (including if it is already cancelled on entry — thrown before any hashing) |

**Algorithm, in execution order**

1. **Guards.** Empty-path-list check, then the existence check for every target path. The `Stopwatch` starts *after* the guards, so a rejected scan reports no duration.
2. **Enumeration.** Two paths:
   - `ExcludeFolderNames` non-empty **and** `IncludeSubdirectories` → a private recursive walker `EnumerateFilesExcluding`, which lists the top directory's files, then recurses into `EnumerateDirectories` skipping any folder whose name is in an `OrdinalIgnoreCase` `HashSet`. `UnauthorizedAccessException` and `IOException` from `EnumerateDirectories` cause that branch to `yield break` rather than propagate.
   - otherwise → one `EnumerateFiles(path, "*.*", AllDirectories | TopDirectoryOnly)` per target path.
   Both paths end with `.Distinct(StringComparer.OrdinalIgnoreCase)` over the concatenation, which is **where overlapping targets are de-duplicated** — a file reachable from both `D:\` and `D:\sub` is counted once.
   Note: exclusions are **not applied** when `IncludeSubdirectories` is `false`.
3. **Collect + filter.** Per file: `ThrowIfCancellationRequested()`; skip if `GetFileSize < MinimumFileSize`; skip if `FileExtensions` is non-empty and the dot-trimmed extension is not in it (`OrdinalIgnoreCase`, so `a.TXT` matches `"txt"`); otherwise build a `ScannedFile` with `GetLastWriteTime`. `filesScanned` counts **kept** files only, and that is the value reported as `TotalFilesScanned`.
4. **Progress.** `progress.Invoke(filesScanned)` every 100 kept files, then one unconditional final `progress?.Invoke(filesScanned)` after the loop. 250 files produce `100, 200, 250`; 2 files produce just `2`.
5. **Grouping.** `MatchRegex` non-whitespace selects `GroupByNameRegex`; otherwise `GroupBySizeAndHash`.
6. **Finalize.** Stop the stopwatch, sort groups by `WastedBytes` descending, sum `Count` into `TotalDuplicates` and `WastedBytes` into `TotalWastedBytes`.

**`GroupBySizeAndHash` (default mode)**

Group by `FileSize`, keep groups of ≥ 2 — the cheap filter that means only same-size candidates are ever read. Then per size-group: `ThrowIfCancellationRequested()`, hash **every** file in the group (writing into `ScannedFile.Hash`), re-group by `Hash`, keep hash-groups of ≥ 2. Files inside a group are ordered by `FilePath` ascending. Same size + different content produces no group.

**`GroupByNameRegex` (regex mode)**

```csharp
regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
```

- Matches against `ScannedFile.FileName` only — never the path, never the size, **never the content**.
- Non-matching files are skipped entirely.
- The group key is built by `BuildRegexKey`: the concatenation of capture groups 1..n when the pattern has captures, else the full match value. Keys are bucketed in an `Ordinal` (case-sensitive) dictionary, so case sensitivity is the caller's job via an inline `(?i)`.
- Buckets smaller than 2 are dropped. A surviving group takes `FileSize = orderedFiles.Max(f => f.FileSize)` and `Hash = key`.
- The 1-second per-match timeout exists because the cancellation token is only checked *between* files, never inside `Match`. Without it a pattern like `(a+)+X` would hang the worker thread; with it the scan aborts with a descriptive `ArgumentException`.

> **Code/comment discrepancy — not covered by a test.** The comment above `BuildRegexKey` states captures are "joined with SOH (0x01) so ('ab','c') and ('a','bc') stay distinct", but the code is `string.Join("", parts)` — an empty separator. Those two capture tuples therefore collide. Either the comment or the code is wrong; nothing currently asserts which.

### `SettingsService`

```csharp
public SettingsService(IFileSystemService fileSystem, string settingsPath)

public AppSettings Load()
public void        Save(AppSettings settings)
```

The path is supplied by the caller. The application passes `%APPDATA%\WindowsFileManager\settings.json`, built in `MainViewModel.CreateDefaultSettings`.

**`Load()` — five behaviors, in order**

| Situation | Result |
|-----------|--------|
| File does not exist (`FileExists` false) | `CreateDefault()` — one `ProfileSettings { Name = "Default" }`, `ActiveProfileName = "Default"` |
| `ReadAllText` + `Deserialize<AppSettings>` throws `JsonException` | `CreateDefault()` |
| Payload is literal `null` | `new AppSettings()`, which then falls into migration |
| `settings.Profiles.Count == 0` | **legacy migration** — re-parse the raw JSON with `JsonDocument` and hand-map the old flat schema into one `"Default"` profile |
| `ActiveProfileName` empty, or names no existing profile (`OrdinalIgnoreCase`) | repaired to `Profiles[0].Name` |

**Legacy migration** (`MigrateLegacyProfile`) reads the old top-level property names into the new profile through five typed helpers, each of which returns the supplied fallback (the property's current default) when the token is absent or the wrong `ValueKind`:

| Helper | Reads | Skips |
|--------|-------|-------|
| `ReadStringList` | `TargetPaths`, `DisabledTargetPaths`, `ExcludeFolderNames`, `DisabledExcludeFolderNames`, `FolderSearchResultPaths`, `SelectedFolderSearchResultPaths` | non-string and null array elements |
| `ReadObjectList<T>` | `FilterRules`, `FolderSearchPatterns` | null elements; deserializes per item |
| `ReadBool` | `IncludeSubdirectories`, `IsMiniPreview`, `IsAutoPreview`, `IsAutoPlay` | anything not `True`/`False` |
| `ReadLong` | `MinimumFileSize` | non-number, or a value that overflows `Int64` |
| `ReadDouble` | `Volume` | non-number (so `"Volume": "not-a-number"` falls back to `0.5`) |
| `ReadString` | `SelectedSortOption`, `MoveTargetPath` | non-string |

A root that is not a JSON object (e.g. `[]`) returns the bare default profile. Any `JsonException` raised inside migration is swallowed, keeping whatever was populated so far.

**`Save(AppSettings)`**

```csharp
var directory = Path.GetDirectoryName(_settingsPath);
if (!string.IsNullOrEmpty(directory) && !_fileSystem.DirectoryExists(directory))
    _fileSystem.CreateDirectory(directory);

var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
_fileSystem.WriteAllText(_settingsPath, json);
```

A bare filename makes `GetDirectoryName` return `""`, in which case neither `DirectoryExists` nor `CreateDirectory` is called at all — pinned by `Save_BareFilename_ShouldNotAttemptCreateDirectory`.

## Rules & constraints

**Dependency**

1. **Application depends only on Core interfaces.** Never add a `ProjectReference` to Infrastructure, and never call `System.IO.File` / `Directory` directly. Doing either makes the affected code path untestable and immediately breaks the 100% coverage gate, because there is no way to mock a static.
2. **No package references.** The module compiles against the BCL and Core alone. A new dependency needs an ADR.
3. **No UI or WPF types.** Progress and cancellation are expressed with `Action<int>` and `CancellationToken`, never `IProgress<T>` marshalling or a dispatcher.

**Behavior invariants**

4. **The size pre-filter is what makes the scan affordable** — only files that collide on byte length are ever hashed. Removing it turns an O(colliding-files) read into an O(all-files) read.
5. **`TotalFilesScanned` counts kept files, not visited files.** Files rejected by `MinimumFileSize` or the extension filter are never counted or reported through `progress`.
6. **Regex mode ignores size and content entirely.** Two files with the same capture key are duplicates even if their bytes differ. That is intentional; do not "improve" it by adding a hash check without changing [SPEC-001](../specs/SPEC-001-duplicate-detection.md).
7. **Every user-supplied regex must carry a `MatchTimeout`.** Both regex construction sites in the codebase (here and `MainViewModel.MatchesFilter`) pass `TimeSpan.FromSeconds(1)`. No analyzer enforces this — it is a convention held by review. A regex without a timeout is a hang.
8. **Guards run before the stopwatch and before enumeration.** A scan that will fail on a missing directory must fail before it touches any other target path.

**Threading**

9. **Nothing here is thread-safe, and nothing here creates a thread.** `Scan` is designed to be called on a background thread by a single caller; two concurrent `Scan` calls sharing a `ScannedFile` list would race on `ScannedFile.Hash`. `SettingsService.Save` has no file lock — concurrent saves are last-writer-wins.
10. **Cancellation granularity is documented, not fine-grained.** The token is not observed inside `SHA256.HashData` for one file, nor inside a single `Regex.Match`. The regex case is mitigated by the 1-second match timeout; the hashing case is not mitigated, so a single huge file delays cancellation.

**Error handling**

11. **Fail loudly on a bad request, tolerantly on bad data.** `Scan` throws for an unusable request (no paths, missing directory, broken regex). `Load` never throws — it degrades through **two** levels of `JsonException` tolerance (whole-file parse → `CreateDefault()`, `SettingsService.cs:45`; legacy migration → keep whatever the profile already holds, `:125`) so a corrupt settings file can never prevent the app from starting. There is **no** per-element tolerance: `ReadObjectList` (`:153`) calls `JsonSerializer.Deserialize<T>` unguarded (`:162`), so a single malformed `FilterRules` / `FolderSearchPatterns` element throws out of `MigrateLegacyProfile` into the `:125` handler and every field not yet read is abandoned at its default — pinned by `SettingsServiceTests.Load_LegacyFilterRulesWithTypeMismatch_ShouldSwallowJsonException`.
12. **Only the two documented exception types are swallowed during enumeration** (`UnauthorizedAccessException`, `IOException`, in `EnumerateFilesExcluding`). Do not widen that to a bare `catch` — it would hide real bugs behind a silently short scan.

## Testing

Three test classes under `tests/WindowsFileManager.Tests/Services/`, all built on a `Mock<IFileSystemService>` (Moq 4.20.70) with FluentAssertions for the assertions. **No test touches a real disk.**

| Test class | Approach |
|------------|----------|
| `FileHashServiceTests` | `OpenRead` is stubbed to return a `MemoryStream` over known bytes. Asserts determinism, content sensitivity, and a non-empty hash for an empty file. |
| `DuplicateScannerServiceTests` | Stubs `DirectoryExists`, `EnumerateFiles`, `EnumerateDirectories`, `GetFileSize`, `GetFileName`, `GetLastWriteTime`, `OpenRead`. Covers both guard exceptions, both grouping modes, overlapping-path de-duplication, the extension filter both ways, the exclusion walker (with `Times.Never` on the excluded directory), the swallowed `UnauthorizedAccessException`/`IOException`, progress throttling at 100/200/250 and the single final call, group ordering by wasted space, a pre-cancelled token, the `TopDirectoryOnly` call shape, catastrophic-backtracking timeout via `(a+)+X`, and regex-mode case sensitivity. |
| `SettingsServiceTests` | Stubs `FileExists`/`ReadAllText`/`WriteAllText`/`DirectoryExists`/`CreateDirectory`. Covers missing file, malformed JSON, literal `null`, a non-object root, the full legacy flat-schema migration including the `MinimumFileSize` overflow and `"Volume": "not-a-number"` fallbacks, active-profile repair, the bare-filename directory guard, and a multi-profile round-trip including window geometry. |

**What is mocked:** `IFileSystemService`, always. **What is not mocked:** nothing else — the services have no other dependency, and `FileHashService` is passed to `DuplicateScannerService` as a real instance wrapping the same mock.

**Coverage:** `[WindowsFileManager.Application]*` is in the test project's `Include` list, so the 100% line/branch/method threshold applies to every method here. No type in Application is marked `[ExcludeFromCodeCoverage]`. A new `if`, `??`, or `catch` needs its test in the same commit or `dotnet test` fails.

## Links

- [`../../ARCHITECTURE.md`](../../ARCHITECTURE.md) — system map and layer boundaries
- [`../adr/`](../adr/) — ADR-003 (three-stage duplicate detection: size grouping, then SHA256, then confirmation), ADR-004 (all I/O behind `IFileSystemService`), ADR-005 (100% coverage enforced by coverlet.msbuild), ADR-007 (System.Text.Json settings, enum-ordinal stability)
- [core.md](core.md) — the models these services consume and produce
- [infrastructure.md](infrastructure.md) — the real `IFileSystemService` these services run against in production
- [ui.md](ui.md) — the composition root and how `Scan` is called
- [`../specs/SPEC-001-duplicate-detection.md`](../specs/SPEC-001-duplicate-detection.md) — the scan behavior contract
- [`../specs/SPEC-002-filtering-and-sorting.md`](../specs/SPEC-002-filtering-and-sorting.md) — size/extension filtering
- [`../specs/SPEC-009-settings-and-window-state-persistence.md`](../specs/SPEC-009-settings-and-window-state-persistence.md) — the settings contract
- [`../SECURITY.md`](../SECURITY.md) — regex ReDoS handling and the unrestricted content read
