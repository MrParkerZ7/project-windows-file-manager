# ADR-011: Measure coverage with coverlet.collector, enforce the threshold with a script

## Status

Accepted — 2026-09-02. **Supersedes [ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md).**

## Context

[ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md) moved coverage enforcement into
`coverlet.msbuild` in the test `.csproj`, so that a local `dotnet test` and a CI `dotnet test`
enforced the same 100% line/branch/method bar. That worked, and it drove coverage from ~44% to
100%.

It also raced.

`coverlet.msbuild` instruments assemblies **during the build**. Intermittently one or more modules
came out uninstrumented and reported **0%** while all 217 tests passed, dragging the total below
100% and failing the build. Two signatures recurred:

| Signature | Total | Modules at 0% |
|---|---|---|
| single-module | 55.72% line | `WindowsFileManager.Application` |
| two-module | 9.5% line | `WindowsFileManager.Application` **and** `WindowsFileManager.Core` |

The failure is never a test failure — the suite is green in every occurrence. It is purely lost
instrumentation.

### What was measured

The gate turned `main` red on docs-only commits, which is what prompted the investigation.

- **It is a flake, not a commit defect.** Commit `da50cf6` failed and then passed on a bare re-run
  with zero changes.
- **Base rate, uncontended and from a properly clean tree: ~20%** (2 failures in 10 runs at 6.0.2).
  Under machine load it rose to ~36-40%.
- **No version fixes it.** Measured across three versions, clean tree, 6+ trials each:

  | Version | Result |
  |---|---|
  | 6.0.2 (in use) | 8 pass / 2 fail of 10 |
  | 6.0.4 | 4 pass / 2 fail of 6 |
  | 10.0.1 (latest) | 7 pass / 3 fail of 10 |

  A parallel trial of 10.0.1 initially returned 6/6 and looked like a fix; a larger sequential
  series reproduced the failure three times. The bug spans four years of releases.
- **Ruled out by experiment:** `--no-build`; the redundant `/p:TreatWarningsAsErrors=true` global
  property; MSBuild parallelism (`-m:1`); build-then-test sequencing (`dotnet test` alone is
  *worse*); any TFM or double-build mismatch. Stale `obj/`/`bin` makes it markedly more likely but
  is not necessary — clean trees still fail.

A gate that fails ~1 run in 5 for reasons unrelated to the code stops being a gate. Its failures get
re-run reflexively, which is precisely how a real regression would slip through.

## Decision

**Separate measurement from enforcement.**

1. **Measure with `coverlet.collector`** via `dotnet test --collect:"XPlat Code Coverage"`. The data
   collector instruments **at runtime**, in-process, so there is no build step for it to race.
2. **Enforce with [`scripts/Check-Coverage.ps1`](../../scripts/Check-Coverage.ps1)**, which reads the
   Cobertura report and fails below 100% line, branch and method. The collector cannot enforce a
   threshold; this restores the bar ADR-005 established, unchanged.
3. **`tests/WindowsFileManager.Tests/coverlet.runsettings` becomes the single source of coverage
   scope.** It was previously inert *and* wrong — its `Include` omitted `ViewModels`, describing a
   narrower scope than the csproj enforced. Both filters now match what was actually enforced.
4. **`coverlet.msbuild` is removed** from the test project, along with its `CollectCoverage` /
   `Threshold` / `ThresholdType` property block.

### Method coverage

Cobertura carries `line-rate` and `branch-rate` but has no method rate. The script derives it: a
method counts as covered when it has at least one covered line — what coverlet's own method
threshold measured. Verified to report the same 100/100/100 across the same three modules that
`coverlet.msbuild` reported.

## Consequences

### Positive

- **The gate is stable.** 20 consecutive passes of the exact CI sequence, against a ~20% base
  failure rate — p ≈ 1.2% by chance. Unlike a version bump this is principled: runtime
  instrumentation has no build to race.
- **A red gate means something again.** Re-running a failure is no longer the reflex.
- **`--no-build` is safe again**, so the test step reuses the Build gate's output instead of
  rebuilding.
- **The failure mode is legible.** The script prints a per-module table and, on failure, says that a
  module at exactly 0% means lost instrumentation rather than missing tests.
- **Scope is declared in one place** that is actually read, and it no longer under-reports.

### Negative

- **Enforcement is now project-specific code** — a 118-line PowerShell script that must be
  maintained, rather than a package feature. It is covered by no test of its own; it was verified by
  negative tests (a raised threshold and a missing report both exit 1) run by hand.
- **Local `dotnet test` no longer fails on low coverage by itself.** The check is a second command.
  `dotnet test` + `./scripts/Check-Coverage.ps1` is now the full local gate, and a contributor who
  runs only the first sees a green suite at 90% coverage.
- **PowerShell is now a hard dependency of the quality gate**, on a Windows-only WPF project where
  that is a small cost but is no longer zero.
- **Two coverage packages existed briefly.** Anyone with a stale branch may still reference
  `coverlet.msbuild`; its properties are silently ignored once the package is gone.

### Neutral

- The Cobertura output moves from `tests/**/coverage/` to `tests/**/TestResults/<guid>/`, the
  collector's location. The CI artifact glob follows it.
- The 100% bar, the `Include`/`Exclude` filters, and the `[ExcludeFromCodeCoverage]` escape hatch
  are all unchanged. This ADR changes the *mechanism*, not the *policy*.

## Links

- Supersedes [ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md) — the original in-build gate
- [`scripts/Check-Coverage.ps1`](../../scripts/Check-Coverage.ps1) — the enforcement
- [`../DEV.md`](../DEV.md) — the contributor-facing test loop
- [ADR-009](ADR-009-treat-warnings-as-errors.md) — the other build-time quality gate
