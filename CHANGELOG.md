# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com),
and this project adheres to [Semantic Versioning](https://semver.org).

## [Unreleased]

18 commits have landed since 1.0.0 without a release. Grouped from the git log; no
version has been tagged yet (`git tag` is empty and no GitHub release exists).

### Added

- **Folder Control tab** — search folders by configurable patterns with inline editable
  rule chips, priority reordering, select-all, and an Action section in the sidebar
- **Six folder match types** — `Include`, `Match`, `Contains`, `Exclude`, `Mismatch`,
  `NotContain`, combined with AND logic across all enabled patterns
- **Folder-search depth limit** — bound how deep a recursive folder search descends
- **Clear Subfolders** — discover repeated subfolder names across search results,
  expand each to its parent locations, and bulk-delete with confirmation
- **Flatten Folders** — move nested files up, with a file-type filter and `(2)`/`(3)`
  conflict renaming
- **Profiles** — save and switch between multiple workspaces, with a profile name dialog,
  a right-aligned profile bar, and separate blank-New vs Clone creation
- **History tab** — a global action history with undo across all destructive actions
- **Recycle Bin deletes** — destructive actions route to the Recycle Bin rather than
  deleting permanently
- **Scan Folders view** — file-type breakdown, size column, paging, and persistent state
- **Regex matching for duplicates**, and mutually exclusive filter modes
- **Per-file size and a per-file Preview button** in duplicate group rows
- **Sibling folder linking**, non-blocking actions, and a global progress bar
- **Column sorting** and tab-aware sidebar panel switching
- **Application icon** for the taskbar, title bar, and executable
- **AI-native documentation set** — `ARCHITECTURE.md` plus `docs/` with ADRs, feature
  specs, module docs, context, security guardrails, and a dev guide

### Changed

- **Renamed the application to "Folder File Control"** (`Package.appxmanifest`
  `DisplayName` and the main window title). The repository, solution, assemblies, and
  namespaces remain `WindowsFileManager`.
- Renamed the duplicates tab to "Duplicates Control"
- Moved "Include Subdirectories" into the header row; replaced `GroupBox` with `Border`
- Profile switching no longer blocks the UI thread
- **Running the tests locally is now two commands** if you want the coverage gate:
  `dotnet test -c Release --collect:"XPlat Code Coverage" --settings tests/WindowsFileManager.Tests/coverlet.runsettings`
  followed by `pwsh ./scripts/Check-Coverage.ps1`. A bare `dotnet test` still runs all
  217 tests but no longer fails on low coverage, and `-p:CollectCoverage=false` — which
  belonged to the removed package — now skips nothing.

### Fixed

- Bulk-select lag when toggling many rows at once
- **CI coverage gate rebuilt.** The 100% line/branch/method bar is unchanged, but the
  mechanism behind it is new. `coverlet.msbuild` instrumented assemblies during the
  build and raced: whole modules intermittently reported 0% while all 217 tests passed,
  failing the gate in roughly 1 run in 5. Reproduced across coverlet 6.0.2, 6.0.4 and
  10.0.1 — no version fixed it. Coverage is now measured by the `coverlet.collector`
  data collector, which instruments at runtime, and the threshold is enforced by
  `scripts/Check-Coverage.ps1`. See ADR-011.
- Removed a redundant `/p:TreatWarningsAsErrors=true` from the CI build step; the
  property is already set in `Directory.Build.props`.

### Security

- **Every GitHub Action is pinned to a commit SHA** rather than a mutable major tag,
  closing 10 blocking Semgrep `github-actions-mutable-action-tag` findings that had kept
  the MSIX pipeline red. `.github/dependabot.yml` keeps the pins current with a weekly
  grouped PR, a release cooldown, and major bumps ignored.

### Internal

- Restored 100% coverage (90 new tests) and fixed the coverlet configuration
- Added quality gates to CI (format, build-with-warnings-as-errors, test+coverage,
  dependency audit)

## [1.0.0] - 2026-04-15

Initial release.

### Added
- Duplicate file finder with multi-folder scan and hash-based detection
- Modular monorepo with Clean Architecture layers (Core, Application, Infrastructure, UI)
- File preview panel with image, video, audio, and text support
- Mini thumbnail previews in duplicate group list
- Analytics dashboard with scan statistics and storage insights
- Dynamic filter rules with regex pattern matching and priority ordering
- Extension type filters with select all/clear controls
- Size and duplicate count filters with apply button
- Inline filter UI with responsive WrapPanel layout
- Contextual help buttons (`?` popups) with rich text and clickable links
- Bulk file management: delete, move, select all/newer/older
- Granular move options: move by oldest, newest, filename, or path
- Exclude folders from scan by name
- Collapsible sections for filters and actions
- Window state persistence (position, size, maximized) across sessions
- Live resource monitor (CPU, memory) during scan
- Tabbed UI with full-height Analytics and File Preview panels
- Settings persistence to `settings.json` (paths, filters, rules, preferences)
- GitHub Actions CI pipeline (build, format, test)
- MSIX packaging and Microsoft Store CI/CD pipeline
- 100% test coverage on Core and Application layers (xUnit + Moq + FluentAssertions)
