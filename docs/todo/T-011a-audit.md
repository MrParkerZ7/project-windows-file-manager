# T-011a — Contrast audit, token role list, keyboard navigation model

> **Status:** delivered 2026-09-04 · **Fork 1 answered: option A** (take the darker ramp steps)
> **Consumed by:** `T-001` (palette values + key names) · `T-003` (focus visuals) · `T-011` (implementation)
>
> This is the Wave-0 analysis that had to land **before** T-001 freezes the token dictionary. It is
> analysis, not design intent — the frozen design record is
> [`docs/design/DESIGN-001-ide-frame-redesign.md`](../design/DESIGN-001-ide-frame-redesign.md) and is not
> superseded by anything here.

---

## 1. Contrast audit

Measured against [WCAG 2.2 AA](https://www.w3.org/TR/WCAG22/) — 4.5:1 for body text
([SC 1.4.3](https://www.w3.org/TR/WCAG22/#contrast-minimum)), 3:1 for large text and non-text UI
([SC 1.4.11](https://www.w3.org/TR/WCAG22/#non-text-contrast)). Ratios computed from the vendored
`docs/design/canvas/_ds/broadsheet-…/styles.css` against the pairs the **3a artboard actually specifies**.

### 1a. As drawn

| Pair | Foreground | On | Ratio | Bar | Verdict |
|---|---|---|---:|---:|:--|
| Body text | `text` #201e1d | page #f3f2f2 | 14.86 | 4.5 | ✅ pass |
| Body text | `text` #201e1d | surface #eae9e9 | 13.70 | 4.5 | ✅ pass |
| **Secondary text** | `neutral-600` #7d7979 | page #f3f2f2 | **3.85** | 4.5 | ❌ **fail** |
| **Secondary text** | `neutral-600` #7d7979 | surface #eae9e9 | **3.55** | 4.5 | ❌ **fail** |
| **Disabled / unchecked row** | `neutral-500` #9b9797 | page #f3f2f2 | **2.59** | 4.5 | ❌ **fail** |
| **Link / active-tab label** | `accent` #0088b0 | page #f3f2f2 | **3.65** | 4.5 | ❌ **fail** |
| "will be recycled" | `accent-2` #d6006c | neutral-100 #f8f4f4 | 4.72 | 4.5 | ✅ pass |
| Active-tab 2px underline *(non-text)* | `accent` #0088b0 | page #f3f2f2 | 3.65 | 3.0 | ✅ pass |
| Checkbox border *(non-text)* | `accent` #0088b0 | page #f3f2f2 | 3.65 | 3.0 | ✅ pass |

### 1b. The regression finding — why Fork 1 mattered

Two failures are not merely below the bar; they are **regressions against the shipping app**, in its two most
pervasive text roles.

| Role | Ships today | 3a as drawn | Net |
|---|---:|---:|:--|
| Secondary text | **5.74** (#666666 on white) ✅ | **3.85** ❌ | **pass → fail** |
| Link / accent text | **5.75** (#1565C0 on white) ✅ | **3.65** ❌ | **pass → fail** |
| Disabled / muted | 3.54 ❌ | 2.59 ❌ | already failing, made worse |

In 3a, `neutral-600` carries the profile switcher, the inactive tabs, every section label, the results count
("3 of 3 · 41.2 MB reclaimable"), the "first match wins" caption, and the entire status bar.

### 1c. Applied fixes — Fork 1 = option A

Every value is a **real Broadsheet ramp step** from the same ramp the artboard already uses. No colour is invented.
`neutral-700` is not even new to 3a — it already appears in the artboard.

| Role | Was | **Ships as** | Ratio | Result |
|---|---|---|---:|:--|
| Secondary text | `neutral-600` #7d7979 | **`neutral-700` #605d5d** | **5.83** on page · 5.38 on surface | ✅ also beats today's 5.74 |
| Disabled / unchecked row | `neutral-500` #9b9797 | **`neutral-700` #605d5d** | **5.83** | ✅ |
| Link / active-tab **label** | `accent` #0088b0 | **`accent-700` #006786** | **5.72** | ✅ |
| Active-tab underline, checkbox border *(non-text)* | `accent` #0088b0 | **unchanged** | 3.65 | ✅ already passes the 3:1 bar |

❗ **`accent` is retained.** Only its *text* use moves to `accent-700`. The 2px tab underline and the checkbox
borders keep `accent` — they clear the non-text 3:1 bar, and darkening them would visibly change the design for
no accessibility gain.

---

## 2. Token role list

**Derived from the artboard, not asserted.** The 3a block uses **14 distinct design tokens** — 12 colour, 1 radius,
1 font family.

> ⚠ **The ticket said "sixteen role keys". That figure was written at plan time and is wrong** — the real count is
> 14 as drawn, and **15** once option A adds `accent-700`. `neutral-700` was already in use. T-001 should publish
> the list below, not a count.

| # | Key T-001 publishes | Value | Role in 3a | Res. |
|---|---|---|---|:--|
| 1 | `Broadsheet.Brush.Text` | #201e1d | body text | Static |
| 2 | `Broadsheet.Brush.Bg` | #f3f2f2 | page background | Dynamic |
| 3 | `Broadsheet.Brush.Surface` | #eae9e9 | title bar, tool-window/group-row surface | Dynamic |
| 4 | `Broadsheet.Brush.Neutral100` | #f8f4f4 | results toolbar, action bar band | Dynamic |
| 5 | `Broadsheet.Brush.Neutral300` | #d7d3d3 | subtle borders | Dynamic |
| 6 | `Broadsheet.Brush.Neutral500` | #9b9797 | *(retained for non-text only — see note)* | Dynamic |
| 7 | **`Broadsheet.Brush.Neutral600`** | #7d7979 | **non-text only after option A** | Dynamic |
| 8 | **`Broadsheet.Brush.Neutral700`** | #605d5d | **secondary + disabled text** | Dynamic |
| 9 | `Broadsheet.Brush.Accent` | #0088b0 | tab underline, checkbox borders *(non-text)* | Dynamic |
| 10 | **`Broadsheet.Brush.Accent700`** | #006786 | **link / active-tab label text** | Dynamic |
| 11 | `Broadsheet.Brush.Accent100` | #e9f8ff | selected Scope row wash | Dynamic |
| 12 | `Broadsheet.Brush.Accent2` | #d6006c | the "will be recycled" phrase (one use) | Dynamic |
| 13 | `Broadsheet.Brush.Divider` | see § 2a | every hairline and both splitters | Dynamic |
| 14 | `Broadsheet.Radius.Md` | 2px (`CornerRadius`) | pills, chips, checkboxes | Static |
| 15 | `Broadsheet.FontFamily.Heading` | *see T-001 DECISION 1* | title bar, tabs, card titles | Dynamic |

**Naming rule — enforced.** Keys are named by **ramp role**, never by semantics. `Broadsheet.Brush.Accent2`, never
`Destructive`. A semantically-named colour key is the first row of the deferred 4a layer, and the coherence pass
caught exactly that leaking in from T-011.

**Static vs Dynamic.** Everything a future theme (4a) would swap is `DynamicResource`; only `Text` and `Radius.Md`
are safe as `StaticResource`. Getting this wrong is cheap now and ruinous later — it is the single thing in T-001
that would force a rewrite rather than a dictionary swap.

### 2a. `--color-divider` needs a decision T-001 must make explicitly

The token is `color-mix(in srgb, #201e1d 16%, transparent)`. **WPF has no `color-mix`**, so T-001 must pick one:

| Option | Value | Note |
|---|---|---|
| **Alpha brush (recommended)** | `#29201E1D` | one key, composites correctly over *any* background — matters because the same hairline runs over page, surface **and** neutral-100 |
| Baked opaque | `#D1D0D0` over page · `#CAC9C8` over surface · `#D5D2D2` over neutral-100 | three keys, and each is only correct on its own background |

Measured contrast of the composited hairline against its own background is **1.36–1.38**. That is *below* 3:1, and
is **acceptable**: SC 1.4.11 governs boundaries needed to *identify* a control, not decorative separators. The
splitters are the exception — see § 3.

---

## 3. Keyboard navigation model

The app has **zero `GridSplitter`s today**; all **three** in 3a are new construction (two vertical in the body grid,
one horizontal in the right stack — per DESIGN-001 § Inventory).

### 3a. Tab order

Linear, following visual reading order. One tab stop per region, then arrow/`Tab` within it.

```
1  title bar        profile switcher → window controls
2  tab strip        Duplicates / Folders / History (← → moves, does not activate until Space/Enter)
3  Scan button      the primary action, reachable without entering any panel
4  Scope panel      header actions (+ Folder, ⋯) → target list → Include subfolders
                    → Skip-folders chips → Match-duplicates-by segmented control
5  splitter A       (vertical, Scope ‖ results)
6  results toolbar  Type → Copies ≥ → Size ≥ → Sort → Mark: All/Newer/Older/By rules/None
7  results list     group rows; ← → expand/collapse, ↑ ↓ move, Space toggles Keep
8  action bar       Move to… → Recycle N files
9  splitter B       (vertical, results ‖ right stack)
10 Preview panel    header actions (Auto ✓ ✕)
11 splitter C       (horizontal, Preview ‖ Rules)
12 Rules panel      + Rule / Apply → Enable all / Disable all → rule rows → compose row
13 status bar       the single Undo affordance — reachable, not a dead end
```

### 3b. Splitter accessibility — the part with no precedent in this codebase

A `GridSplitter` is focusable and arrow-key resizable in WPF **by default**, but two things must be added:

- **`AutomationProperties.Name`** on each — "Scope panel width", "Preview panel width", "Preview and Rules split".
  Unnamed splitters are announced as bare "separator" and are unusable with a screen reader.
- **A visible focus adornment.** A 1px splitter with the default dotted focus rectangle is effectively invisible.
  T-003 must give the focused state a **3px `accent` band** (not `accent-700` — this is non-text, and `accent`
  already clears 3:1).

### 3c. Focus-visual contract for T-003

| Control | Focus visual | Contrast requirement |
|---|---|---|
| Buttons (primary/secondary/ghost) | 2px `accent` outline, offset 1px | 3:1 vs adjacent — `accent` on page = 3.65 ✅ |
| Checkboxes / Keep control | 2px `accent` ring around the 14–15px box | 3.65 ✅ |
| Segmented control options | 2px `accent` inset outline on the focused segment | 3.65 ✅ |
| Text inputs | 1px border → `accent`, plus a 2px outer ring | 3.65 ✅ |
| Tab strip items | the existing 2px underline **thickens to 3px** on focus | 3.65 ✅ |
| List rows | `accent-100` wash + a 2px `accent` left edge | wash is decorative; the edge carries the signal |
| Splitters | 3px `accent` band (see § 3b) | 3.65 ✅ |

**Never signal focus by colour alone** — every row above changes shape or thickness too, so the design survives
greyscale and the B/W-print bar the brief standard asks for.

---

## 4. What this audit does not cover

- **States the artboard does not draw** — hover, pressed, selected-and-disabled, error/validation. Nobody has
  chosen colours for these; they are a real gap for T-003, not an omission here.
- **`process-yellow`** (#edbb00, 1.61 on the page background) — measured, but **no text use of it exists in the
  3a artboard**. It is turn-4 vocabulary. Not in the shipped role list.
- **Implementation.** Focus visuals, `AutomationProperties` and the manual keyboard pass are `T-011`.

## 5. Open questions carried forward to T-001 / T-003

1. Is 3a's page background (#f3f2f2) final? Every ratio here moves if it changes.
2. T-001 DECISION 1 (the Broadsheet typeface) is still open with a recommended default of Cambria — it does not
   block this audit, but it does decide `Broadsheet.FontFamily.Heading`'s value.
3. The hover/pressed/error states above need colours chosen before T-003 can write the component library.
