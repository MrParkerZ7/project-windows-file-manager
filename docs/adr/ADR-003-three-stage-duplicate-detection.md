# ADR-003: Three-stage duplicate detection (size grouping, then SHA-256, then confirmation)

## Status

Accepted — 2026-04-04 (commit `7b59636`; `DuplicateScannerService.cs` added in the Clean Architecture
restructure, carrying the algorithm from the initial commit `57de160`)

## Context

The core operation is "find files with identical content across one or more folders". The naive
implementations are both unacceptable:

- Hash every file. On a folder tree of tens of thousands of files this reads every byte on disk, and the
  overwhelming majority of files have no size-mate at all and therefore cannot be duplicates.
- Compare every pair byte-for-byte. Quadratic in file count.

Two files with identical content necessarily have identical length, so file size is a free, perfectly
sound pre-filter — the metadata is already collected while enumerating.

## Decision

`DuplicateScannerService.GroupBySizeAndHash`
([`../../src/WindowsFileManager.Application/Services/DuplicateScannerService.cs`](../../src/WindowsFileManager.Application/Services/DuplicateScannerService.cs)
lines 137–170) runs three stages, in order:

**Stage 1 — size grouping (the cheap filter).** Group the collected `ScannedFile` list by `FileSize` and keep
only groups with more than one member (lines 139–141). Anything with a unique size is discarded without a
single content read. The class comment states the intent verbatim:

> `Algorithm: group by size (fast filter) -> hash only same-size files -> group by hash.`

**Stage 2 — SHA-256 content hash.** For each surviving size group, hash *every* member via
`FileHashService.ComputeHash`, which is `SHA256.HashData(stream)` over the whole stream, rendered as uppercase
hex by `Convert.ToHexString`
([`../../src/WindowsFileManager.Application/Services/FileHashService.cs`](../../src/WindowsFileManager.Application/Services/FileHashService.cs)
lines 27–32). `CancellationToken.ThrowIfCancellationRequested()` is checked once per size group, before the
hashing loop (line 147).

**Stage 3 — hash-equality confirmation.** Re-group the size group by `Hash` and keep only hash groups with
more than one member (lines 154–166). This is the confirmation step: it proves the size collision was not a
coincidence. Members are ordered by `FilePath` ascending, and each surviving group becomes a `DuplicateGroup`
carrying the hash, the shared size, and its files.

Results are then sorted by `WastedBytes` descending (line 125) so the biggest reclaim opportunity appears
first.

**Deliberate exception — regex mode.** When `ScanOptions.MatchRegex` is non-whitespace, `Scan` routes to
`GroupByNameRegex` instead (lines 118–120), which groups by a filename regex key and **never consults size or
content**. That is a separate contract, documented in
[`../specs/SPEC-001-duplicate-detection.md`](../specs/SPEC-001-duplicate-detection.md).

## Consequences

### Positive

- Content reads are bounded to size-collision candidates. On a typical tree the majority of files are never
  opened.
- Stage 1 costs nothing extra — `GetFileSize` was already called during collection to apply the
  minimum-size filter.
- SHA-256 rather than MD5 or SHA-1: an accidental collision between two non-identical files is not a
  practical concern, which is what makes stage 3 trustworthy without a byte comparison.
- Deterministic output. Group members are ordered by `FilePath`, groups by `WastedBytes` descending, so the
  same tree yields the same result and the same UI ordering every run.
- The whole algorithm is a pure function of `ScanOptions` and an `IFileSystemService`
  ([ADR-004](ADR-004-ifilesystemservice-io-abstraction.md)), so it is exercised entirely with mocks.

### Negative

- **Whole-file hashing, no partial pre-pass.** There is no head-block or head+tail pre-hash before the full
  read. Ten identical 4 GB videos means ten full 4 GB reads, even though the first mismatched block would have
  settled it for non-duplicates.
- **No byte-for-byte verification after hash equality.** The app groups, presents, and deletes on SHA-256
  equality alone. This is a defensible engineering trade, but it is a trade: "identical" in this application
  means "SHA-256-identical", not "verified byte-identical". Anyone reading the UI as a byte-level guarantee is
  reading more than the code delivers.
- **Cancellation granularity is coarse.** The token is checked once per size group, never inside
  `ComputeHash`. Cancelling mid-hash of a single very large file waits for that file to finish.
- **Hashing is sequential and single-threaded** inside the size-group loop. There is no parallel hashing and
  no I/O concurrency, so throughput is bounded by one stream at a time.
- **No hash cache.** Nothing is persisted between scans; re-scanning an unchanged tree re-reads every
  size-colliding file from scratch.
- **The regex mode shares the same destructive UI.** Groups produced by `GroupByNameRegex` have not been
  content-verified at all, yet they feed the same delete/move commands
  ([`../specs/SPEC-004-selection-and-file-actions.md`](../specs/SPEC-004-selection-and-file-actions.md)).
  The mode is opt-in and visually distinct, but the safety properties of stages 1–3 do not apply to it.
- Reading every candidate file's full content means the scanner touches file bodies in whatever directories
  the user pointed it at, with no size cap and no exclusion of sensitive locations — see
  [`../SECURITY.md`](../SECURITY.md).

### Neutral

- The **user-facing** confirmation — the `MessageBox` shown before any delete or move — is a separate,
  later gate in the UI layer, not part of this algorithm. Stage 3 here is the *hash-equality* confirmation.
- `DuplicateGroup.WastedBytes` has two branches: `sum(FileSize) − max(FileSize)` when the sum is positive, and
  a `FileSize × (Count − 1)` fallback when individual sizes are all zero. Both are pinned by tests.
- In regex mode a group's `FileSize` is the **maximum** of its members and `Hash` holds the regex key rather
  than a digest — the same `DuplicateGroup` shape carries different semantics per mode.
- `TotalFilesScanned` counts only files that survived the minimum-size and extension filters, not every file
  enumerated.

## Links

- [ADR-004](ADR-004-ifilesystemservice-io-abstraction.md) — the I/O seam the scanner runs on
- [ADR-001](ADR-001-clean-architecture-four-modules.md) — why the algorithm lives in Application
- [`../specs/SPEC-001-duplicate-detection.md`](../specs/SPEC-001-duplicate-detection.md) — the living behaviour contract
- [`../specs/SPEC-002-filtering-and-sorting.md`](../specs/SPEC-002-filtering-and-sorting.md) ·
  [`../specs/SPEC-004-selection-and-file-actions.md`](../specs/SPEC-004-selection-and-file-actions.md)
- [`../SECURITY.md`](../SECURITY.md) — content-read and regex trust boundaries
- [`../modules/`](../modules/) — Application module documentation
- Source: [`../../src/WindowsFileManager.Application/Services/DuplicateScannerService.cs`](../../src/WindowsFileManager.Application/Services/DuplicateScannerService.cs) ·
  [`../../src/WindowsFileManager.Application/Services/FileHashService.cs`](../../src/WindowsFileManager.Application/Services/FileHashService.cs)
