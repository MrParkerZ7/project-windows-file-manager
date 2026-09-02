# SPEC-010 — Contextual help

<!-- Sync contract: `## Current behavior & invariants` is the section a behavior-changing commit must update in the same commit. -->

- Status: Current
- Feature area: shell/UI
- Ships in: **1.0.0** — *"Contextual help buttons (`?` popups) with rich text and clickable links"* is a 1.0.0 CHANGELOG line, delivered by `758e3ed` (2026-04-09, the buttons) and `cc02c3b` (2026-04-15, the link tag). Popups for features added later (folder search, match types, search depth, the folder actions, match-by-name-regex) ship with those features in **Unreleased**.

## What

Next to most section headers and several individual controls sits a small blue **?** circle. Clicking it opens a yellow popup under the control explaining what that section does, in formatted text — bold terms, blue highlighted sub-headings, red warning lines, and clickable links. Clicking anywhere outside closes it; clicking the **?** again toggles it shut.

There are **21 such popups** today, covering the profile bar, the target/exclude folder lists, the folder-search toolbar, the duplicate filters and rule builder, the file actions, and the folder-action panel.

The text of each popup is not code — it is the `Tag` string on the button, written in a tiny markup grammar (`<b>`, `<h>`, `<w>`, `<link=…>`) that a WPF attached behavior turns into styled inline runs.

## Why

The app's controls are dense and several of them are genuinely non-obvious: what "Contains" means as a folder match type, why regex mode ignores file size, what a filter rule's priority does, what a "flatten" actually moves. Tooltips are too small for that, and a separate manual would go unread and out of date.

Putting the explanation one click from the control keeps it where the question is asked. Making the text pure data on the control — rather than strings in code — means adding help is a XAML-only edit with no new code, which is why the coverage grew from a handful of popups to 21 without a single change to the parser.

## Scope

### In

- The `?` button visual, its toggle behavior, and the popup container.
- The markup grammar and how it is parsed into WPF inlines.
- Link activation.
- The inventory of popups that exists today and where each lives.

### Out

- The *accuracy* of any individual popup's wording. Each popup describes a feature owned by another spec; when behavior changes, that feature's spec is the contract and the popup text is a consumer of it.
- Regular tooltips (`ToolTip="…"`), which are plain-text WPF defaults used throughout and have no relationship to this feature.
- Status-line and dialog text (`StatusMessage`, `ClearSubfolderStatus`, `MessageBox` confirmations) — those are per-feature.
- Any help *system* beyond popups: there is no F1 handler, no searchable help, no in-app manual, and no per-control discovery affordance beyond the visible `?` circles.

## Current behavior & invariants

**Entry points**

| Trigger | Handler | Notes |
|---------|---------|-------|
| A `?` circle (`ToggleButton` with `Style="{StaticResource HelpButtonStyle}"`) | The style's `ControlTemplate` (`Views/MainWindow.xaml`, in `Window.Resources`) | the template's `Popup.IsOpen` is bound to the button's own `IsChecked` |
| Popup body | `TextBlock` with `helpers:FormattedTextBehavior.FormattedText="{TemplateBinding Tag}"` | the **only** use of the behavior in the application |
| Markup parse | `FormattedTextBehavior.OnFormattedTextChanged` → `ParseAndApply(TextBlock, string)` | attached `DependencyProperty`, `static`, `[ExcludeFromCodeCoverage]` |
| Inline construction | `AddPlainText` · `AddStyledRun` · `AddHyperlink` | private statics |
| Link click | `Hyperlink.Click` handler in `AddHyperlink` | `Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })`, all exceptions swallowed |

**Rules**

1. **The button.** 16 × 16, `CornerRadius = 8`, background `#E3F2FD` with border `#90CAF9`, a bold `?` glyph in `#1565C0`, hand cursor, `VerticalAlignment = Center`. Hover repaints the circle `#BBDEFB`; the checked state repaints it `#1565C0` with border `#0D47A1`. It is a `ToggleButton`, so a second click closes the popup.
2. **The popup.** `Placement = Bottom`, `StaysOpen = False` (an outside click dismisses it), `AllowsTransparency = True`, `PopupAnimation = Fade`. The body is a `Border` — background `#FFFDE7`, border `#FFD54F`, `CornerRadius = 6`, `Padding = 12,10`, `MaxWidth = 380`, drop shadow (`Opacity 0.15`, `BlurRadius 8`, `ShadowDepth 2`) — wrapping a `TextBlock` at `FontSize = 11.5`, `LineHeight = 18`, `Foreground = #333333`, `TextWrapping = Wrap`.
3. **Content is data on the control.** Each popup's text is the `ToggleButton`'s `Tag`. Adding a popup means adding a styled `ToggleButton` with a `Tag`; no code changes and no registration.
4. **The grammar.** Four tags, all parsed by `FormattedTextBehavior`:

    | Markup | Renders as |
    |--------|-----------|
    | `<b>text</b>` | `FontWeight = Bold` |
    | `<h>text</h>` | `FontWeight = SemiBold`, `Foreground = #0D47A1` — used for the popup's leading title line and for sub-headings |
    | `<w>text</w>` | `FontWeight = SemiBold`, `Foreground = #C62828` on background `#FFEBEE` — the warning style |
    | `<link=URL>text</link>` | `Hyperlink`, `Foreground = #1565C0`, underlined, hand cursor |

5. **Newlines.** A literal `\n` in the string becomes a `LineBreak`, both in plain text and inside a styled run. In XAML the text is an attribute value, so newlines are written as `&#x0a;` and `<`, `>`, `&`, `"` must be XML-escaped (`&lt;b&gt;` … ).
6. **Parsing is a single forward scan.** From the current position: find the next `<`; everything before it is emitted as plain text. Find the matching `>`; the text between them is the tag.
7. **Recognized tag → matched close tag.** For `b`, `h`, `w` the parser searches for `</b>` / `</h>` / `</w>` (`StringComparison.Ordinal`) after the open tag and emits the content between them as a styled run; for a tag starting with `link=` the URL is everything after `link=` and the close tag is `</link>`. Scanning resumes after the close tag.
8. **Unclosed tag → literal remainder.** If the close tag is not found, everything from the opening `<` to the end of the string is emitted as plain text and parsing **stops**.
9. **`<` with no `>` → literal remainder,** and parsing stops.
10. **Unknown tag → literal tag.** Anything that is not `b`/`h`/`w`/`link=…` (including every close tag reached out of context, e.g. a stray `</i>`) is emitted verbatim, including its angle brackets, and scanning continues after it.
11. **No nesting.** A styled tag's content is taken verbatim up to its close tag and rendered as one run; an inner tag inside it is not parsed and shows as literal text.
12. **Empty segments produce no `Run`.** Splitting on `\n` emits a `Run` only for non-empty parts, but always emits the `LineBreak` between parts — so consecutive newlines render as blank lines.
13. **Re-assignment re-parses.** `OnFormattedTextChanged` calls `TextBlock.Inlines.Clear()` first; a `null` or empty value leaves the block empty. A non-`TextBlock` target is a no-op.
14. **Link activation.** The click handler launches the URL with `UseShellExecute = true`, so the system default handler opens it. Any exception is swallowed — a bad or unlaunchable URL does nothing visible.
15. **The popup surface today (21 popups, all in `Views/MainWindow.xaml`):**

    | Area | Popups |
    |------|--------|
    | Shared header (above the tabs) | `PROFILES` · `WHERE TO SCAN` · `EXCLUDE FOLDER NAMES` |
    | **Folder** tab | `FOLDER SEARCH` · `MATCH TYPES` · `SEARCH DEPTH` · `SEARCH` |
    | **Duplication** tab | `SCAN & DISPLAY OPTIONS` · `BASE FILTERS` · `FILTER BY FILE TYPE` · `FILTER BY SIZE & COUNT` · `MATCH BY NAME REGEX` · `CUSTOM FILTER RULES` · `REGEX (Regular Expression)` · `IGNORE CASE` · `TAKE ACTION on selected files` |
    | Folder Action side panel | `SCAN FOLDERS` · `CLEAR SELECTED SUBFOLDERS` · `LINK SIBLING FOLDERS` · `CLEAR SELECTED FILES` · `MOVE FILES TO ROOT` |

    The **History** tab has no popups. Every popup's text opens with an `<h>…</h>` title line by convention; the longest is `MATCH TYPES` at 903 characters.
16. **Exactly one link exists.** The `REGEX (Regular Expression)` popup ends with `<link=https://regex101.com>regex101.com — test & learn regex interactively</link>`.

**Invariants**

- Help content is data, never code. Nothing in `MainViewModel` knows the help system exists.
- The parser never throws: it only slices strings and constructs inlines, and every `IndexOf` result is bounds-checked before use. Malformed markup degrades to literal text rather than failing.
- Every popup is independent — each `?` owns its own `Popup` instance through the template, and opening one does not close another; `StaysOpen = False` closes on an outside click.
- Because `Popup.IsOpen` is bound to `IsChecked`, the button's visual state and the popup's visibility can never disagree.
- The four styling colors are fixed in code (`#0D47A1` highlight, `#C62828` on `#FFEBEE` warning, `#1565C0` link) and are not themeable.
- `FormattedTextBehavior` is `[ExcludeFromCodeCoverage]`, so none of the grammar is exercised by the 100 % coverage gate ([ADR-011](../adr/ADR-011-coverage-via-collector-and-script.md)).

**Edge cases**

| Case | Behavior |
|------|----------|
| `<b>text` (no close tag) | `"<b>text"` renders literally; everything after it is dropped from parsing |
| `Some <b>bold` after other markup | The already-parsed prefix keeps its formatting; the unclosed tail is literal |
| `<b><h>x</h></b>` | The bold run's content is the literal string `"<h>x</h>"` — inner tags are not parsed |
| `<i>text</i>` | All three fragments render as literal text; scanning continues, so later valid tags still work |
| `<link=notaurl>click</link>` | Renders as a link; clicking it does nothing (the `Process.Start` exception is swallowed) |
| `Tag` is empty or unset | Inlines cleared; the popup opens showing an empty bordered box |
| Text longer than the popup | Wraps at `MaxWidth = 380` and grows downward; there is no scrollbar or height cap |
| Two `?` buttons clicked in turn | The first closes on the outside click that lands on the second |
| Literal `<` needed in help text | Must be written `&lt;` in XAML; an unescaped `<` is a XAML parse error, not a runtime one |

**Not implemented**

- **Nothing keeps a popup honest.** Help text has no test, no link to the code it describes, and no review gate — and it has drifted:
  - `TAKE ACTION on selected files` states *"Permanently removes checked files from disk. `<w>`No Recycle Bin — cannot be undone.`</w>`"*, but every delete path recycles and pushes an undoable history entry ([SPEC-004](SPEC-004-selection-and-file-actions.md)).
  - `FOLDER SEARCH` states *"Matching: Case-insensitive contains match"*, which describes the feature before match types existed ([SPEC-007](SPEC-007-folder-search.md)).
  - `MATCH TYPES` documents five of the six match types — `NotContain` is implemented and selectable but undocumented.
- **No tests.** The grammar's behavior — including the degradation rules above — is pinned by nothing.
- **No nesting, and no way to escape a literal `<` inside help text at runtime.** A `<` that is not a recognized tag is emitted verbatim, which happens to work, but there is no escape syntax.
- **No URL allow-list.** `AddHyperlink` shell-executes whatever string the `Tag` supplies; the parser does not require `http`/`https`. Only app-authored text reaches it today, but nothing enforces that — see [`../SECURITY.md`](../SECURITY.md).
- **Not localizable.** Every string is hard-coded English inside a XAML attribute; there is no resource file and no `x:Uid` usage.
- **No global help affordances.** No F1 key handler, no help menu, no "show all help" mode, no search across popups, and no way to keep a popup pinned open while interacting with the control it describes.

## Links

- Decisions: [ADR-002 — Hand-rolled MVVM](../adr/ADR-002-hand-rolled-mvvm.md) · [ADR-010 — WPF on .NET 8 for the desktop shell](../adr/ADR-010-wpf-net8-desktop-shell.md) · [ADR-011 — coverage measured by coverlet.collector, enforced by script](../adr/ADR-011-coverage-via-collector-and-script.md)
- Module docs: [WindowsFileManager (WPF UI)](../modules/ui.md)
- Related specs: [SPEC-007 — Folder search](SPEC-007-folder-search.md) · [SPEC-008 — Clear subfolders](SPEC-008-clear-subfolders.md) · [SPEC-003 — Custom filter rules](SPEC-003-custom-filter-rules.md) · [SPEC-004 — Selection and file actions](SPEC-004-selection-and-file-actions.md)
- Background: [`../CONTEXT.md`](../CONTEXT.md) · [`../SECURITY.md`](../SECURITY.md)
- Tests: none — `WindowsFileManager.Helpers.FormattedTextBehavior` and `Views/MainWindow.xaml` are outside the coverage boundary
