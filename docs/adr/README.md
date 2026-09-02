# Architecture Decision Records

Index of the load-bearing decisions behind this codebase — the *why* that the code itself cannot state.

## What an ADR is here

An ADR records one decision that is **hard to reverse and hard to infer from the code**. It exists so a
maintainer (human or agent) reading a surprising line does not "fix" a deliberate choice.

An ADR here is written **from evidence** — a commit, a config file, a CI workflow, or a dated entry in
[`../../CLAUDE.md`](../../CLAUDE.md) § Project Notes. Every claim in an ADR traces to one of those. A decision
with no recoverable evidence does not get an ADR.

What does **not** belong here:

| Question | Home |
|---|---|
| Why this way? | `docs/adr/` (this folder) — frozen at acceptance |
| How does this feature behave today? | [`../specs/`](../specs/) — living, updated with the code |
| How does this module work? | [`../modules/`](../modules/) — living, code-adjacent |
| What is the system shaped like? | [`../../ARCHITECTURE.md`](../../ARCHITECTURE.md) |
| What must I not do? | [`../SECURITY.md`](../SECURITY.md) |

## Numbering rule

- IDs are global and assigned in acceptance order: `ADR-001`, `ADR-002`, …
- **An ADR is never renumbered and never deleted.** The number is a stable citation key used by specs,
  module docs, and commit messages.
- Filename: `ADR-NNN-<kebab-slug>.md`. The slug may be corrected for clarity; the number may not.
- A decision that replaces an earlier one gets a **new number** and sets the old one to `Superseded by ADR-NNN`.
  The superseded ADR keeps its full text — it is the record of what was true then.

## Status vocabulary

| Status | Meaning |
|---|---|
| `Proposed` | Written down, not yet acted on in the code. No ADR in this repo currently holds this status. |
| `Accepted` | The decision is live in the codebase. The `Status` line carries the date and the commit that made it live. |
| `Superseded by ADR-NNN` | Replaced. Text retained; the successor ADR explains the change. |

Where the acceptance date is recoverable from `git log` or a dated `CLAUDE.md` Project Note, the `Status`
line states it and cites the commit. Where it is not, the line reads `Accepted (date not recorded)`.

## The records

| ID | Title | Status | Date | Evidence |
|---|---|---|---|---|
| [ADR-001](ADR-001-clean-architecture-four-modules.md) | Clean Architecture with four modules (Core / Application / Infrastructure / UI) | Accepted | 2026-04-04 | commit `7b59636` |
| [ADR-002](ADR-002-hand-rolled-mvvm.md) | Hand-rolled MVVM (`ViewModelBase` + `RelayCommand`) instead of an MVVM framework | Accepted | 2026-04-04 | commit `57de160` |
| [ADR-003](ADR-003-three-stage-duplicate-detection.md) | Three-stage duplicate detection — size grouping, then SHA-256, then hash-equality confirmation | Accepted | 2026-04-04 | commit `7b59636` |
| [ADR-004](ADR-004-ifilesystemservice-io-abstraction.md) | All I/O behind `IFileSystemService`, with Infrastructure excluded from coverage | Accepted | 2026-04-04 | commit `7b59636` |
| [ADR-005](ADR-005-coverage-enforcement-coverlet-msbuild.md) | 100% coverage enforced by `coverlet.msbuild` in the test csproj (moved off `coverlet.runsettings`) | Superseded by [ADR-011](ADR-011-coverage-via-collector-and-script.md) | 2026-04-16 | commit `125a7b1`; CLAUDE.md `[2026-04-16]` |
| [ADR-006](ADR-006-persist-settings-on-every-mutation.md) | Persist settings on every mutation rather than on window close | Accepted | 2026-04-15 | commit `fab9c91`; CLAUDE.md `[2026-04-15]` |
| [ADR-007](ADR-007-system-text-json-settings-compatibility.md) | `System.Text.Json` settings with enum-ordinal stability and `[JsonIgnore]` on computed properties | Accepted | 2026-04-15 | commits `2a9177c` · `cc02c3b` · `fab9c91`; CLAUDE.md `[2026-04-15]` |
| [ADR-008](ADR-008-msix-packaging-anycpu-store.md) | MSIX packaging on AnyCPU targeting the Microsoft Store | Accepted | 2026-04-04 | commit `9d82e5a` |
| [ADR-009](ADR-009-treat-warnings-as-errors.md) | `TreatWarningsAsErrors` with StyleCop and .NET analyzers as build gates | Accepted | 2026-04-16 | commit `125a7b1`; CLAUDE.md `[2026-04-16]` |
| [ADR-010](ADR-010-wpf-net8-desktop-shell.md) | WPF on .NET 8 for the desktop shell (and why `dotnet watch` is not usable) | Accepted | 2026-04-04 | commit `57de160`; CLAUDE.md `[2026-04-14]` |
| [ADR-011](ADR-011-coverage-via-collector-and-script.md) | Measure coverage with `coverlet.collector`, enforce the threshold with a script | Accepted | 2026-09-02 | CLAUDE.md `[2026-09-02]`; [`../../scripts/Check-Coverage.ps1`](../../scripts/Check-Coverage.ps1) |

## Template

New ADRs use the four-section shape every record here follows:

```markdown
# ADR-{NNN}: {Title}

## Status
Accepted — {YYYY-MM-DD} (commit `{sha}`)

## Context
{What problem motivated this decision? Cite the evidence.}

## Decision
{What was chosen, stated concretely against the code that implements it.}

## Consequences

### Positive
- {Benefit}

### Negative
- {Real, specific cost — including what it costs contributors}

### Neutral
- {Observation that is neither win nor loss}

## Links
- {Relative links to related ADRs, specs, module docs, and source}
```
