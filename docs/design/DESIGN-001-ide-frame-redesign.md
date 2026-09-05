# DESIGN-001 — IDE-frame redesign (option 3a)

<!-- Freeze contract: this record is frozen at adoption. It is NEVER edited to match what shipped.
     Deviations are recorded in the owning SPEC-NNN (and, if load-bearing, an ADR) — not here.
     docs/design/ is outside the sync surface of any docs-refresh pass, `perfect-docs` included. -->

- Status: Adopted
- Adopted: 2026-09-04
- Supersedes: —
- Source: [`canvas/redesign-explorations.dc.html`](canvas/redesign-explorations.dc.html) `#3a`
- Governs: the WPF shell frame (`src/WindowsFileManager/Views/…`)
- Realised by: `T-001` … `T-011` (epic `G-001`)
- Decision record: `ADR-012` (written by `T-012`)

## What was explored

Four turns over the Folder File Control shell, on the **Broadsheet** design system. Ids and turn titles are verbatim
from the canvas.

| Turn | Title (verbatim) | Artboards | Status |
|---|---|---|---|
| 1 | Folder File Control — simplified in Broadsheet | 1a 1b 1c 1d 1e 1f | Explored — not adopted |
| 2 | Clear section boundaries | 2a 2b | Explored — not adopted |
| 3 | IDE-standard structure (IntelliJ conventions) in Broadsheet | **3a** · 3b | **3a ADOPTED** · 3b not adopted |
| 4 | Colour + theme switch on 3a | 4a | **Explored — deferred to a follow-on goal. Not a requirement of this epic.** |

Turn 3's own brief is the scope fence, quoted because it is what keeps turn 4 out:

> "Borrowed from IDE tool-window practice: every panel has a 30px title bar (name left, actions right, hairline under
> it); panels are separated by 1px splitters; main tabs are underlined editor tabs; each panel gets one toolbar row
> for its controls; a single status bar at the bottom. **The typography, colors and controls stay Broadsheet — only
> the framing is IDE-standard.**"

## Inventory of 3a

Transcribed from the markup, not paraphrased. Where this table and any prose summary disagree, this table wins.

| Region | Verbatim geometry / class | Contents |
|---|---|---|
| Title bar | `height:34px; padding:0 14px; gap:14px; background:var(--color-surface); border-bottom:1px solid var(--color-divider)` | "Folder File Control" (`--font-heading`, 600, 14px) · "Photos cleanup ▾" (`--color-neutral-600`) · `margin-left:auto` window controls `— ▢ ✕` (12px, gap 26px). **No theme switch.** |
| Tab strip | `height:38px; padding:0 14px; border-bottom:1px solid var(--color-divider); background:var(--color-bg)` | Tabs `padding:0 16px`, `--font-heading` 14px. Active: weight 600 + `border-bottom:2px solid var(--color-accent)` + `margin-bottom:-1px`. Inactive: `--color-neutral-600`. Order **Duplicates · Folders · History**. Right (`margin-left:auto`): `<button class="btn btn-primary">Scan</button>` (`min-height:28px; padding:0 14px`) + caption "last scan 10:12 · 4.2 s". |
| Body | `display:grid; grid-template-columns:260px 1px 1fr 1px 360px; min-height:0` | Scope ‖ splitter ‖ results ‖ splitter ‖ right stack |
| Splitters | `<div style="background:var(--color-divider)">` at 1px | **THREE, not two** — two vertical in the body grid **plus one horizontal** in the right stack (`grid-template-rows:1fr 1px 1fr`) |
| Tool-window header (all four) | `height:30px; padding:0 12px; border-bottom:1px solid var(--color-divider)`; name `font-weight:600` left; actions right `display:flex; gap:12px; font-size:12px` | — |
| Scope | section labels `font-size:11px; letter-spacing:.08em; text-transform:uppercase; color:var(--color-neutral-600)`; rows `padding:5px 6px`; selected row `background:var(--color-accent-100)`; 14px check squares `border:1.5px solid var(--color-accent); border-radius:2px` | header actions `+ Folder` `⋯` · "Target folders" (`D:\Photos` + "2 sub", `D:\Backup\Photos`, unchecked `E:\OneDrive\Pictures` in `--color-neutral-500`) · "Include subfolders" as `.radio`+`.dot` with `border-radius:2px` · "Skip folders named" + `+ Add` + chips `node_modules` `.git` `bin` · "Match duplicates by" as `.seg`/`.seg-opt` **Content \| Name pattern** |
| Results header | 30px, `gap:8px` | "Duplicate groups" (600) · "3 of 3 · 41.2 MB reclaimable" (neutral-600) · right: `Expand all` `Collapse all` |
| Results toolbar | `height:36px; background:var(--color-neutral-100); font-size:12.5px` — **one row** | `Type [All ▾]` · `Copies ≥ [2]` (`width:22px`) · `Size ≥ [0 MB ▾]` · `Sort [Wasted space ↓ ▾]` — each a `border:1px solid var(--color-divider); border-radius:var(--radius-md); padding:2px 8px` pill — then a 1px×20px divider, then `Mark  All  Newer  Older  By rules  None` (None in neutral-600) |
| Column header | separate strip, `height:26px`, `grid-template-columns:28px 1fr 130px 90px 70px 90px; gap:10px`, 11px uppercase | ` · File / copy · Modified · Size (right) · Keep · ` |
| Group row | same 6-col grid, `height:32px`, `background:var(--color-surface)` | `▼` · **{name}** + `<span class="tag tag-accent">{ext}</span>` + "{n} copies · {size} each · {wasted} wasted" · right verbs **Preview · Recycle** |
| Copy row | same grid, `height:30px`, rule `color-mix(in srgb, var(--color-text) 6%, transparent)` | path (`padding-left:16px`, ellipsised) · date · size (right) · **Keep** = 15px circle `border:1.5px solid var(--color-text)` with a 7px inner dot · right verbs **Open · ⋯** |
| Action bar | `height:40px; background:var(--color-neutral-100); border-top:1px solid var(--color-divider)` — inside the centre column | "**4 marked**" · "27.5 MB · `will be recycled`" (that phrase in `--color-accent-2`) · right `btn-secondary` "Move to…" + `btn-primary` "Recycle 4 files" |
| Preview (right, top) | header actions `Auto ✓` `✕` | `.halftone` thumb `aspect-ratio:4/3` · "IMG_2041.jpg" (600) · metadata `display:grid; grid-template-columns:auto 1fr; gap:2px 14px` — Size / Pixels / Modified / Path |
| Rules (right, bottom) | **header 30px carries only `+ Rule` `Apply`**; a **second 32px toolbar row** below it carries `Enable all` `Disable all` + right-aligned "first match wins" (neutral-600) | rule rows `grid-template-columns:22px 18px 1fr auto` — checkbox · ordinal · sentence with the pattern in `ui-monospace` · `<span class="tag tag-neutral">` flag (`Aa` / `.*`). Row 1 "Never mark path containing `\Originals\`"; row 2 (disabled, neutral-500) "Mark name matching `^IMG_\d+ \(\d\)`". Compose row order: **`[Mark ▾] [name ▾] [input placeholder="text or /regex/"] [Add]`** |
| Status bar | `height:26px; background:var(--color-surface); border-top:1px solid var(--color-divider); gap:18px; font-size:12px; color:var(--color-neutral-600)` — **one, spanning the full width** | "Ready" · "↶ Undo recycle of 4 files, 10:12" · `margin-left:auto` "RAM 186 MB" · "CPU 0.4%" · "31 threads" |

## Canvas vs. epic brief — five recorded divergences

The planning brief paraphrased 3a; the markup above is authoritative. Each of these would produce a wrong build if
taken from the paraphrase.

1. **Three splitters, not two** — the right stack is `grid-template-rows:1fr 1px 1fr`, so Preview and Rules are
   separated by a horizontal splitter too. *(Affects `T-005` and `T-008`: three `GridSplitter`s, not two.)*
2. **Verbs are split across two row levels** — `Preview` + `Recycle` on the **group** row; `Open` + `⋯` on the
   **copy** row. Not four verbs per copy. *(Affects `T-007`.)*
3. **The Rules panel has a two-row head** — a 30px header (`+ Rule`, `Apply`) *plus* a 32px toolbar
   (`Enable all`, `Disable all`, "first match wins"). Not one four-action header. *(Affects `T-008`.)*
4. **Compose-row order is `Mark ▾`, `name ▾`, pattern input, `Add`** — the verb and field precede the pattern box.
   *(Affects `T-008`.)*
5. **The results head is two strips** — a 36px toolbar over a separate 26px column-header row. "One toolbar row" is
   true and does not mean one strip. *(Affects `T-007`.)*

## Deliberately in 3a, and easy to mistake for turn 4

3a itself uses `--color-accent` (cyan) for the active-tab rule, checkboxes and links; one `--color-accent-2`
(magenta) on the phrase "will be recycled"; `tag-accent` on the file-extension tag; and `--color-accent-100` on the
selected Scope row. **These are Broadsheet's own two accents used as the system already uses them — not turn 4's
colour-as-meaning scheme.**

Turn 4 is the different, deferred thing: a *theme switch*, a *semantic* cyan/magenta/print-yellow vocabulary,
*per-type* tinted tags, tinted selected rows, and duotone panel icons.

## How to read it

Open [`canvas/redesign-explorations.dc.html`](canvas/redesign-explorations.dc.html) in a browser and jump to the
adopted artboard with `#3a`.

> ⚠ **Network required.** Opening a `.dc.html` fetches, at open time:
> `https://unpkg.com/react@18.3.1/umd/react.production.min.js`,
> `https://unpkg.com/react-dom@18.3.1/umd/react-dom.production.min.js`,
> `https://unpkg.com/@babel/standalone@7.29.0/babel.min.js` (all three from `support.js`),
> `https://unpkg.com/@phosphor-icons/web@2.1.1/src/duotone/style.css`, and Source Serif 4 from
> `fonts.googleapis.com` / `fonts.gstatic.com` (the doc head and `styles.css`'s `@import`).
> **Offline, the page renders unstyled with a literal `{{ groups }}`.** `.thumbnail` is vendored as a low-fidelity
> fallback view of the canvas.

`{{ groups }}` is a runtime repeat: the three groups and two copies per group are `hint-placeholder-count` sample
data and **not** a specification of row counts.

## What this design does not govern

The scan algorithm (`SPEC-001`), duplicate matching, the settings schema, and recycle semantics are all untouched by
this record. It governs **frame, layout and chrome only.**

Realising it will change these specs: [`SPEC-004`](../specs/SPEC-004-selection-and-file-actions.md),
[`SPEC-005`](../specs/SPEC-005-file-preview.md),
[`SPEC-006`](../specs/SPEC-006-analytics-and-resource-monitor.md),
[`SPEC-007`](../specs/SPEC-007-folder-search.md),
[`SPEC-009`](../specs/SPEC-009-settings-and-window-state-persistence.md),
[`SPEC-010`](../specs/SPEC-010-contextual-help.md). The decision record is `ADR-012`.

## Provenance

Captured 2026-09-04 from `C:\Users\priva\Downloads\UI mockups project planning\` — **10 files, 343,869 bytes**,
vendored byte-for-byte into [`canvas/`](canvas/). The canvas is a **design-tool export, not hand-authored HTML**.

Rename map (content untouched — the digests below are of the vendored copies and match the sources exactly):

| Source | Vendored as |
|---|---|
| `Redesign Explorations.dc.html` | `canvas/redesign-explorations.dc.html` |
| `Baseline - Current WPF UI.dc.html` | `canvas/baseline-current-wpf-ui.dc.html` |
| everything else | same relative path under `canvas/` |

The `_ds/broadsheet-20e204aa-d8f4-4c1f-9771-d781d63fcc9d/` directory **keeps its UUID name**: both `.dc.html` files
reference it by that literal path, and renaming it silently unstyles both artboards.

| sha256 | Bytes | Path under `canvas/` |
|---|---:|---|
| `5bc2c1dbbec50dc98df80e36da5d6f6e916c1d2c23e19b98b72882437d74bb58` | 114,880 | `redesign-explorations.dc.html` |
| `43bf8df92076c1d2a362b4783f570f17c287988f874eda10decbec29c2c1cf73` | 71,549 | `baseline-current-wpf-ui.dc.html` |
| `8fe7df74405f3c55f49b7249c74ea1397e65d07dea2b1bd3b4a489bec2e28cbe` | 69,150 | `support.js` |
| `189cb6363a4e1a68d7b0f4841d7715abea29f4842a41ab0ba367c7345652115f` | 28,258 | `.thumbnail` (WebP, 640×513) |
| `03741248b7767ec0be2d8e29721c4452f414657f63f85658835852cae68f9741` | 19,645 | `_ds/broadsheet-20e204aa-…/styles.css` |
| `8937c1152b00ebb3189bd556225ddc50741cc0ed3ad42be91b6409770733b636` | 13,805 | `_ds/broadsheet-20e204aa-…/_ds_bundle.js` |
| `72956a6ef0f4a6be76e346572e9f9406f2e998359d9259bef05f8d5507a9817b` | 8,691 | `_ds/broadsheet-20e204aa-…/readme.md` |
| `d3e6ce8145fb06dc41640d3d38f91ddd5493c101b272b99b45006f918b4c52dd` | 7,572 | `_ds/broadsheet-20e204aa-…/_ds_manifest.json` |
| `57403b760d9aec43320ae2ed2d0edcb482e8c5029584087b3e941d7872df973d` | 4,361 | `_ds/broadsheet-20e204aa-…/_adherence.oxlintrc.json` |
| `0190e3d3c4ebfe914550c40c41a9d8998253da791b055778ce74fd8e09a28adb` | 5,958 | `assets/app-icon.png` |

Byte fidelity is enforced by `.gitattributes` (`docs/design/canvas/** -text`) because this repo runs
`core.autocrlf=true`; without it a fresh clone would check out CRLF and every digest above would fail to reproduce.
`.editorconfig` carries a matching `[docs/design/canvas/**]` block so no editor or agent "tidies" a frozen artifact.
