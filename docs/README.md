# Documentation portal

**Folder File Control** (solution name `WindowsFileManager`) is a .NET 8 WPF desktop application for Windows that finds byte-identical duplicate files by content hash, searches and reshapes folder trees, and keeps an undo history of everything destructive it did. The code is a four-project Clean Architecture solution — `Core`, `Application`, `Infrastructure`, and the WPF shell — with 217 tests and 100% line / branch / method coverage on Core, Application, and the UI's ViewModels + Helpers (measured 2026-08-30; Infrastructure is deliberately excluded). This documentation set is split so that **each fact has exactly one home**: root files carry the system-level view, `docs/` carries the domain and operational background, and the three subdirectories carry per-feature contracts (`specs/`), per-decision rationale (`adr/`), and per-module mechanics (`modules/`). Nothing below is aspirational — every file listed here exists on disk.

---

## Start here

Read in this order. Most tasks stop after step 3.

| # | Read | Why |
|---|---|---|
| 1 | [`../CLAUDE.md`](../CLAUDE.md) | Build / format / test commands, the SDK-not-on-PATH gotcha, conventions, and dated project notes. Always first. |
| 2 | [`../README.md`](../README.md) | What the app does — the feature tour, tech stack, and quick start. |
| 3 | [`../ARCHITECTURE.md`](../ARCHITECTURE.md) | Module map, dependency rules, the layered flow of a scan, patterns in use, build outputs. Read before any change that crosses a project boundary. |
| 4 | [`CONTEXT.md`](CONTEXT.md) | Why the product exists, the domain primer, and the key user flows. Read when the task touches domain or business logic. |
| 5 | the relevant [`specs/SPEC-NNN`](specs/_index.md) | The current-truth behavior contract for the feature you are about to change. This is the file your change must keep in sync. |

Then, as the task demands:

- Touching filesystem operations, deletion, regex, settings, or CI → [`SECURITY.md`](SECURITY.md) **before** writing code.
- Setting up, building, testing, or packaging locally → [`DEV.md`](DEV.md).
- Hitting an unfamiliar term (`Folder pattern`, `Mismatch`, `Action history`) → [`GLOSSARY.md`](GLOSSARY.md).
- Working inside one project → its [`modules/`](modules/_index.md) doc.
- Wondering why something is built the way it is → [`adr/`](adr/README.md).

---

## The four documentation kinds

Every document here is exactly one of four kinds. The kind determines the home, the lifetime, and the question it answers. **Do not duplicate content across homes** — link instead. The recurring dividing line: a **spec** describes feature behavior *across* modules; a **module doc** describes one module's mechanics.

| Kind | Home | Lifetime | Answers |
|------|------|----------|---------|
| Feature spec | [`specs/`](specs/_index.md) | Living — updated in the same commit that changes the behavior | How does this feature behave **today**? |
| Decision | [`adr/`](adr/README.md) | Frozen at acceptance; superseded, never rewritten | **Why** was it built this way? |
| Module doc | [`modules/`](modules/_index.md) | Living, code-adjacent — moves when the code moves | How does **this code** work, inside one module? |
| Background | [`CONTEXT.md`](CONTEXT.md) (+ [`GLOSSARY.md`](GLOSSARY.md)) | Living, slow-changing | What is this **for**, for whom, and what do the terms mean? |

The standard this repo follows also defines a fifth home, `docs/design/` — frozen design-time intent that seeds a spec's first version. **This repo has no `docs/design/`**: the features were built before the spec layer was reverse-engineered from the code, so there is no design-time snapshot to preserve. Specs here were seeded from the source tree and git history, not from designs.

---

## Root documents

| File | What it is |
|------|-----------|
| [`../CLAUDE.md`](../CLAUDE.md) | Agent instructions: quick-reference command block, project structure, context loading order, key conventions, module reference, and dated `Project Notes`. The single agent-instruction file — there is no `AGENTS.md` or `.cursorrules`. |
| [`../README.md`](../README.md) | Product overview: the three tabs (Folder / Duplication / History), the feature tour, tech stack, and quick start. |
| [`../ARCHITECTURE.md`](../ARCHITECTURE.md) | Structural reference: module map, dependency rule, layered request flow, patterns in use, build outputs, cross-cutting concerns, and a `Known deviations` section that names doc-vs-code contradictions instead of smoothing them over. |
| [`../CHANGELOG.md`](../CHANGELOG.md) | Keep a Changelog / SemVer release history. `1.0.0` (2026-04-15) plus an `Unreleased` section — several documented features ship only in `Unreleased`, which the specs' `Ships in` lines track individually. |

---

## `docs/` — domain and operational background

| File | What it is |
|------|-----------|
| [`CONTEXT.md`](CONTEXT.md) | Problem and goal, domain primer, key user flows, the historical decision timeline, and explicit constraints / non-goals. The whole-project narrative that per-decision ADRs do not give. |
| [`GLOSSARY.md`](GLOSSARY.md) | Alphabetical domain terms and coined names, each mapped to the type and file that implements it. Terms marked **(coined)** are this product's own vocabulary. |
| [`SECURITY.md`](SECURITY.md) | Mandatory guardrails for code that deletes or moves the user's files, writes the settings document, compiles user regex, or touches shell/COM interop. Every protection is marked **PRESENT / ABSENT / PARTIAL** so an absent one is never mistaken for a real defence. |
| [`DEV.md`](DEV.md) | Clean clone → running app → green suite → signed MSIX. Prerequisites, the portable-SDK PATH step, build/test/coverage commands, and the two-step MSIX publish-then-pack path. |
| `README.md` | This portal. Navigation and the sync contract; holds no content of its own. |

---

## `docs/adr/` — decisions

Frozen at acceptance. IDs are global, assigned in acceptance order, and **never renumbered or deleted** — an `ADR-NNN` is a stable citation key. A replacement decision gets a new number and marks the old one superseded.

| File | Decision | Accepted |
|------|----------|----------|
| [`adr/README.md`](adr/README.md) | Index, numbering rule, status vocabulary, the records table, and the ADR template. | — |
| [`adr/ADR-001-clean-architecture-four-modules.md`](adr/ADR-001-clean-architecture-four-modules.md) | Clean Architecture with four modules (Core / Application / Infrastructure / UI). | 2026-04-04 |
| [`adr/ADR-002-hand-rolled-mvvm.md`](adr/ADR-002-hand-rolled-mvvm.md) | Hand-rolled MVVM (`ViewModelBase` + `RelayCommand`) instead of an MVVM framework. | 2026-04-04 |
| [`adr/ADR-003-three-stage-duplicate-detection.md`](adr/ADR-003-three-stage-duplicate-detection.md) | Three-stage duplicate detection — size grouping, then SHA-256, then hash-equality confirmation. | 2026-04-04 |
| [`adr/ADR-004-ifilesystemservice-io-abstraction.md`](adr/ADR-004-ifilesystemservice-io-abstraction.md) | All I/O behind `IFileSystemService`, with Infrastructure excluded from coverage. | 2026-04-04 |
| [`adr/ADR-005-coverage-enforcement-coverlet-msbuild.md`](adr/ADR-005-coverage-enforcement-coverlet-msbuild.md) | 100% coverage enforced by `coverlet.msbuild` in the test csproj (moved off `coverlet.runsettings`). | 2026-04-16 |
| [`adr/ADR-006-persist-settings-on-every-mutation.md`](adr/ADR-006-persist-settings-on-every-mutation.md) | Persist settings on every mutation rather than on window close. | 2026-04-15 |
| [`adr/ADR-007-system-text-json-settings-compatibility.md`](adr/ADR-007-system-text-json-settings-compatibility.md) | `System.Text.Json` settings with enum-ordinal stability and `[JsonIgnore]` on computed properties. | 2026-04-15 |
| [`adr/ADR-008-msix-packaging-anycpu-store.md`](adr/ADR-008-msix-packaging-anycpu-store.md) | MSIX packaging on AnyCPU targeting the Microsoft Store. | 2026-04-04 |
| [`adr/ADR-009-treat-warnings-as-errors.md`](adr/ADR-009-treat-warnings-as-errors.md) | `TreatWarningsAsErrors` with StyleCop and .NET analyzers as build gates. | 2026-04-16 |
| [`adr/ADR-010-wpf-net8-desktop-shell.md`](adr/ADR-010-wpf-net8-desktop-shell.md) | WPF on .NET 8 for the desktop shell (and why `dotnet watch` is not usable). | 2026-04-04 |

---

## `docs/specs/` — feature specs

Living current-truth contracts, one per feature. Shape is **flat**, numbering is global seeding order, and a `SPEC-NNN` is never renumbered. The sync surface in each file is `## Current behavior & invariants`.

| File | Feature | Contract |
|------|---------|----------|
| [`specs/_index.md`](specs/_index.md) | — | The map: structure declaration, one row per spec, and the sync contract. |
| [`specs/_TEMPLATE.md`](specs/_TEMPLATE.md) | — | Skeleton for a new spec. Copy it and add the row to `_index.md` in the same commit. |
| [`specs/SPEC-001-duplicate-detection.md`](specs/SPEC-001-duplicate-detection.md) | Duplicate detection | Scan one or more folders and find byte-identical files by content hash. |
| [`specs/SPEC-002-filtering-and-sorting.md`](specs/SPEC-002-filtering-and-sorting.md) | Filtering and sorting | Extension filters, minimum file size, minimum duplicate count, and result sort order. |
| [`specs/SPEC-003-custom-filter-rules.md`](specs/SPEC-003-custom-filter-rules.md) | Custom filter rules | User-authored pattern rules (contains/ignore, regex, case-sensitivity, priority, enable/disable). |
| [`specs/SPEC-004-selection-and-file-actions.md`](specs/SPEC-004-selection-and-file-actions.md) | Selection and file actions | Select all/newer/older, move, delete, and open-in-Explorer on duplicate groups. |
| [`specs/SPEC-005-file-preview.md`](specs/SPEC-005-file-preview.md) | File preview | Inline preview panel (image/video/audio/text), mini thumbnails, media playback controls. |
| [`specs/SPEC-006-analytics-and-resource-monitor.md`](specs/SPEC-006-analytics-and-resource-monitor.md) | Analytics and resource monitor | Scan statistics dashboard and live CPU/memory/thread monitor. |
| [`specs/SPEC-007-folder-search.md`](specs/SPEC-007-folder-search.md) | Folder search | Find folders by six match types combined with AND logic, with depth limiting. |
| [`specs/SPEC-008-clear-subfolders.md`](specs/SPEC-008-clear-subfolders.md) | Clear subfolders | Discover repeated subfolder names across search results and bulk-delete them. |
| [`specs/SPEC-009-settings-and-window-state-persistence.md`](specs/SPEC-009-settings-and-window-state-persistence.md) | Settings and window-state persistence | Persist preferences and window geometry to `%APPDATA%` on every mutation. |
| [`specs/SPEC-010-contextual-help.md`](specs/SPEC-010-contextual-help.md) | Contextual help | The `?` popup help system and its rich-text markup grammar. |

Each spec's `Ships in` line distinguishes behavior released in `1.0.0` from behavior that exists in the code but sits in `Unreleased` — check it before assuming a documented capability shipped.

---

## `docs/modules/` — module docs

How the code works inside one project. Dependencies point one way only: outward layers reference inward ones, never the reverse.

| File | Module | Role |
|------|--------|------|
| [`modules/_index.md`](modules/_index.md) | — | Module table, the dependency rule and its diagram, target frameworks, and the coverage boundary. |
| [`modules/core.md`](modules/core.md) | `WindowsFileManager.Core` | Domain models and the `IFileSystemService` port. Depends on nothing — no project or package references. |
| [`modules/application.md`](modules/application.md) | `WindowsFileManager.Application` | Use-case services: SHA256 hashing, the duplicate scan algorithm, settings load/save with legacy migration. Depends on Core only. |
| [`modules/infrastructure.md`](modules/infrastructure.md) | `WindowsFileManager.Infrastructure` | The single real-I/O adapter — `FileSystemService` over `System.IO`. Deliberately outside the coverage boundary. |
| [`modules/ui.md`](modules/ui.md) | `WindowsFileManager` (WPF shell) | The executable and composition root: ViewModels, MVVM primitives, converters, shell/COM helpers, XAML views. |

---

## Keeping docs current

1. **A behavior change updates its feature spec in the same commit** — edit `## Current behavior & invariants` in the owning `specs/SPEC-NNN`; the spec is never allowed to lag the code.
2. **A new load-bearing decision gets an ADR** — one that is hard to reverse and hard to infer from the code, written from evidence (a commit, a config file, a CI workflow), with a new number and a row in [`adr/README.md`](adr/README.md).
3. **Module docs move with the code** — renaming a type, changing a dependency edge, or shifting the coverage boundary updates the owning `modules/*.md` and, if the edge changed, [`../ARCHITECTURE.md`](../ARCHITECTURE.md).

Adding a file to any home also adds its row here and in that home's index (`adr/README.md`, `specs/_index.md`, `modules/_index.md`). A doc that would be created empty is not created at all.

**Deliberately absent.** `ROADMAP.md` does not exist: this repo holds no forward-work source — no planned-feature list, no issue export, no in-flight tracker — so a roadmap could only be invented. `docs/design/` does not exist for the reason given above. Both are omissions on purpose, not gaps to fill.
