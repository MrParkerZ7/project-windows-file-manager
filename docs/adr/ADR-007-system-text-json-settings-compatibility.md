# ADR-007: `System.Text.Json` settings with enum-ordinal stability and `[JsonIgnore]` on computed properties

## Status

Accepted — 2026-04-15 (conventions established across commits `2a9177c` 2026-04-14, `cc02c3b` 2026-04-15, and
`fab9c91` 2026-04-15)

## Context

`settings.json` is user data. Every release writes it — on every mutation, after
[ADR-006](ADR-006-persist-settings-on-every-mutation.md) — and every later release must still be able to read
what an earlier one wrote. The serializer is `System.Text.Json` with no custom converters registered, which
imposes two behaviours the model has to be designed around:

1. **Enums serialize as their integer ordinal**, not their name. Renaming an enum member is invisible in the
   file; *reordering* one silently reinterprets every existing file.
2. **Every public getter is serialized**, including computed, getter-only properties. On write they bloat the
   file with derived data; on read, a property with no setter is a deserialization hazard when an old file
   contains it.

Both bit this project during the filter-rule work in April 2026, and both were then written up as standing
conventions in [`../../CLAUDE.md`](../../CLAUDE.md) § Key Conventions:

> **`[JsonIgnore]` on computed properties**: Getter-only properties on serialized models (e.g.,
> `DisplaySummary`, `Priority`) must have `[System.Text.Json.Serialization.JsonIgnore]` to prevent
> serialization/deserialization issues with old settings files

> **Enum rename safety**: `System.Text.Json` serializes enums as integers by default. When renaming enum
> values (e.g., `Select` → `Contains`), keep the same ordinal position to maintain backward compatibility with
> existing settings

and recorded as a dated incident in § Project Notes:

> **[2026-04-15]** `FilterAction.Select` renamed to `FilterAction.Contains` for user clarity. Backward
> compatible with old settings (enum ordinal 0 unchanged).

The `fab9c91` commit body records the other half: *"Add `[JsonIgnore]` to `DisplaySummary` to fix
serialization."*

## Decision

Three rules govern every model type that reaches `settings.json`.

**1 — `System.Text.Json`, no custom converters.** `SettingsService` serializes `AppSettings` with
`WriteIndented = true` and deserializes with the default options
([`../../src/WindowsFileManager.Application/Services/SettingsService.cs`](../../src/WindowsFileManager.Application/Services/SettingsService.cs)).

**2 — Enum ordinals are frozen; only names change.** Ordinal 0 of `FilterAction` has been renamed **twice**
and has never moved:

| Commit | Date | Ordinal 0 member |
|---|---|---|
| `2a9177c` | 2026-04-14 | `Select` |
| `cc02c3b` | 2026-04-15 | `Contains` |
| `567ac3c` | 2026-04-18 | `Include` (current) |

Two enums have explicit ordinal guards in the test suite:

- `ActionHistoryEntryTests.ActionHistoryKind_Ordinals_Preserved` asserts `MoveFiles=0`, `RecycleFiles=1`,
  `RecycleDirectories=2`, `CreateShortcuts=3`.
- `FolderSearchPatternTests.FolderMatchType_Ordinals_Preserved` is a six-case `[Theory]` asserting
  `Include=0`, `Match=1`, `Contains=2`, `Exclude=3`, `Mismatch=4`, `NotContain=5`.

**3 — `[JsonIgnore]` on every computed property of a serialized model.** In
[`../../src/WindowsFileManager.Core/Models/FilterRule.cs`](../../src/WindowsFileManager.Core/Models/FilterRule.cs):
`Priority` (line 40) and `DisplaySummary` (line 87). `FolderSearchPattern.Priority` carries the same
attribute. `FilterRuleTests` asserts that the serialized JSON contains **neither** name, and that a legacy blob
which *does* contain `DisplaySummary` still deserializes successfully.

## Consequences

### Positive

- Renaming an enum member for UI clarity is a safe, isolated change — user settings survive it. This has been
  exercised three times on one enum without a compatibility break.
- `System.Text.Json` ignores unknown JSON properties by default, so a member that was removed or later given
  `[JsonIgnore]` does not break loading an older file.
- Absent properties fall back to C# field initializers, so a file written before a property existed loads with
  that property's intended default (`FilterRule.IsEnabled` returns to `true`, `IgnoreCase` to `true`).
- No third-party serializer to reference, version, or vulnerability-scan.
- The whole load path — including the legacy flat-schema migration and the corrupt-file fallback — is testable
  because it runs on `IFileSystemService` ([ADR-004](ADR-004-ifilesystemservice-io-abstraction.md)).

### Negative

- **Ordinal position is load-bearing but only half-guarded.** `ActionHistoryKind` and `FolderMatchType` have
  ordinal tests. **`FilterAction` and `FilterTarget` do not.** Reordering `FilterAction`'s members — the very
  enum whose rename triggered this convention — would silently reinterpret every user's existing filter rules
  as the opposite action, with a green test suite. Only code review stands between that and a release.
- **The `[JsonIgnore]` rule has no analyzer behind it.** It is a convention applied by hand. Adding a new
  computed property to a serialized model without the attribute is a latent load bug that surfaces only on
  someone else's old settings file.
- **`settings.json` is not human-diagnosable.** Enums are opaque integers, so reading a user's file to
  reproduce a bug requires cross-referencing the enum declarations at that version.
- **Corruption is silent.** `SettingsService.Load` catches `JsonException` and returns `CreateDefault()`. A
  malformed file therefore resets every setting with no warning and no salvage attempt — see
  [ADR-006](ADR-006-persist-settings-on-every-mutation.md) § Negative for how a truncated write can produce
  exactly that.
- The compatibility guarantee is one-directional and undocumented in the file itself: there is no schema
  version field in `AppSettings`, so a future breaking change has no marker to branch on.

### Neutral

- The other half of the compatibility story is the **legacy flat-schema migration**: when `Profiles.Count == 0`
  after deserialization, `Load` re-parses the raw JSON with `JsonDocument` and hand-maps the old flat shape
  into a single `"Default"` profile, skipping malformed elements rather than aborting. That path is itself
  wrapped in a `JsonException` catch.
- `AppSettings` repairs an empty or unknown `ActiveProfileName` (compared `OrdinalIgnoreCase`) by falling back
  to `Profiles[0].Name`.
- `Priority` is `[JsonIgnore]`d because ordering is recovered from list position at load time and
  re-derived by `RefreshRulePriorities()`, not persisted.

## Links

- [ADR-006](ADR-006-persist-settings-on-every-mutation.md) — when this format gets written
- [ADR-004](ADR-004-ifilesystemservice-io-abstraction.md) — the seam that makes `SettingsService` testable
- [ADR-001](ADR-001-clean-architecture-four-modules.md) — why the serialized models live in Core
- [`../specs/SPEC-009-settings-and-window-state-persistence.md`](../specs/SPEC-009-settings-and-window-state-persistence.md) ·
  [`../specs/SPEC-003-custom-filter-rules.md`](../specs/SPEC-003-custom-filter-rules.md)
- [`../GLOSSARY.md`](../GLOSSARY.md) — profile, filter rule, folder-search pattern
- Source: [`../../src/WindowsFileManager.Application/Services/SettingsService.cs`](../../src/WindowsFileManager.Application/Services/SettingsService.cs) ·
  [`../../src/WindowsFileManager.Core/Models/FilterRule.cs`](../../src/WindowsFileManager.Core/Models/FilterRule.cs) ·
  [`../../src/WindowsFileManager.Core/Models/FolderSearchPattern.cs`](../../src/WindowsFileManager.Core/Models/FolderSearchPattern.cs)
