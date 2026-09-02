# ADR-009: `TreatWarningsAsErrors` with StyleCop and .NET analyzers as build gates

## Status

Accepted — 2026-04-16 (commit `125a7b1`). Analyzers were present as *warnings* from the initial commit
`57de160`, 2026-04-04.

## Context

`Directory.Build.props` shipped in the initial commit already enabling the analysis stack, but with the
enforcement switches off (`git show 57de160:Directory.Build.props`):

```xml
<EnableNETAnalyzers>true</EnableNETAnalyzers>
<AnalysisLevel>latest</AnalysisLevel>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
<TreatWarningsAsErrors>false</TreatWarningsAsErrors>
<CodeAnalysisTreatWarningsAsErrors>false</CodeAnalysisTreatWarningsAsErrors>
```

Warnings therefore accumulated and were cleared in batches — commit `1cc516c` (2026-04-06) is literally
*"fix: resolve StyleCop warnings and add regex timeout for ReDoS prevention"*, and `b4feb08` (2026-04-04) is
*"Fix StyleCop warnings: add XML param docs, fix parameter line placement"*. Cleaning up in batches means the
warning count is only ever zero immediately after a cleanup commit.

On 2026-04-16 the quality-gate commit flipped both switches. Its body: *"Enable TreatWarningsAsErrors in
Directory.Build.props."* The dated Project Note in [`../../CLAUDE.md`](../../CLAUDE.md) records both the
change and its first concrete cost:

> **[2026-04-16]** `TreatWarningsAsErrors` enabled — all StyleCop/Roslyn warnings are now build errors.
> Inline lambdas without braces trigger SA1503.

## Decision

Enforce analyzers at build time, repo-wide.

[`../../Directory.Build.props`](../../Directory.Build.props) — applies to **all five projects, including the
test project**, because it sits at the repository root:

```xml
<EnableNETAnalyzers>true</EnableNETAnalyzers>
<AnalysisLevel>latest</AnalysisLevel>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<CodeAnalysisTreatWarningsAsErrors>true</CodeAnalysisTreatWarningsAsErrors>
```

plus `StyleCop.Analyzers` 1.1.118 (`PrivateAssets=all`) and `stylecop.json` as an `AdditionalFiles` entry.

[`../../stylecop.json`](../../stylecop.json) turns on `documentInterfaces` and `documentExposedElements` —
public and interface members must carry XML documentation — plus `systemUsingDirectivesFirst`,
`usingDirectivesPlacement: outsideNamespace`, and `newlineAtEndOfFile: require`.

[`../../.editorconfig`](../../.editorconfig) adds naming rules (`I`-prefixed interfaces and PascalCase types
at `warning`, `_camelCase` private fields at `suggestion`), `csharp_style_namespace_declarations =
file_scoped:warning`, `csharp_prefer_braces = true:warning`, and **14 explicit StyleCop suppressions**, each
with a rationale comment (lines 78–91): `SA1101`, `SA1309`, `SA1633`, `SA1200`, `SA1202`, `SA1600`, `SA1601`,
`SA1602`, `SA1000`, `SA1204`, `SA0001`, `SA1118`, `SA1008`, `SA1009`.

CI relies on `Directory.Build.props` rather than re-asserting the flag — its build step is plain
`dotnet build -c Release --no-restore` ([`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml)
line 35). It did once pass `/p:TreatWarningsAsErrors=true` explicitly, but as a *global* property that made
the build step a different MSBuild build than the test step; it was removed as redundant on 2026-09-02 while
ruling out causes of the coverage flake ([ADR-011](ADR-011-coverage-via-collector-and-script.md) § Context),
and the workflow carries a comment saying the removal was a cleanup, not a fix. CI does add a separate
formatting gate (`dotnet format --verify-no-changes --no-restore`, lines 25–26).

## Consequences

### Positive

- Warning debt cannot accumulate. There is no "clean up the warnings" commit to schedule, because the count is
  structurally pinned at zero.
- Public API documentation is present in practice, though **not enforced** — `IFileSystemService`,
  `ViewModelBase`, `RelayCommand`, and every Core model carry XML docs, which is the difference between a
  readable and an unreadable public surface for both humans and agents. It holds as a convention, not as a
  gate: the suppression list above sets `SA1600`, `SA1601`, and `SA1602` to `none`
  ([`../../.editorconfig`](../../.editorconfig) lines 83–85), so an undocumented public member compiles
  clean.
- Style is uniform without spending review attention on it. Reviewers argue about design, not brace placement.
- Local build and CI build enforce the same bar for this gate, so failures are reproducible offline.
- The gate applies to test code too, so the test project does not become a style-free zone.

### Negative

These are what the gate costs a contributor:

- **Every warning is a hard stop, including irrelevant ones.** A warning in a file you did not touch blocks
  your build. Because `AnalysisLevel` is `latest`, an SDK or analyzer upgrade can break the build with **zero
  code changes** — the ruleset moves under you.
- **StyleCop 1.1.118 pre-dates modern C#, and it shows.** Four of the 14 suppressions exist because the
  analyzer is wrong, not the code: `SA1000` (*"new() target-typed — spacing rule outdated"*), `SA1008` and
  `SA1009` (*"false positive on C# range operator"*), and `SA0001`. Pinning an old analyzer to a `latest`
  analysis level guarantees more of these over time.
- **The cost named in the Project Note is real and recurring:** SA1503 means brace-less one-liners are not
  writable, including inline lambdas — `x => { return y; }` where `x => y` would read better in context.
- **Test code carries the full production style and documentation burden.** SA1118 is suppressed specifically
  *"(test data)"* — an exception carved for test readability, which is evidence that the uniform bar does not
  fit both kinds of code equally.
- **The escape valve is easier than the fix.** Adding a line to `.editorconfig` silences a rule repo-wide, and
  nothing distinguishes a justified suppression from a lazy one except the comment convention. The suppression
  list grows monotonically; nothing prunes it.
- **`dotnet format` is only enforced in CI.** There is no pre-commit hook, no lefthook, no husky — so a
  formatting failure is discovered after push, on a runner, rather than before commit.

### Neutral

- 14 suppressions is the documented, deliberate compromise between StyleCop's defaults and this codebase's
  style; each carries a rationale comment in `.editorconfig`.
- Naming rules sit at `warning` (interfaces, types) and `suggestion` (private fields) — so the `_camelCase`
  field convention is advisory, not enforced, while the `I` prefix is enforced.
- **Neither** workflow's build step passes `/p:TreatWarningsAsErrors=true` — both are plain
  `dotnet build -c Release --no-restore`
  ([`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) line 35 ·
  [`../../.github/workflows/msix-pipeline.yml`](../../.github/workflows/msix-pipeline.yml) line 71).
  `Directory.Build.props` supplies the flag to both, so the policy now holds by a single route rather than two.
- `StyleCop.Analyzers` is `PrivateAssets=all`, so it does not flow to consumers of any produced package.

## Links

- [ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md) — the sibling gate added in the same commit
- [ADR-002](ADR-002-hand-rolled-mvvm.md) — why no source generator needed a carve-out here
- [ADR-001](ADR-001-clean-architecture-four-modules.md) — the projects this policy applies to
- [`../DEV.md`](../DEV.md) — running the format and build gates locally
- [`../SECURITY.md`](../SECURITY.md) — the CI gates that sit alongside these
- Source: [`../../Directory.Build.props`](../../Directory.Build.props) ·
  [`../../stylecop.json`](../../stylecop.json) ·
  [`../../.editorconfig`](../../.editorconfig) ·
  [`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml)
