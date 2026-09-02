# Project Context — Folder File Control (code identity: `WindowsFileManager`)

> Deep background for humans and AI agents. Read at session start when the task touches
> domain or business logic. For *how it's built* see [../ARCHITECTURE.md](../ARCHITECTURE.md);
> for per-decision rationale see [adr/README.md](adr/README.md); for per-feature current-truth
> behavior see [specs/](specs/); for term definitions see [GLOSSARY.md](GLOSSARY.md).

---

## Problem & Goal

A long-lived Windows machine accumulates two distinct kinds of waste, and Explorer helps with
neither. The first is **byte-identical file copies** — the same photo in three album folders, the
same installer in `Downloads` and on a backup drive, the same asset duplicated across project
trees. They are invisible because their names, dates, and locations all differ; only their
*content* is the same. The second is **folder sprawl** — dozens of project folders each carrying
the same heavy, regenerable subfolder (`node_modules`, `.venv`, `obj`, `bin`, `__pycache__`), so
the waste is not one big file but the same directory name repeated fifty times.

This application is a **single-user Windows desktop tool** that attacks both. It scans one or more
folders, groups files that are byte-identical by content hash, shows how much space each group
wastes, and lets the user select and act on the redundant copies. Separately it searches for
*folders* by structural traits (name, or "contains a `.git` child", or "does not contain
`package.json`"), then discovers subfolder names repeated across those results and bulk-removes
them. **"Good" means the user reclaims disk space without losing a file they still needed** — which
is why nearly every destructive path routes through the Recycle Bin, every bulk action shows a
confirmation dialog, and the last 30 actions are replayable from a History tab.

**Naming note.** The repository, solution, and assemblies are all `WindowsFileManager`; the
*shipped* product name is **"Folder File Control"** (`src/WindowsFileManager/Package.appxmanifest`
`DisplayName`, and the `Window.Title` in `Views/MainWindow.xaml`). The rename happened on
2026-04-16 and was never propagated to the code identifiers — expect both names in the tree.

---

## Domain Primer

The minimum vocabulary needed to work here. Each term is defined precisely in
[GLOSSARY.md](GLOSSARY.md); this section explains how the pieces fit together.

### Content hash vs. name matching — two mutually exclusive scan modes

The app can decide "these files are the same" in exactly two ways, and they never combine:

| Mode | How sameness is decided | When it is used |
|------|------------------------|-----------------|
| **Size + content hash** (default) | Files with equal byte length are hashed with SHA256; equal hashes ⇒ duplicates | `ScanOptions.MatchRegex` is empty |
| **Name regex** (opt-in) | A user regex is matched against the **file name only**; the concatenated capture groups (or the whole match) form the grouping key | `DuplicateMatchByRegex` is on *and* the pattern is non-blank |

In name-regex mode **size and content are never read** — two files grouped as "duplicates" may hold
completely different bytes. That is intentional (it is how you find `report_v1.pdf` /
`report_v2.pdf` families), but it means "duplicate" carries a weaker guarantee in that mode. The
grouping key is compared case-sensitively; case-insensitivity is the caller's job via an inline
`(?i)`. Both modes live in `DuplicateScannerService.Scan`
(`src/WindowsFileManager.Application/Services/DuplicateScannerService.cs`) — see
[specs/SPEC-001-duplicate-detection.md](specs/SPEC-001-duplicate-detection.md).

### Why size grouping precedes hashing

Hashing is the expensive step: it reads **every byte** of a file. Byte-identical files must have
identical lengths, so file length is a free, perfect pre-filter — any file whose size is unique in
the scan set cannot possibly have a duplicate and never needs to be read. The scanner therefore
runs three stages:

1. **Collect & filter** — enumerate the target paths, drop files below the minimum size or outside
   the extension filter, and de-duplicate overlapping paths (adding both `D:\` and `D:\sub` must
   not make a file its own duplicate).
2. **Group by size** — keep only size-groups with 2+ members. On a typical disk this eliminates the
   large majority of files without reading any file content.
3. **Hash the survivors** — compute SHA256 for every file in each surviving size-group, re-group by
   hash, and keep hash-groups with 2+ members. Same size + different content correctly produces no
   group.

The cost model is what matters for future changes: *stage 2 is O(n) metadata reads; stage 3 is
O(bytes) disk reads.* Anything that pushes more files into stage 3 (for example, removing the size
filter) makes scans dramatically slower.

### Duplicate group

A **duplicate group** is the unit the whole UI is built around: a grouping key (`Hash`), a
representative `FileSize`, and 2+ member files sorted by path — the `DuplicateGroup` model in
`src/WindowsFileManager.Core/Models/DuplicateGroup.cs`. A group is never created with fewer than
two members. Groups are presented sorted by wasted space, descending — the biggest win first.

### Wasted space

**Wasted space is the space you would get back if you kept exactly one copy.** For a group it is
`sum(all file sizes) − max(file size)`, so a group of three 1 MB files wastes 2 MB, not 3 MB. A
fallback branch exists for groups whose individual sizes are all zero (legacy and test fixtures):
`FileSize × (Count − 1)`. The scan-level `TotalWastedBytes` is the sum across groups, and the
Analytics dashboard expresses it as a percentage of the total duplicate byte volume. This number is
the app's headline metric — it is what "reclaiming disk space" means numerically.

### The keeper — a coined term, and a deliberate non-guarantee

The **keeper** is whichever copy in a group is *left unselected* when the user acts. Nothing in the
data model marks a file as the keeper; it is purely emergent from selection:

- **Select Newer** selects every copy except the oldest by `LastModified` ⇒ the **oldest** is the keeper.
- **Select Older** selects every copy except the newest ⇒ the **newest** is the keeper.
- **Select All** selects every copy in every group ⇒ **there is no keeper**.

There is no invariant anywhere in the codebase that forces one copy of a group to survive. "Delete
All" on a group deletes *every* member after confirmation. The safety net for that is the Recycle
Bin plus the undo history, not a guard rail in the model. Any change that touches selection or
deletion must keep this understood: **the app trusts the user's selection and makes it reversible,
rather than preventing it.**

### Exclusions, filters, and rules — three different things

Three separate mechanisms narrow what a scan produces, and they act at three different times:

- **Exclude folders** — folder *names* (e.g. `node_modules`) skipped during enumeration. Applied
  before any file is looked at, so they make the scan faster as well as smaller.
- **Extension filters / size filters** — applied to the *results* after a scan, as a view over the
  duplicate groups. Changing them does not rescan.
- **Filter rules** — user-authored patterns (contains-match or regex, against file name or full
  path) that drive *selection*, not visibility. Applying rules walks each file through the rule list
  in priority order; the **first enabled matching rule wins** and decides whether the file ends up
  checked (`Include`) or unchecked (`Exclude`).

Confusing these three is the most common way to misread this codebase. See
[specs/SPEC-002-filtering-and-sorting.md](specs/SPEC-002-filtering-and-sorting.md) and
[specs/SPEC-003-custom-filter-rules.md](specs/SPEC-003-custom-filter-rules.md).

### Folder search — finding folders, not files

The second half of the app answers a different question: *"which folders look like this?"* A search
pattern pairs a text pattern with a `FolderMatchType`, and every **enabled** pattern must pass
(**AND** logic) for a folder to appear in the results. The six match types split into three
name-based tests and three child-existence tests, each with its negation — see
[GLOSSARY.md](GLOSSARY.md) for the exact semantics of each value. Two properties matter for
reasoning about it:

- **Zero enabled patterns means every folder matches** (an empty filter is not an empty result).
- Only *sub*directories are ever results; a root path you typed in is never itself a hit.

Search depth is limited by an optional maximum depth (blank ⇒ unlimited; `1` ⇒ direct children
only). See [specs/SPEC-007-folder-search.md](specs/SPEC-007-folder-search.md).

### Clear subfolders — the sprawl half of the product

Once you have a set of matched folders, **scan subfolders** discovers every distinct subfolder
*name* across them with an occurrence count and the list of parents it appears under (e.g.
`node_modules (37)`). Selecting a name and clearing it recycles that subfolder inside **every**
matched root at once. This is the feature that turns "50 project folders each with a 300 MB
dependency tree" into a single confirm-and-reclaim action, and it is also the single most dangerous
action in the app — one confirmation authorizes N folders × M roots of recursive deletion. See
[specs/SPEC-008-clear-subfolders.md](specs/SPEC-008-clear-subfolders.md).

### Where state lives

Everything the user configures is a **profile** — a named bundle of target paths, exclusions,
filters, rules, search patterns, and view preferences. Profiles, the active profile name, the
action history, and the window geometry are serialized as one JSON document to
`%APPDATA%\WindowsFileManager\settings.json`, rewritten **on every mutation** (not on close). See
[specs/SPEC-009-settings-and-window-state-persistence.md](specs/SPEC-009-settings-and-window-state-persistence.md).

---

## Key User Flows

### Flow 1 — Reclaim space from duplicate files

1. **Choose targets.** The user adds one or more folders (browse dialog or typed path) and
   optionally adds folder *names* to exclude. Each entry has an enable/disable checkbox, so a path
   can be parked without being removed.
2. **Scan.** `ScanCommand` runs the scan off the UI thread with a cancellation token and a progress
   callback that reports every 100 files kept. The status row shows a live file count; the resource
   monitor shows CPU/RAM/threads. Cancel is available throughout.
3. **Read the result.** Duplicate groups arrive sorted by wasted space descending. The Analytics
   panel summarizes the scan: totals, duration, wasted bytes, duplicate percentage, top duplicated
   extensions, and a six-bucket size distribution.
4. **Filter the view.** Extension toggles, a minimum file size (with a B/KB/MB/GB unit), and a
   minimum duplicate count narrow the visible groups; a sort selector reorders them. These are view
   filters — no rescan happens.
5. **Select what to remove.** Either directly (per-file checkboxes, per-group *Select All* /
   *Clear*), or in bulk via **Select All / Select Newer / Select Older**, or by **applying filter
   rules**. After any bulk selector runs, every `Exclude` rule sweeps back through and *deselects*
   its matches — the ignore rules always win over a bulk select.
6. **Act.** Delete the selected files (to the Recycle Bin), delete one file or a whole group, or
   move the selection to a target folder (created if missing; name collisions get a `_HHmmss`
   suffix). Every one of these confirms first and is recorded in the action history.
7. **Verify.** *Open in Explorer* on any file or group selects it in a real Explorer window.

Implemented across `MainViewModel` (`src/WindowsFileManager/ViewModels/MainViewModel.cs`),
`DuplicateScannerService` and `FileHashService`
(`src/WindowsFileManager.Application/Services/`). Specs:
[SPEC-001](specs/SPEC-001-duplicate-detection.md),
[SPEC-002](specs/SPEC-002-filtering-and-sorting.md),
[SPEC-004](specs/SPEC-004-selection-and-file-actions.md),
[SPEC-005](specs/SPEC-005-file-preview.md),
[SPEC-006](specs/SPEC-006-analytics-and-resource-monitor.md).

### Flow 2 — Find folders, then clear repeated subfolders

1. **Define the shape of the folder.** The user adds one or more search patterns, each a text
   pattern plus a match type (e.g. `Contains` + `.git`, then `Include` + `client`), reorders them by
   priority, and can disable any of them without deleting it. Optionally a maximum search depth is
   set.
2. **Search.** `SearchFoldersCommand` walks each enabled target path, skipping excluded folder names
   entirely, and collects every subdirectory for which **all** enabled patterns pass. A second pass
   computes each hit's total size (cancellable). Results are a sortable grid — clicking the *Size*
   or *Full Path* header sorts on the underlying value, not the formatted string.
3. **Select roots.** The user checks the folders to operate on (a header checkbox does a bulk
   set/clear).
4. **Discover subfolders.** *Scan Subfolders* walks the selected roots and produces a list of
   distinct subfolder names with occurrence counts, total sizes, and expandable per-location lists,
   with a text filter and paging (50 locations per page) for large result sets. A sibling scan
   produces the same view for *file types*.
5. **Clear.** Checking subfolder names and confirming recycles that subfolder under every selected
   root. The confirmation names the count, the subfolder names, and the number of roots. Progress is
   throttled; the operation is recorded in history as a `RecycleDirectories` entry; the subfolder
   list re-scans automatically afterwards.
6. **Adjacent actions on the same result set.** *Flatten* moves nested files up to each selected
   root (with `(2)`, `(3)` conflict suffixes and optional empty-directory removal); *Link Sibling
   Folders* creates `.lnk` shortcuts between sibling folders under a shared parent.

Implemented in `MainViewModel` (`SearchFoldersAsync`, `SearchFoldersRecursive`, `ScanSubfolders`,
`ClearSelectedSubfolders`, `FlattenFolder`, `LinkSiblingsRecursive`) with the `FolderSearchPattern`,
`FolderSearchResult`, and `SubfolderItem` models in `src/WindowsFileManager.Core/Models/`. Specs:
[SPEC-007](specs/SPEC-007-folder-search.md), [SPEC-008](specs/SPEC-008-clear-subfolders.md).

### Flow 3 — Configure rules and preferences, and have them persist

1. **Author a rule.** The user types a pattern, picks an action (`Include` = check matching files,
   `Exclude` = uncheck them), picks a target (`Filename` or `Filepath`), and toggles regex and
   ignore-case. *Add* appends the rule and assigns priorities by list position.
2. **Order and toggle.** Move up / move down re-prioritizes (priority 1 = highest = evaluated
   first). Each rule has an enable checkbox; *Enable All* / *Disable All* / *Clear All* act in bulk.
3. **Apply.** *Apply Rules* clears the current selection, then for each file walks the rule list and
   lets the **first enabled matching rule** decide selection. Regex evaluation is bounded by a
   1-second match timeout; a pattern that times out (or throws) is treated as *no match*.
4. **Persist — automatically, on every change.** There is no Save button. Adding a path, toggling a
   checkbox, reordering a rule, changing a sort option, selecting a folder result — each calls
   `SaveSettings()`, which serializes the whole `AppSettings` document to
   `%APPDATA%\WindowsFileManager\settings.json`. Window position, size, and maximized state are
   saved on close (using `RestoreBounds` when maximized) and restored on load only after validating
   the saved rectangle is still on a connected monitor.
5. **Switch context with profiles.** *New* (blank) / *Clone* (deep copy) / *Rename* / *Delete*
   manage named profiles through a modal dialog with live duplicate-name validation. Switching a
   profile snapshots the live state into the outgoing profile and applies the incoming one across
   the 20 fields that `SnapshotLiveStateInto` / `ApplyProfileToLiveState` round-trip. Of
   `ProfileSettings`'s 22 properties, `Name` is the profile's identity and `MinimumFileSize` is not
   carried through the switch. Window geometry and action history are **global**, not per-profile.

Implemented in `MainViewModel` plus `SettingsService`
(`src/WindowsFileManager.Application/Services/SettingsService.cs`) and the `AppSettings` /
`ProfileSettings` / `FilterRule` models. Specs:
[SPEC-003](specs/SPEC-003-custom-filter-rules.md),
[SPEC-009](specs/SPEC-009-settings-and-window-state-persistence.md).

### Flow 4 — Undo a destructive action

1. Every completed bulk action pushes an `ActionHistoryEntry` (kind: `MoveFiles`, `RecycleFiles`,
   `RecycleDirectories`, or `CreateShortcuts`) onto the front of a 30-entry history that is
   persisted with the settings.
2. The **History** tab lists entries with per-kind counters and offers *Undo Last*, *Undo Specific*,
   and *Clear History*.
3. Undo replays the inverse: moves are reversed **in reverse order** so conflict-renames unwind
   correctly; recycled files and folders are restored via the Windows Shell Recycle Bin `Restore`
   verb; created shortcuts are deleted.
4. **Undo is best-effort, not guaranteed.** Recycle-Bin restore matches entries by original location
   and name using a shell detail column, and silently reports zero restored if the match fails.
   Empty directories removed during *Flatten* are hard-deleted and are **not** recorded in history —
   that path is not undoable.

---

## Historical "Why" (decision timeline)

Reconstructed from `git log`, [../CHANGELOG.md](../CHANGELOG.md), and the dated `## Project Notes`
in [../CLAUDE.md](../CLAUDE.md). Decision records are indexed in [adr/README.md](adr/README.md).

| When | Decision (what + why) | ADR |
|------|----------------------|-----|
| 2026-04-04 | **Initial commit** — WPF duplicate file finder with multi-folder support. Desktop WPF chosen for the direct Windows Shell / filesystem access the product needs. | ADR-010 |
| 2026-04-04 | **Restructured into a modular monorepo with Clean Architecture layers** (Core / Application / Infrastructure / UI) on the same day as the first commit — so business logic could be unit-tested without touching a real disk. | ADR-001, ADR-004 |
| 2026-04-04 | **GitHub Actions CI added** (build + `dotnet format` + test) — quality gates enforced in CI from day one rather than retrofitted. | ADR-009 |
| 2026-04-04 | **MSIX packaging + Microsoft Store CI/CD pipeline added**, followed the same day by three fix commits (use `MakeAppx.exe` against the publish output; add the `EntryPoint` attribute the manifest requires; skip signing when certificate secrets are absent) — the pipeline had to work for contributors without the signing secret. | ADR-008 |
| 2026-04-04 | **Preview panel, filters, sorting, mini previews, resource monitor** land; layout restructured to a 2-column grid so the analytics dashboard could be full-height. Shell thumbnails adopted for video/document previews because WPF can only decode a small set of image formats natively. | — |
| 2026-04-05 | **Bulk file management** — multi-select, an actions panel, and move-to-folder. The product moves from "show me duplicates" to "act on duplicates". | — |
| 2026-04-06 | **Granular move options added** (move oldest / newest / by filename / by path) — and **reworked the same day**: "separate selection from action" and "replace group-level selection with per-group Select All/Clear". The lasting design is *selection commands decide which copies, a single move/delete action executes* — the granular move variants no longer exist in the code, though the v1.0.0 changelog entry still lists them. | — |
| 2026-04-06 | **Ignore filename/filepath filters with regex + case toggles** — the seed of the custom filter-rule system. | — |
| 2026-04-06 | **Regex match timeout added for ReDoS prevention**, alongside a StyleCop warning cleanup. User-authored regexes are untrusted input; every regex site is time-boxed. | — |
| 2026-04-07 | **Filter settings persisted**; collapsible sections and clear-rules controls added. | — |
| 2026-04-09 | **Contextual `?` help buttons introduced**, and *ignore-over-contain* rule priority enforced — an ignore rule must be able to override a broad select rule. | SPEC-010 |
| 2026-04-12 → 04-13 | **A separate "Search" tab was added, then removed the next day**; target folders moved above the content area instead. Search was folded back into the main surface rather than kept as a parallel tab. | — |
| 2026-04-14 | **Actions workflow redesigned** around dynamic filter rules, exclude folders, and priority ordering — the rule model reaches its current shape. | SPEC-003 |
| 2026-04-14 | **Recorded: `dotnet watch run` does not work for WPF** (watch/hot-reload is web-only). Development uses plain `dotnet run`; agents must not assume a watch loop. | ADR-010 |
| 2026-04-15 | **Tabbed UI with full-height Analytics and File Preview panels**; filter UI refactored from collapsible panels to always-visible inline `WrapPanel` rows so everything is visible at a glance and wraps responsively. | — |
| 2026-04-15 | **Window state persistence** added, with multi-monitor validation before restoring a saved rectangle. | SPEC-009 |
| 2026-04-15 | **Settings are saved on every mutation, not on window close** — because `Stop-Process -Force` (and any hard kill) skips `Window.Closing`, and users were losing their configuration. | ADR-006 |
| 2026-04-15 | **`FilterAction.Select` renamed to `Contains` for clarity, keeping enum ordinal 0** — `System.Text.Json` writes enums as integers, so ordinal stability is the back-compat contract for existing `settings.json` files. | ADR-007 |
| 2026-04-15 | **`CHANGELOG.md` added** with the v1.0.0 baseline, adopting Keep a Changelog + Semantic Versioning as the release-notes convention. | — |
| 2026-04-15 | **Folder Control tab** shipped — configurable folder search patterns, inline editable rule chips, priority reorder, select-all. The product's second pillar (folder sprawl) begins. | SPEC-007 |
| **2026-04-15** | **v1.0.0 released** (the only released version to date; `CHANGELOG.md` has carried an empty `[Unreleased]` section since). | — |
| 2026-04-16 | **Quality gates hardened** — `TreatWarningsAsErrors` turned on, so every StyleCop/Roslyn warning is now a build error; `Exclude` / `Mismatch` / `Contains` match types, column sorting, and tab-panel switching added. | ADR-009 |
| 2026-04-16 | **Coverage enforcement moved from `coverlet.runsettings` to `coverlet.msbuild` in the test `.csproj`** — the data-collector settings file could not enforce a threshold. Coverage was ~44% at the time and the note recorded the intent to drive it to 100%. | ADR-005 |
| 2026-04-16 | **Clear Subfolders action added**; the app renamed to "Folder File Manager" and the tab to "Duplicates Control". (The shipped display name is now "Folder File Control".) | SPEC-008 |
| 2026-04-16 | **Recorded: `SelectionChanged` bubbles from nested `ListView`s to the `TabControl` handler** — the handler must check `e.Source == tabControl` or clicking a file hides the side panels. | — |
| 2026-04-17 | **Folder scan gains file types, a size column, paging, and persistent state.** | — |
| 2026-04-18 | **Deletes routed to the Recycle Bin and an undo history introduced**, covering every destructive action; flatten-folders and scan loading indicators added. This is the decision that makes the app's destructive features acceptable. | — |
| 2026-04-18 | **100% coverage restored — 90 new tests plus a coverlet config fix**, closing the gap opened on 04-16. | ADR-005 |
| 2026-04-18 | **History tab** shipped, plus a flatten file-type filter. | — |
| 2026-04-22 → 04-23 | **Profiles** — save and switch between multiple workspaces; then a profile-name dialog, right-aligned profile bar, the `NotContain` match type, a split between blank *New* and *Clone*, a bulk-select lag fix, and a non-blocking profile switch. | SPEC-009 |
| 2026-04-24 | **Link sibling folders**, non-blocking long actions, and a global progress bar with ETA. | — |
| 2026-04-27 | **Regex duplicate-matching mode** (match by file name instead of content) and a **folder-search depth limit** added. | SPEC-001, SPEC-007 |
| 2026-04-28 | **Per-file size column, a per-file preview button, and mutually exclusive filter modes** — the base-filter toolbar and the regex-match toolbar became alternatives rather than co-existing controls. | — |
| 2026-07-12 | **Application icon** added for taskbar, title bar, and the executable. | — |
| 2026-08-30 | **Documentation set reverse-engineered from the code** (this file, ADRs, feature specs, module docs). Measured on the same day: 217 tests passing, 100% line/branch/method coverage on Core + Application + UI ViewModels/Helpers. Prior claims of "105 tests", "96% branch", and "~44% coverage" in `README.md` / `CLAUDE.md` were stale. | — |

---

## Constraints & Non-Goals

### Constraints

- **Constraint: Windows-only, and deliberately so.** The UI is WPF; the target frameworks are
  `net8.0-windows` / `net8.0-windows10.0.22621.0`. Beyond WPF, the app calls the Windows Shell
  directly for thumbnail extraction (`IShellItemImageFactory`), Recycle-Bin delete and restore
  (`Microsoft.VisualBasic.FileIO.FileSystem` and `Shell.Application`), and shortcut creation
  (`WScript.Shell`). There is no cross-platform abstraction and no plan for one — a port would be a
  rewrite of the Infrastructure and Helpers layers, not a retarget.
- **Constraint: single-user desktop, no server, no accounts.** All state is one JSON file under the
  current user's `%APPDATA%`. There is no login, no multi-user awareness, and no file locking — two
  instances of the app writing settings simultaneously is undefined behavior.
- **Constraint: no network I/O at runtime.** The application makes zero HTTP calls. The only network
  reach in the whole repository belongs to CI (Semgrep rule fetch, the timestamp server used when
  signing) and to `<link=...>` hyperlinks inside help popups, which hand a URL to the default
  browser. Any proposal that introduces runtime network traffic changes the product's threat model
  and needs an explicit decision.
- **Constraint: the app runs unelevated and unsandboxed over the user's real filesystem.** There is
  no app manifest requesting elevation, and the MSIX declares the restricted `runFullTrust`
  capability. It can read and delete anything the signed-in user can. There is **no allow-list of
  scannable roots and no block on system paths** — protection is confirmation dialogs plus the
  Recycle Bin, nothing structural.
- **Constraint: destructive actions are local, bulk, and only *mostly* reversible.** Deletes of
  files and subfolders go to the Recycle Bin and are recorded in a 30-entry undo history. Two paths
  are **not** reversible: empty directories removed during *Flatten* are hard-deleted with no
  history entry, and Recycle-Bin restore is a best-effort shell operation that can silently restore
  nothing. Treat any new destructive operation as needing both a confirmation and a history entry —
  nothing in the build enforces that today.
- **Constraint: user input is untrusted and must stay time-boxed.** Regex patterns come from the
  user, are persisted to `settings.json`, and are reloaded on the next launch. Both regex evaluation
  sites carry a 1-second match timeout; removing one reintroduces a ReDoS stall that survives
  restarts. See [SECURITY.md](SECURITY.md).
- **Constraint: 100% line/branch/method coverage is a build gate**, enforced by `coverlet.msbuild`
  in the test project on Core, Application, and the UI's ViewModels + Helpers. Infrastructure is
  excluded because it *is* the real I/O. New logic without tests fails the build, not just the
  review.

### Non-Goals

- **Non-goal: cloud sync, cloud storage, or a companion service.** Settings are a local file. Do not
  add a sync layer, a remote backup of the history, or an account system.
- **Non-goal: telemetry, analytics reporting, crash upload, or update checks.** The "Analytics"
  feature in this app is a *local dashboard about the current scan*; it sends nothing anywhere.
- **Non-goal: cross-platform support** (macOS, Linux, .NET MAUI, web). See the Windows-only
  constraint above.
- **Non-goal: a general file manager.** Despite the assembly name, this is not an Explorer
  replacement — there is no browse tree, no copy/paste, no rename, no properties dialog. The scope
  is *find redundancy and remove it*.
- **Non-goal: preventing the user from deleting every copy of a file.** The design makes destructive
  actions reversible rather than restricted; see "The keeper" above. Do not add a silent "always
  keep one" guard — it would contradict the existing selection commands and the "Delete All in
  group" action.
- **Non-goal: content-aware or fuzzy duplicate detection.** Duplicates are byte-identical (SHA256)
  or name-pattern matches. Perceptual image hashing, near-duplicate video matching, and
  similar-document detection are all out of scope.
- **Non-goal: a scheduler, service, or background agent.** Scans run only while the window is open
  and the user asked for one.
- **Non-goal: ROADMAP.md.** No forward-work source exists in this repository (the `[Unreleased]`
  changelog section is empty and there is no issue backlog in-tree), so a roadmap would have to be
  invented. It is deliberately absent rather than stubbed.

---

## External Context

### Platform and framework

| Dependency | Role here | Reference |
|---|---|---|
| **.NET 8 (SDK 8.0.422)** | Target runtime for all five projects. On the current development machine the SDK lives at `D:\_env_storeage\dotnet` and is **not on `PATH`** — see [DEV.md](DEV.md). | <https://dotnet.microsoft.com/> |
| **WPF (Windows Presentation Foundation)** | The desktop UI framework; `UseWPF=true` on the UI and test projects. Supplies XAML data binding, `MediaElement` playback, and `BitmapImage` decoding. | <https://learn.microsoft.com/dotnet/desktop/wpf/> |
| **Windows Shell (shell32 / COM)** | Thumbnail extraction for non-natively-decodable files, Recycle-Bin delete and restore, `.lnk` shortcut creation. Third-party shell thumbnail extractors execute in-process. | <https://learn.microsoft.com/windows/win32/shell/shell-entry> |

### Distribution

| Dependency | Role here | Reference |
|---|---|---|
| **MSIX + Microsoft Store** | The intended distribution channel. The package is assembled in CI by `makeappx.exe` from a hand-built layout (`publish` output + `Package.appxmanifest` + assets) rather than by an MSBuild packaging project. | <https://learn.microsoft.com/windows/msix/> |
| **Windows App Certification Kit (WACK)** | Store-certification gate run in CI (`appcert.exe`) against the produced `.msix`. | <https://learn.microsoft.com/windows/uwp/debug-test-perf/windows-app-certification-kit> |
| **`signtool` + a code-signing PFX** | Signs the MSIX, but only on a direct push to `main` and only when the `CERTIFICATE_PFX` secret is present. The certificate subject **must** equal the manifest `Publisher` (`CN=WindowsFileManager`). | <https://learn.microsoft.com/windows/win32/seccrypto/signtool> |

### Conventions this project follows

| Convention | How it is applied | Reference |
|---|---|---|
| **Keep a Changelog** | `CHANGELOG.md` structure (`[Unreleased]`, then dated releases with `### Added` etc.). | <https://keepachangelog.com> |
| **Semantic Versioning** | Version numbering; `1.0.0` released 2026-04-15. The manifest carries the 4-part MSIX form `1.0.0.0`. **Nothing keeps the manifest version, the changelog, and any tag in sync** — there is no `<Version>` property in any `.csproj` and no CI check. | <https://semver.org> |
| **Conventional Commits** | Used in practice for commit subjects (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `perf:`), which is what makes the timeline above reconstructable. Not enforced by a hook. | <https://www.conventionalcommits.org> |

### CI and quality tooling

| Dependency | Role here | Reference |
|---|---|---|
| **GitHub Actions** | Two workflows: `ci.yml` (format → build → test+coverage → dependency vulnerability audit) and `msix-pipeline.yml` (Semgrep → build/package → WACK). | <https://docs.github.com/actions> |
| **StyleCop.Analyzers 1.1.118 + .NET analyzers** | Style and correctness rules, made fatal by `TreatWarningsAsErrors` in `Directory.Build.props`. Fourteen StyleCop rules are explicitly suppressed in `.editorconfig`, each with a rationale comment. | <https://github.com/DotNetAnalyzers/StyleCopAnalyzers> |
| **coverlet.msbuild 6.0.2** | Enforces the 100% line/branch/method threshold during `dotnet test`. | <https://github.com/coverlet-coverage/coverlet> |
| **xUnit + Moq + FluentAssertions** | The test stack: 217 tests, run fully serially (`xunit.runner.json` disables assembly and collection parallelism). | <https://xunit.net> |
| **Semgrep (`p/default` + `p/csharp`)** | SAST in the MSIX pipeline; `--error` makes any finding block packaging. Results upload as **SARIF** to the GitHub Security tab. | <https://semgrep.dev> |

### Upstream / downstream

There are **no upstream services and no downstream consumers**. The application is a leaf: it reads
the user's filesystem, writes its own settings file, and hands paths to Explorer. Nothing imports
this code as a library, and no sibling project depends on it. The only external party in the loop is
the Microsoft Store, as a distribution endpoint.
