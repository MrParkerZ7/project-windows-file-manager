# Feature specs — index

## Structure
- shape: flat
- numbering: global-seeding-order
- entry: SPEC-NNN-<slug>.md

## The map

| Spec | Feature | Status | Why |
|------|---------|--------|-----|
| [SPEC-001](SPEC-001-duplicate-detection.md) | Duplicate detection | Current | Scan one or more folders and find byte-identical files by content hash |
| [SPEC-002](SPEC-002-filtering-and-sorting.md) | Filtering and sorting | Current | Extension filters, minimum file size, minimum duplicate count, and result sort order |
| [SPEC-003](SPEC-003-custom-filter-rules.md) | Custom filter rules | Current | User-authored pattern rules (contains/ignore, regex, case-sensitivity, priority, enable/disable) |
| [SPEC-004](SPEC-004-selection-and-file-actions.md) | Selection and file actions | Current | Select all/newer/older, move, delete, and open-in-Explorer on duplicate groups |
| [SPEC-005](SPEC-005-file-preview.md) | File preview | Current | Inline preview panel (image/video/audio/text), mini thumbnails, media playback controls |
| [SPEC-006](SPEC-006-analytics-and-resource-monitor.md) | Analytics and resource monitor | Current | Scan statistics dashboard and live CPU/memory/thread monitor |
| [SPEC-007](SPEC-007-folder-search.md) | Folder search | Current | Find folders by six match types combined with AND logic, with depth limiting |
| [SPEC-008](SPEC-008-clear-subfolders.md) | Clear subfolders | Current | Discover repeated subfolder names across search results and bulk-delete them |
| [SPEC-009](SPEC-009-settings-and-window-state-persistence.md) | Settings and window-state persistence | Current | Persist preferences and window geometry to %APPDATA% on every mutation |
| [SPEC-010](SPEC-010-contextual-help.md) | Contextual help | Current | The "?" popup help system and its rich-text markup grammar |

## Sync contract

A change that alters a feature's behavior updates that feature's spec in the **same commit** — the spec is never allowed to lag the code. The section that must move is `## Current behavior & invariants`; the rest of a spec changes only when the feature's intent or scope changes.

Each documentation kind has exactly one home and is never duplicated across them:

| Kind | Home | Lifetime | Answers |
|------|------|----------|---------|
| Feature spec | `docs/specs/` | living — synced as behavior lands | how does this feature behave today? |
| Decision | [`../adr/`](../adr/) | frozen at acceptance | why this way? |
| Module doc | [`../modules/`](../modules/) | living, code-adjacent | how does this code work? |

Specs are the current-truth contract; designs (when a project keeps them) are frozen intent; ADRs are frozen decisions; module docs describe code mechanics. A spec **links** to the other kinds — it never copies them. The dividing line that recurs: a spec describes feature behavior *across* modules, a module doc describes one module's mechanics.

Numbering is global and assigned in seeding order. A `SPEC-NNN` id is **never renumbered** — a retired feature's spec is marked superseded or removed, and its number is not reused.

Start a new spec from [`_TEMPLATE.md`](_TEMPLATE.md) and add its row to the map above in the same commit.
