# Design records

Frozen design-time intent — what a change was **meant** to be, captured before it was built.

- Structure: `shape: flat` · `numbering: global-adoption-order` · `entry: DESIGN-NNN-<slug>.md`
- Supporting artifacts for a record live beside it in their own folder (here, [`canvas/`](canvas/)).

## The map

| # | Record | Status | Adopted | Artifact |
|---|--------|--------|---------|----------|
| DESIGN-001 | [IDE-frame redesign (option 3a)](DESIGN-001-ide-frame-redesign.md) | Adopted | 2026-09-04 | [`canvas/`](canvas/) |

## The freeze contract

> A design record is **frozen at adoption**. It records what was intended *before* the build. It is never edited to
> match what shipped — that is what the owning `SPEC-NNN` is for. If the build deviates from the design, the
> deviation is recorded in the spec (and, if load-bearing, an ADR); the design record gains at most a
> `Superseded by DESIGN-NNN` line. **`docs/design/` is outside the sync surface of any docs-refresh pass,
> `perfect-docs` included.**

This is a rule, not a preference. A design record that has been "corrected" to match the code has been destroyed: it
no longer answers the only question it exists to answer.

## Design vs. ADR vs. spec

The three frozen-or-living kinds are easy to confuse, and each owns a different question at a different moment.
A **design** is frozen *intent*, captured **before** the build: what we meant the thing to look like and do. An
**ADR** is a frozen *decision*, captured **at acceptance**: why we chose this approach over the alternatives, and
what it costs. A **spec** is *living truth*, synced **in the same commit** as the behaviour it describes: what the
feature does today. Design seeds the first spec; the spec then diverges from it as reality lands, and that divergence
is normal and is recorded in the spec — not by rewriting the design.

## The `_ds/` caveat

[`canvas/_ds/broadsheet-20e204aa-…/`](canvas/_ds/) is **the subset of the Broadsheet design system the canvas
loads**, not the whole system. Its `readme.md` and `_ds_manifest.json` reference `components/*.html`, `templates/`,
`theme.json` and `print-plates.js` that were **not** exported. Do not chase those links — they were never here. Do
not edit these files: they are part of the frozen artifact, and `styles.css` is the source the WPF design tokens
(`T-001`) are translated from.

## How to open the canvas

Open [`canvas/redesign-explorations.dc.html`](canvas/redesign-explorations.dc.html) in a browser; jump to the adopted
artboard with `#3a`.

> ⚠ **Network required.** Opening a `.dc.html` fetches, at open time:
> `https://unpkg.com/react@18.3.1/umd/react.production.min.js`,
> `https://unpkg.com/react-dom@18.3.1/umd/react-dom.production.min.js`,
> `https://unpkg.com/@babel/standalone@7.29.0/babel.min.js` (all three from `support.js`),
> `https://unpkg.com/@phosphor-icons/web@2.1.1/src/duotone/style.css`, and Source Serif 4 from
> `fonts.googleapis.com` / `fonts.gstatic.com` (the doc head and `styles.css`'s `@import`).
> **Offline, the page renders unstyled with a literal `{{ groups }}`.** `canvas/.thumbnail` is vendored as a
> low-fidelity fallback view.

Adding a record adds its row to the map above in the same commit.
