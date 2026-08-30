# SPEC-003 — Custom filter rules

<!-- Sync contract: `## Current behavior & invariants` is the section a behavior-changing commit must update in the same commit. -->

- Status: Current
- Feature area: duplicate scanning (selection)
- Ships in: **1.0.0** — rule builder, plain-text and regex matching, case toggle, filename/filepath target, priority ordering. Per-rule enable/disable (`FilterRule.IsEnabled`) and the ✓ All / ✕ None bulk toggles ship in **Unreleased**: `IsEnabled` does not exist on `FilterRule` at the 1.0.0 baseline commit `53bfad1` and arrives with commit `fab9c91`.

## What

The **Custom Rules** bar lets the user write ordered pattern rules that decide, for every file in every duplicate group, whether its checkbox ends up ticked. A rule reads as a sentence: *`Include` on `Filename` matching `IMG_`* — with two option toggles (`.* Regex`, `Aa` ignore-case) and a **+ Add** button (or the Enter key in the pattern box).

Added rules appear as chips, each carrying an enable checkbox, its priority number, ▲/▼ reorder arrows and a remove button. **▶ Apply** runs the whole list against the current results; **✓ All** / **✕ None** enable or disable every rule at once.

Rules do not hide anything and do not delete anything. They only set the per-file selection checkbox that the delete/move actions later read.

## Why

The hard part of de-duplicating is not *finding* the copies — it is saying which copy of each pair to keep, several hundred times. That decision is almost always expressible as a pattern the user already knows: everything under `\Originals\` is sacred; anything whose name contains `(1)` or `- Copy` is the redundant one; the `_thumb` variants can all go.

Rules turn that knowledge into one statement applied uniformly, which is both faster and far less error-prone than clicking through groups. Their second job is as a guard rail: `Exclude` rules are re-applied automatically after the bulk selection commands, so a "select all the newer copies" sweep can never re-tick a file the user has declared off-limits.

## Scope

### In

- The `FilterRule` model — pattern, action, target, regex flag, ignore-case flag, enabled flag, positional priority, display summary.
- The rule builder (add, remove, reorder, enable/disable, clear all) and its command availability.
- The matching primitive: plain substring vs regex, case-sensitive vs not, against file name vs full path.
- **▶ Apply** — how the ordered rule list resolves to a per-file selection state.
- The `Exclude`-rule post-pass that runs after the bulk selection commands.
- How rules are persisted per profile and what survives an old `settings.json`.

### Out

- Hiding or re-ordering duplicate groups — [SPEC-002](SPEC-002-filtering-and-sorting.md). Those filters change *visibility*; rules change *selection*. The two mechanisms never read each other.
- The Select All / Select Newer / Select Older commands themselves, and everything that consumes the selection (delete, move, reveal) — [SPEC-004](SPEC-004-selection-and-file-actions.md). This spec owns only the `Exclude` post-pass those commands call into.
- The scan-time `MatchRegex` that decides what a duplicate *is* — [SPEC-001](SPEC-001-duplicate-detection.md). Different regex, different stage, different purpose: that one groups files, these ones tick checkboxes.
- Folder-search patterns (`FolderSearchPattern` / `FolderMatchType`) — [SPEC-007](SPEC-007-folder-search.md). Similar shape, different model and different semantics (AND-combined, six match types).
- Where the rules are written to disk — [SPEC-009](SPEC-009-settings-and-window-state-persistence.md).

## Current behavior & invariants

> The `FilterRule` model lives in `WindowsFileManager.Core` and **is** covered by unit tests (named below). The rule *engine* — add/reorder/apply/ignore-pass — lives in `MainViewModel`, which carries `[ExcludeFromCodeCoverage]` and therefore has **no unit tests**. Verify engine changes by hand.

**Entry points**

| Trigger | Handler | Availability |
|---------|---------|--------------|
| **+ Add** (`AddFilterRuleCommand`), or Enter in the pattern box via `TextBoxEnterKeyBehavior.Command` | `MainViewModel.AddFilterRule()` | enabled while `RulePatternText` is non-whitespace |
| **▶ Apply** (`ApplyFilterRulesCommand`) | `MainViewModel.ApplyFilterRules()` | enabled while `DuplicateGroups.Count > 0` |
| **✓ All** / **✕ None** (`EnableAllRulesCommand` / `DisableAllRulesCommand`) | `SetAllRulesEnabled(bool)` | enabled while `FilterRules.Count > 0` |
| Clear all (`ClearAllRulesCommand`) | `ClearAllRules()` | enabled while `FilterRules.Count > 0` |
| ▲ / ▼ on a chip (`MoveFilterRuleUpCommand` / `MoveFilterRuleDownCommand`, `CommandParameter="{Binding}"`) | `MoveFilterRuleUp/Down(FilterRule?)` | always; no-ops at the ends |
| ✕ on a chip (`RemoveFilterRuleCommand`) | `RemoveFilterRule(FilterRule?)` | always |
| Chip checkbox | two-way bound to `FilterRule.IsEnabled` | always |
| Select All / Newer / Older (SPEC-004) | `MainViewModel.ApplyIgnoreRules()` post-pass | automatic |
| Matching primitive | `static MainViewModel.MatchesFilter(input, filter, useRegex, ignoreCase)` | — |

**The rule model** (`WindowsFileManager.Core.Models.FilterRule`)

| Member | Type | Default | Notes |
|--------|------|---------|-------|
| `Pattern` | `string` | `""` | substring or regex source, depending on `IsRegex` |
| `Action` | `FilterAction` | `Include` | `Include = 0`, `Exclude = 1` |
| `Target` | `FilterTarget` | `Filename` | `Filename = 0` (matches `ScannedFile.FileName`), `Filepath = 1` (matches `FilePath`) |
| `IsRegex` | `bool` | `false` | |
| `IgnoreCase` | `bool` | `true` | |
| `IsEnabled` | `bool` | `true` | the **only** change-notifying property on the type; raises `PropertyChanged` only on an actual change |
| `Priority` | `int` | `0` | `[JsonIgnore]` — display only, derived from list position |
| `DisplaySummary` | `string` | computed | `[JsonIgnore]` — `"{Action} \| {Target} \| \"{Pattern}\""`, plus `" [Regex, IgnoreCase]"` when either flag is set |

Defaults and the summary format are pinned by `FilterRuleTests.Constructor_ShouldSetDefaults`, `DisplaySummary_IncludeWithFlags_ShouldFormat`, `DisplaySummary_ExcludeNoFlags_ShouldFormat`.

**Rules**

1. **Add trims, guards, appends, then resets the builder.** `AddFilterRule` trims `RulePatternText`; an empty result returns without adding (a second guard behind the command's `CanExecute`). Otherwise a new `FilterRule` is appended from the builder's five values, `RefreshRulePriorities()` runs, and the builder resets to its defaults — pattern `""`, `IsRegex = false`, `IgnoreCase = true`, `Action = Include`, `Target = Filename`. Status becomes `"Added filter rule #N: <DisplaySummary>"` and `SaveSettings()` runs.
2. **Priority is purely positional.** `RefreshRulePriorities()` assigns `Priority = index + 1` across the whole collection and is called after every add, remove, move, clear, and after a profile is applied. Priority 1 is the first chip and the highest priority. `Priority` is never serialized; ordering survives on disk only as the order of the JSON array.
3. **Reordering is a `Move`, not a re-sort.** ▲ swaps toward index 0 only when `index > 0`; ▼ only when `index < Count - 1`; both then re-number and `SaveSettings()`. At the ends they do nothing at all — no wrap, no error.
4. **Remove and Clear both re-number and save.** Remove sets status `"Removed filter rule."`; Clear sets `"All filter rules cleared."`.
5. **▶ Apply resolves the list first-match-wins, in priority order.**
   - If `FilterRules` is empty, status becomes `"No filter rules to apply. Add rules first."` and nothing else happens.
   - Otherwise `ClearFileSelection()` runs first, so **every file starts unselected**.
   - For each file of each group in `DuplicateGroups`, walk `FilterRules` from index 0. Skip any rule with `IsEnabled == false`. Take `input` = `FileName` when `Target == Filename`, else `FilePath`. On the first `MatchesFilter` hit, set `IsFileSelected = (rule.Action == FilterAction.Include)` and **`break`** — no later rule is consulted for that file.
   - A file matched by no enabled rule stays unselected.
   - Then `RefreshSelectedFileCount()` and status `"Applied {enabled}/{total} rules (priority order): {selected} files selected."`
6. **`MatchesFilter` is a substring test, or a time-boxed regex test.**
   - `IsRegex == true` → `Regex.IsMatch(input, pattern, IgnoreCase|None, TimeSpan.FromSeconds(1))` inside `try { … } catch { return false; }`. Every failure mode — invalid pattern, `RegexMatchTimeoutException`, anything else — collapses to "did not match", with no error shown anywhere.
   - `IsRegex == false` → `input.Contains(pattern, OrdinalIgnoreCase|Ordinal)`. This is a **substring** test: no anchoring, no wildcards, no glob, no path-segment awareness. `.jpg` matches `photo.jpg.bak`.
7. **The `Exclude` post-pass is a different resolution strategy.** `ApplyIgnoreRules()` runs at the end of Select All / Select Newer / Select Older ([SPEC-004](SPEC-004-selection-and-file-actions.md)). It takes **every** rule whose `Action == Exclude`, and for each currently-selected file deselects it if *any* of them matches. It returns the deselect count, which the calling command appends to its status as `"(N excluded by ignore rules)"`. Here `Exclude` genuinely always wins — unlike ▶ Apply, where a higher-priority `Include` beats a lower-priority `Exclude`.
8. **Persistence is per profile, and rules are shared references while a profile is live.** `SnapshotLiveStateInto` writes `profile.FilterRules = FilterRules.ToList()` — a new list of the *same* `FilterRule` instances. `ApplyProfileToLiveState` clears the live collection, re-adds the profile's rules, and re-numbers. `CloneProfile` is the one place that deep-copies each rule (all seven fields including `Priority`), so a cloned profile's rules are independent objects.
9. **A bare enable/disable toggle is not itself a disk write.** The chip checkbox writes `FilterRule.IsEnabled` directly; `MainViewModel` does not subscribe to individual rules' `PropertyChanged`, so no `SaveSettings()` fires. Because the live rule objects are the same instances the active profile holds (rule 8), the new value is already in memory and lands on disk with the next `SaveSettings()` from any other action. The **✓ All** / **✕ None** buttons do call `SaveSettings()` explicitly.

**Invariants**

- `Priority == index + 1` for every rule, re-established after every structural change, and never persisted.
- ▶ Apply only ever writes `ScannedFile.IsFileSelected`. It never deletes, moves, hides, re-orders, or re-scans anything.
- ▶ Apply always starts from a cleared selection, so it is idempotent: running it twice on unchanged results and unchanged rules yields the same selection.
- Every regex evaluation in this feature carries a 1-second `MatchTimeout`. This is one of exactly two `Regex` sites in the codebase — the other is `DuplicateScannerService.GroupByNameRegex` ([SPEC-001](SPEC-001-duplicate-detection.md)). Nothing enforces the convention mechanically; a new `Regex` without a timeout would pass every gate.
- Rules iterate `DuplicateGroups` directly, not the filtered `ICollectionView`. Groups hidden by the [SPEC-002](SPEC-002-filtering-and-sorting.md) filters are still evaluated and can still end up selected.
- `FilterAction` and `FilterTarget` serialize as their ordinals (`System.Text.Json` writes enums as numbers by default), so the ordinals *are* the on-disk format: `Include = 0` / `Exclude = 1`, `Filename = 0` / `Filepath = 1`. The action members were renamed after 1.0.0 (`Contains`/`Ignore` → `Include`/`Exclude`) **without** renumbering, which is why a 1.0.0 `settings.json` still loads correctly. Indirectly pinned by `FilterRuleTests.JsonDeserialize_WithDisplaySummary_ShouldNotFail`, which asserts `"Action":0` deserializes to `Include`.
- An old `settings.json` that still contains the now-`[JsonIgnore]`d `DisplaySummary` / `Priority` properties loads without error — unknown JSON properties are ignored. Pinned by `JsonSerialize_ShouldNotIncludeDisplaySummary`, `JsonSerialize_ShouldNotIncludePriority`, `JsonDeserialize_WithDisplaySummary_ShouldNotFail`.
- A rule persisted before `IsEnabled` existed comes back enabled — an absent property falls back to the C# initializer. Pinned by `JsonDeserialize_MissingIsEnabled_ShouldDefaultToTrue`.
- Round-tripping a fully-populated rule preserves all six serialized fields. Pinned by `JsonRoundTrip_ShouldPreserveAllProperties`.

**Edge cases**

| Case | Behavior |
|------|----------|
| Empty or whitespace-only pattern | **+ Add** is disabled by `CanExecute`; `AddFilterRule` also returns early after `Trim()` |
| Pattern with leading/trailing spaces | Trimmed once, at add time; the stored pattern has no outer whitespace |
| Two identical rules added twice | Both are kept — there is no de-duplication. The earlier one always wins under first-match |
| Invalid regex (e.g. `[unclosed`) | Silently matches nothing, for every file, forever. No validation at add time, no error at apply time |
| Catastrophically backtracking regex | Costs up to 1 second **per file**, then counts as no-match. `ApplyFilterRules` runs synchronously on the UI thread, so a large result set freezes the window for that long |
| All rules disabled, then **▶ Apply** | Selection is cleared and nothing is re-selected; status reads `"Applied 0/N rules …"` |
| No rules at all, then **▶ Apply** | Early return with `"No filter rules to apply. Add rules first."`; the current selection is left untouched |
| **▶ Apply** with no scan results | Command is disabled (`DuplicateGroups.Count > 0`) |
| A **disabled** `Exclude` rule + Select All / Newer / Older | Still deselects. `ApplyIgnoreRules` filters on `Action` only and never checks `IsEnabled` |
| `Include` at priority 1 and `Exclude` at priority 2, both matching one file | Under **▶ Apply** the file is selected (first match wins). Under Select All the post-pass then deselects it |
| `Target = Filepath` with `IgnoreCase` off | Ordinal substring over the full path. Windows paths are case-insensitive on disk, so a differently-cased path will not match |
| ▲ on the first chip / ▼ on the last | No-op; no re-number, no save |
| Switching profiles | The live rule list is replaced wholesale by the new profile's rules and re-numbered; the builder's in-progress state is not cleared |

**Not implemented**

- **The help popup contradicts the code, in two ways.** The Custom Rules `?` popup (`MainWindow.xaml`, the `HelpButtonStyle` `Tag` on the Custom Rules label) still names the actions **"Contains"** and **"Ignore"** — the drop-down shows `Include` / `Exclude` — and states *"Ignore ALWAYS overrides Contains. If a file matches both, it stays unchecked."* That is true of the `ApplyIgnoreRules` post-pass (rule 7) but **false of ▶ Apply**, which is first-match-wins by priority. The popup text is stale, not the code.
- **Dead code in `ApplyFilterRules`.** It builds `var rulesHighToLow = FilterRules.Reverse().ToList();` and never reads it — a leftover from an earlier lowest-priority-first overwrite strategy that was replaced by the first-match `break`.
- **No preview or dry-run.** There is no "this would select N files" before committing; the only feedback is the status line afterwards.
- **No pattern validation.** Regex syntax is never checked at add time, and a broken pattern is indistinguishable from a pattern that legitimately matches nothing.
- **No rule names, descriptions, import/export, or grouping.** Rules are an ordered flat list scoped to one profile; sharing a rule set means cloning the profile.
- **No glob or path-segment matching.** `\Originals\` as a plain pattern works only because it happens to be a substring of the path; there is no `*`/`**` support outside regex mode.
- **Rules do not auto-apply.** Nothing re-runs them after a scan, after a profile switch, or when a rule changes — **▶ Apply** is always an explicit action.

## Links

- Decisions: [ADR-002 — Hand-rolled MVVM (`ViewModelBase` + `RelayCommand`)](../adr/ADR-002-hand-rolled-mvvm.md) · [ADR-007 — `System.Text.Json` settings with enum-ordinal stability and `[JsonIgnore]` on computed properties](../adr/ADR-007-system-text-json-settings-compatibility.md) · [ADR-006 — Persist settings on every mutation](../adr/ADR-006-persist-settings-on-every-mutation.md)
- Module docs: [WindowsFileManager.Core](../modules/core.md) · [WindowsFileManager (WPF UI)](../modules/ui.md)
- Related specs: [SPEC-001 — Duplicate detection](SPEC-001-duplicate-detection.md) · [SPEC-002 — Filtering and sorting](SPEC-002-filtering-and-sorting.md) · [SPEC-004 — Selection and file actions](SPEC-004-selection-and-file-actions.md) · [SPEC-007 — Folder search](SPEC-007-folder-search.md) · [SPEC-009 — Settings and window-state persistence](SPEC-009-settings-and-window-state-persistence.md) · [SPEC-010 — Contextual help](SPEC-010-contextual-help.md)
- Background: [`../CONTEXT.md`](../CONTEXT.md) · guard rails in [`../SECURITY.md`](../SECURITY.md)
- Tests: `tests/WindowsFileManager.Tests/Models/FilterRuleTests.cs` · `tests/WindowsFileManager.Tests/Services/SettingsServiceTests.cs` (rule persistence and legacy-JSON migration)
