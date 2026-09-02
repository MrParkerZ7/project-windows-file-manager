# ADR-005: 100% coverage enforced by `coverlet.msbuild` in the test csproj (moved off `coverlet.runsettings`)

## Status

Accepted — 2026-04-16 (commit `125a7b1` "feat: add quality gates, Exclude/Mismatch/Contains match types,
column sorting, and tab panel switching")

## Context

From the Clean Architecture restructure (`7b59636`, 2026-04-04) the test project referenced
`coverlet.collector` and shipped `tests/WindowsFileManager.Tests/coverlet.runsettings`, consumed via
`dotnet test --collect:"XPlat Code Coverage" --settings …`. That file declares `<ThresholdType>line</ThresholdType>`
and `<ThresholdStat>total</ThresholdStat>` but **no `<Threshold>` value at all**
([`../../tests/WindowsFileManager.Tests/coverlet.runsettings`](../../tests/WindowsFileManager.Tests/coverlet.runsettings)
lines 11–12). It therefore enforced nothing: coverage was collected and uploaded as an artifact, and a
regression could not fail the build.

The dated Project Note records the decision and the starting point honestly
([`../../CLAUDE.md`](../../CLAUDE.md) § Project Notes):

> **[2026-04-16]** Coverage enforcement moved from `coverlet.runsettings` (XPlat Code Coverage) to
> `coverlet.msbuild` in test `.csproj` for threshold enforcement. Current coverage ~44% — needs
> `automate-test` to reach 100%.

The `125a7b1` commit body states the same intent: *"Add coverlet.msbuild with 100% line/branch/method
threshold enforcement."*

## Decision

Move enforcement into MSBuild properties on the test project itself, so **any** `dotnet test` enforces it
without a flag ([`../../tests/WindowsFileManager.Tests/WindowsFileManager.Tests.csproj`](../../tests/WindowsFileManager.Tests/WindowsFileManager.Tests.csproj)
lines 11–27):

```xml
<CollectCoverage>true</CollectCoverage>
<Threshold>100</Threshold>
<ThresholdType>line,branch,method</ThresholdType>
<ThresholdStat>total</ThresholdStat>
<SkipAutoProps>true</SkipAutoProps>
<IncludeTestAssembly>false</IncludeTestAssembly>
<Include>[WindowsFileManager.Core]*,[WindowsFileManager.Application]*,[WindowsFileManager]WindowsFileManager.Helpers*,[WindowsFileManager]WindowsFileManager.ViewModels*</Include>
<Exclude>[WindowsFileManager]*Views*,[WindowsFileManager]*Helpers.Win32Api*,[WindowsFileManager]XamlGeneratedNamespace*</Exclude>
<ExcludeByFile>**/AssemblyInfo.cs,**/App.xaml.cs,**/*.g.cs,**/*.g.i.cs</ExcludeByFile>
<ExcludeByAttribute>GeneratedCodeAttribute,CompilerGeneratedAttribute,ExcludeFromCodeCoverageAttribute</ExcludeByAttribute>
```

Consequences of that placement: `dotnet test` enforces the threshold; `dotnet test -p:CollectCoverage=false`
is the documented local opt-out ([`../../CLAUDE.md`](../../CLAUDE.md) Quick Reference).

`coverlet.collector` was removed from the test project two days later in commit `cce00c6` (2026-04-18,
"test: restore 100% coverage — 90 new tests + coverlet config fix"), leaving `coverlet.msbuild` 6.0.2 as the
only coverage package.

**Measured state as of 2026-08-30:** 217 tests, 0 failed, 0 skipped; 100% line / 100% branch / 100% method
across `WindowsFileManager.Core`, `WindowsFileManager.Application`, and the UI's ViewModels + Helpers.
Infrastructure is out of scope by design ([ADR-004](ADR-004-ifilesystemservice-io-abstraction.md)). The
`~44%` figure quoted in the Project Note above is historical; [`../../CLAUDE.md`](../../CLAUDE.md) carries a
dated 2026-08-30 note recording it as superseded.

## Consequences

### Positive

- The gate cannot be forgotten or mis-invoked. It travels with the project file, not with a CLI flag, so a
  local `dotnet test` and a CI `dotnet test` enforce the same bar.
- It covers **line, branch, and method** — not just lines — so an untested `else` or an unreferenced public
  method fails the build.
- It worked as intended: the ~44% starting point was driven to 100% and has stayed there.
- `SkipAutoProps=true` and `IncludeTestAssembly=false` keep the denominator honest — trivial auto-property
  accessors and the test assembly itself do not inflate the percentage.

### Negative

These are what the gate costs a contributor:

- **Every new branch needs a test before the suite will pass locally.** A one-line guard clause is a test. A
  defensive `catch` you cannot provoke is a blocked commit until the seam exists to provoke it. This is the
  single largest tax on small changes in this repo.
- **The escape hatch is one attribute, and it is already heavily used.**
  `ExcludeFromCodeCoverageAttribute` is in `ExcludeByAttribute`, and `MainViewModel` — roughly 5,000 lines and
  the most behaviour-dense file in the application — carries it (`MainViewModel.cs:25`), as do `ToggleItem`,
  `ExtensionFilter`, `MainWindow`, `ProfileNameDialog`, and the interop helpers. **"100% coverage" means 100%
  of what is in scope**, and the riskiest code in the app is not in scope. A contributor can silence the gate
  on real logic with a single attribute and nothing will flag it.
- **`--no-build` silently defeats the gate — and did, on 2026-08-30.** coverlet.msbuild instruments the
  referenced assemblies *during the build*. Both workflows ran `dotnet test -c Release --no-build`, so those
  targets never executed and Core/Application reported **0%** while all 217 tests passed — tripping the 100%
  threshold and turning `main` red on a docs-only commit. Reproduced locally with the identical command.
  Fixed the same day by dropping `--no-build` from both workflows; the flag must not be reintroduced, and both
  files now carry a comment saying so.
- **A data collector was requested that this project does not ship.** Both workflows also passed
  `--collect:"XPlat Code Coverage" --settings tests/WindowsFileManager.Tests/coverlet.runsettings`, but
  `coverlet.collector` — the package that provides that collector and consumes runsettings — is not
  referenced, so every run logged *"Could not find data collector 'XPlat Code Coverage'"*. Removed 2026-08-30.
  The CI artifact path was `tests/**/TestResults/**/coverage.cobertura.xml`, the collector's output location;
  it is now `tests/**/coverage/coverage.cobertura.xml`, where coverlet.msbuild actually writes.
- **The gate is non-deterministic, and the rate is high.** One or more modules intermittently report
  0% while all 217 tests pass, failing the 100% threshold. Characterised 2026-09-02:
  - **Reproducible locally**, roughly 4 failures in 10 clean-tree runs. Signatures seen: 55.72% total
    (`Application` at 0%) and 9.5% total (`Application` **and** `Core` at 0%).
  - **Proven a flake, not a commit defect** — commit `da50cf6` failed and then passed on a bare re-run
    with zero changes.
  - **Ruled out by experiment:** `--no-build` (fixed separately, and the flake outlives it); the
    redundant `/p:TreatWarningsAsErrors=true` global property on the Build step (removed; flake
    persists); MSBuild parallelism (`-m:1`; flake persists); build-then-test sequencing (`dotnet test`
    alone with no prior build is flakier still); a TFM or double-build mismatch (all projects resolve
    to a single output path, one `Application.dll` in the test output).
  - **Conclusion: an instrumentation race inside `coverlet.msbuild` 6.0.2 itself**, not in how this
    repo invokes it. Nothing in the workflow files can reliably fix it.
  - **The escape is `coverlet.collector`** — the `--collect:"XPlat Code Coverage"` path instruments at
    runtime through the data collector instead of rewriting assemblies at build time, so there is no
    build race. The cost is that the collector does **not** enforce thresholds, so the 100% gate would
    have to be re-implemented as a separate step reading `coverage.cobertura.xml`'s `line-rate` /
    `branch-rate`. That is a deliberate design change, not a config tweak — it is the open decision
    this ADR now carries.
  - Until then a red run is **not** evidence of a coverage regression. Re-run before investigating.
- **`coverlet.runsettings` remains on disk and is still inert.** Nothing consumes it, and its `Include` list
  omits `ViewModels`, so it describes a narrower scope than the one actually enforced. Anyone editing it to
  change the gate will change nothing. It is a deletion candidate, not a control surface.
- **Weakening the gate is a one-line csproj edit** — lowering `Threshold`, dropping `branch` from
  `ThresholdType`, or wiring `-p:CollectCoverage=false` into CI. Nothing structural raises an alarm; the
  runsettings file would silently *not* compensate.
- Running the full suite is slower than running tests alone, so the fast local loop is
  `-p:CollectCoverage=false` — which means the gate is usually only felt at the end.

### Neutral

- `[WindowsFileManager]*Helpers.Win32Api*` in the `Exclude` list is **stale** — no `Win32Api` type exists in
  the tree. Harmless, but it misdescribes the exclusion set.
- Coverage output is written to `./coverage/coverage.cobertura.xml` relative to the test project by
  `CoverletOutput` (csproj line 14).
- The threshold statistic is `total`, not `minimum` — an individual file may sit below 100% only if another
  compensates, which at a 100% target is a distinction without a difference.
- The [`../../CHANGELOG.md`](../../CHANGELOG.md) v1.0.0 entry claims "100% test coverage on Core and
  Application layers"; that release predates this gate, which was added the following day.

## Links

- [ADR-004](ADR-004-ifilesystemservice-io-abstraction.md) — why Infrastructure is out of scope
- [ADR-009](ADR-009-treat-warnings-as-errors.md) — the sibling build gate added in the same commit
- [ADR-002](ADR-002-hand-rolled-mvvm.md) — why `MainViewModel` is large enough to be excluded
- [`../DEV.md`](../DEV.md) — how to run the suite with and without the threshold
- Source: [`../../tests/WindowsFileManager.Tests/WindowsFileManager.Tests.csproj`](../../tests/WindowsFileManager.Tests/WindowsFileManager.Tests.csproj) ·
  [`../../tests/WindowsFileManager.Tests/coverlet.runsettings`](../../tests/WindowsFileManager.Tests/coverlet.runsettings) ·
  [`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml)
