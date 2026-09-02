# SPEC-009 — Settings and window-state persistence

<!-- Sync contract: `## Current behavior & invariants` is the section a behavior-changing commit must update in the same commit. -->

- Status: Current
- Feature area: persistence
- Ships in: **1.0.0** — settings persistence to `settings.json` (`b7e1de3`, `54388d5`) and window position/size/maximized restore (`cc02c3b`) are both listed in the 1.0.0 CHANGELOG entry. The **multi-profile** schema (`b85e299`, 2026-04-22) and the **persisted action history** (`567ac3c` / `f094c80`, 2026-04-18) ship in **Unreleased**; the legacy-flat-JSON migration exists to read files written by the 1.0.0 shape.

## What

Everything the user configures survives closing the app. The whole configuration lives in one human-readable JSON file under `%APPDATA%`, and it is rewritten on **every** state mutation — not only on exit — so a force-kill or crash costs at most the change in flight.

- **Profiles** — the per-workflow bundle (target folders, exclusions, filter rules, folder-search patterns and results, display preferences). The user can create a blank profile, clone the current one, rename, switch, and delete; at least one profile always exists.
- **Global state** — window geometry, the maximized flag, the undo history, and which profile is active. These are shared across profiles.
- **Compatibility** — a settings file written by an older, pre-profile build is migrated into a single `Default` profile on load; a corrupt or unreadable file falls back to defaults instead of failing to start.

## Why

The app is configured before it is useful: target folders, exclude names, a rule list, a pattern list. Retyping that per session would make it unusable for the repeat cleanup work it exists for.

Saving only on window close was the original design and it failed in practice — `Stop-Process -Force` (the normal way the app ended during development) skips `Window.Closing` and lost an entire session of configuration. That incident is what makes save-on-every-mutation a rule rather than a preference; the reasoning is recorded in [ADR-006](../adr/ADR-006-persist-settings-on-every-mutation.md).

Profiles exist because one machine has several unrelated cleanup jobs — a photo library, a projects tree, a downloads folder — each wanting a different set of folders, rules and patterns. Without them the user edits the same single configuration back and forth.

## Scope

### In

- The on-disk location, format and lifecycle of `settings.json`.
- What is per-profile and what is global.
- When a save is triggered, and what a save writes.
- Load, legacy migration, and the repair rules that guarantee a usable `AppSettings`.
- Profile create / clone / switch / rename / delete, and the live-state ↔ profile snapshot/apply.
- Window geometry capture and multi-monitor-safe restore.

### Out

- The *rationale* for save-on-mutation and for the JSON conventions — [ADR-006](../adr/ADR-006-persist-settings-on-every-mutation.md) and [ADR-007](../adr/ADR-007-system-text-json-settings-compatibility.md). This spec states the behavior, not the argument.
- What each persisted field *means* to its feature — [SPEC-001](SPEC-001-duplicate-detection.md), [SPEC-002](SPEC-002-filtering-and-sorting.md), [SPEC-003](SPEC-003-custom-filter-rules.md), [SPEC-005](SPEC-005-file-preview.md), [SPEC-007](SPEC-007-folder-search.md).
- The undo semantics of the persisted history entries — [SPEC-004](SPEC-004-selection-and-file-actions.md) and [SPEC-008](SPEC-008-clear-subfolders.md).
- Any settings UI beyond the profile bar. There is no settings dialog, no import/export, no "restore defaults" button, and none is scaffolded.

## Current behavior & invariants

**Entry points**

| Trigger | Handler | Notes |
|---------|---------|-------|
| App start | `MainViewModel` constructor → `LoadSettings()` | the parameterless ctor builds the service via `CreateDefaultSettings()` |
| Settings path | `MainViewModel.CreateDefaultSettings()` | `Environment.SpecialFolder.ApplicationData` + `WindowsFileManager` + `settings.json`; the path is a `SettingsService` constructor argument, which is what lets tests inject one |
| Any state mutation | `MainViewModel.SaveSettings()` | **27 call sites** in `MainViewModel` (28 occurrences of the identifier, counting the method itself) plus one in `MainWindow_Closing` |
| Profile switch / rename / delete | `SwitchProfile` · `RenameActiveProfile` · `DeleteActiveProfile` | call `_settingsService.Save(_settings)` **directly**, bypassing `SaveSettings()` |
| Window restore | `MainWindow.MainWindow_Loaded` → `MainViewModel.GetSettings()` | `GetSettings()` is `_settingsService.Load()` — a **fresh read from disk**, not the live `_settings` |
| Window close | `MainWindow.MainWindow_Closing` → `SaveWindowState(...)` then `SaveSettings()` | the **only** caller of `SaveWindowState` |
| Read / write | `SettingsService.Load()` / `SettingsService.Save(AppSettings)` | `System.Text.Json`, no custom converters; all file I/O through `IFileSystemService` |
| Legacy import | `SettingsService.MigrateLegacyProfile(string json)` | `JsonDocument` hand-mapping, private |

**Rules**

1. **Location and format.** `%APPDATA%\WindowsFileManager\settings.json`, serialized from the whole `AppSettings` object with `WriteIndented = true`. Every save rewrites the entire file.
2. **`Save`.** `Path.GetDirectoryName(_settingsPath)` is created only when it is non-empty **and** `DirectoryExists` is false — a bare filename yields `""` and neither call is made (`Save_BareFilename_ShouldNotAttemptCreateDirectory`); an existing directory is not re-created (`Save_DirectoryExists_ShouldNotCreateIt`). Then `WriteAllText`.
3. **`Load` — four outcomes.**
   - File does not exist → `CreateDefault()`: one `ProfileSettings { Name = "Default" }`, `ActiveProfileName = "Default"`.
   - `JsonException` while reading or deserializing → `CreateDefault()` (`Load_InvalidJson_ShouldReturnDefaults`).
   - A literal `null` payload → `new AppSettings()`, which then falls into migration (`Load_JsonDeserializesToNull_ShouldReturnDefaults`).
   - Otherwise the deserialized object, after migration and repair below.
4. **Legacy migration fires when `Profiles.Count == 0`.** The raw JSON is re-parsed with `JsonDocument` and the old *flat* schema is hand-mapped into one profile named `Default`, which is then added and made active. Mapped fields: `TargetPaths`, `DisabledTargetPaths`, `ExcludeFolderNames`, `DisabledExcludeFolderNames`, `FolderSearchResultPaths`, `SelectedFolderSearchResultPaths` (string lists), `IncludeSubdirectories`, `IsMiniPreview`, `IsAutoPreview`, `IsAutoPlay` (bools), `MinimumFileSize` (long), `Volume` (double), `SelectedSortOption`, `MoveTargetPath` (strings), `FilterRules`, `FolderSearchPatterns` (object lists). Every reader falls back to the property's **current default** when the token is absent or the wrong `ValueKind`; string lists skip non-string and null elements; object lists skip nulls and deserialize per item. A root that is not an object (e.g. `[]`) returns the bare default profile. A `JsonException` raised inside migration is swallowed, keeping whatever was populated so far.
5. **Active-profile repair.** If `ActiveProfileName` is empty or names no existing profile (`OrdinalIgnoreCase`), it is set to `Profiles[0].Name`.
6. **What is per-profile.** `ProfileSettings` carries 22 properties: `Name`, `TargetPaths`, `DisabledTargetPaths`, `IncludeSubdirectories`, `MinimumFileSize`, `IsMiniPreview`, `IsAutoPreview`, `IsAutoPlay`, `SelectedSortOption`, `Volume`, `MoveTargetPath`, `ExcludeFolderNames`, `DisabledExcludeFolderNames`, `FilterRules`, `FolderSearchPatterns`, `FolderSearchMaxDepth`, `FolderSearchResultPaths`, `SelectedFolderSearchResultPaths`, `LinkSiblingsLayer`, `LinkSiblingsPrefix`, `DuplicateMatchByRegex`, `DuplicateMatchRegex`. Defaults are pinned by `ProfileSettingsTests.Constructor_ShouldSetDefaults` — notably `Name = "Default"`, `IncludeSubdirectories = true`, `MinimumFileSize = 1`, `IsMiniPreview = true`, `IsAutoPreview = true`, `IsAutoPlay = false`, `SelectedSortOption = "Size (largest)"`, `Volume = 0.5`, `LinkSiblingsLayer = 1`, `FolderSearchMaxDepth = null` (unlimited), `DuplicateMatchByRegex = false`.
7. **What is global.** `AppSettings` holds `Profiles`, `ActiveProfileName`, `ActionHistory`, `WindowLeft`, `WindowTop`, `WindowWidth`, `WindowHeight` (all four `double?`) and `IsMaximized`. Window geometry and undo history are *not* per profile.
8. **`SaveSettings()`.** Returns immediately while `_isSwitchingProfile` is set. Otherwise: resolve (or seed) the active profile, `SnapshotLiveStateInto(profile)`, copy `ActiveProfileName`, copy `ActionHistory` into the settings object, copy the five window fields, then `_settingsService.Save(_settings)`.
9. **The snapshot covers 20 of the 22 profile fields.** `Name` is owned by the profile verbs. **`MinimumFileSize` is never written from live state and never read into it** — it is persisted, migrated and cloned, but dead (see *Not implemented* and [SPEC-001](SPEC-001-duplicate-detection.md)). `FolderSearchMaxDepth` is snapshotted through `ParseFolderSearchMaxDepth()`, so an invalid depth string persists as `null`.
10. **Save triggers.** The 27 in-view-model call sites are: the `DuplicateMatchByRegex` and `DuplicateMatchRegex` setters; the `FolderSearchMaxDepthText` setter; `ScanAsync` (at scan start); `AddFolder`, `AddFolderByPath`, `RemoveFolder`; `AddExcludeFolder`, `RemoveExcludeFolder`; `AddFilterRule`, `RemoveFilterRule`, `MoveFilterRuleUp`, `MoveFilterRuleDown`, `ClearAllRules`, `SetAllRulesEnabled`; `AddFolderSearchPattern`, `RemoveFolderSearchPattern`, `MoveSearchPatternUp`, `MoveSearchPatternDown`; `ClearFolderSearch`; `SearchFoldersAsync` (in its `finally`); `ToggleItem_PropertyChanged` (any target-path/exclude enable toggle); `FolderResult_PropertyChanged` and `BulkSetFolderSelection` (folder-result selection); `PushHistory`, `UndoEntry`, `ClearHistory`. `MainWindow_Closing` adds one more.
11. **Setters that do *not* save.** `IsMiniPreview`, `IsAutoPreview`, `IsAutoPlay`, `MediaVolume`, `SelectedSortOption` and `MoveTargetPath` only raise `PropertyChanged`. Their values reach disk on the next save triggered by something else, or at window close.
12. **Window geometry is captured only at close.** `_windowLeft/_windowTop/_windowWidth/_windowHeight` start `null` and `_isMaximized` starts `false`; `SaveWindowState` is the only writer and `MainWindow_Closing` is its only caller. Every mid-session save therefore writes `null` geometry, and the real values land in the file only during an orderly close. `MainWindow_Closing` uses `RestoreBounds` when the window is maximized, so the stored size is the *normal* size, not the maximized one.
13. **Window restore.** `MainWindow_Loaded` re-reads settings from disk and only proceeds when **both** `WindowWidth` and `WindowHeight` are non-null (`WindowLeft`/`WindowTop` default to `0`). The saved rectangle must intersect the virtual desktop — `left < VirtualScreenLeft + VirtualScreenWidth && left + width > VirtualScreenLeft && top < VirtualScreenTop + VirtualScreenHeight && top + height > VirtualScreenTop` — and only then are `WindowStartupLocation = Manual` plus `Left/Top/Width/Height` applied. `IsMaximized` is applied **regardless** of that test. Loaded also snapshots the panel visibility flags and force-hides the preview and analytics panels, because Folder is the first tab.
14. **Profile verbs.**
    - **Create blank** — resolve a unique name, snapshot the current profile, add an empty `ProfileSettings`, refresh names, switch to it.
    - **Clone** — snapshot the source, resolve a unique name, deep-copy all 22 fields (new `List`s, and new `FilterRule` / `FolderSearchPattern` instances so the copies are independent), switch to it.
    - **Unique name** — `ResolveUniqueProfileName` trims the suggestion (falling back to `"New Profile"` / `"Copy of <name>"`), then appends `" (2)"`, `" (3)"`, … until no `OrdinalIgnoreCase` collision remains.
    - **Switch** — no-op for a blank name, the current name, or an unknown name. Otherwise: snapshot the outgoing profile, set the active name, apply the incoming profile with `_isSwitchingProfile` set (which suppresses the save storm from every collection change), then `_settingsService.Save(_settings)` directly.
    - **Rename** — no-op for blank or unchanged; a colliding name (`OrdinalIgnoreCase`) sets `ProfileOperationStatus = "A profile named '<name>' already exists."` and changes nothing; otherwise the name is updated everywhere and saved.
    - **Delete** — refuses when only one profile remains (the command's `CanExecute` is `ProfileNames.Count > 1`, and the code re-checks); otherwise removes the profile, moves to `Profiles[0]`, applies it under `_isSwitchingProfile`, and saves.
    - The dialogs (`Views/ProfileNameDialog`) validate names against the live name list before the command runs; they are UI-side only.
15. **Applying a profile** (`ApplyProfileToLiveState`) unsubscribes every `ToggleItem` / `FolderSearchResult` handler, clears the target-path, exclude, filter-rule, pattern, result and inventory collections, assigns the scalar preferences (including `IsPreviewVisible = IsAutoPreview` and `LinkSiblingsLayer` floored at 1), rebuilds `ToggleItem`s with their disabled sets, rebuilds rules and patterns, sets the depth text (empty when `FolderSearchMaxDepth` is null or `< 1`), rebuilds the folder-search results with their selection, recomputes counts and the header checkbox, sets the status text, kicks off `ComputeRestoredSizesAsync()` when results were restored, and refreshes rule and pattern priorities.
16. **Action history.** `ActionHistory` is loaded from `AppSettings.ActionHistory` at startup, inserted at index 0 on each new entry, trimmed to `MaxHistoryEntries = 30`, and saved on push, on undo, and on clear.

**Invariants**

- `Load()` always returns an `AppSettings` with **at least one profile**, and `ActiveProfileName` always names a profile that exists.
- A save writes the complete `AppSettings`; there is no partial update, no file lock and no merge. The last writer wins.
- With the exception of window geometry (rule 12), any state that reaches the file is at most one mutation behind the UI ([ADR-006](../adr/ADR-006-persist-settings-on-every-mutation.md)).
- No mutation is written while a profile switch is applying — `_isSwitchingProfile` guards `SaveSettings`, and the switch itself saves once at the end.
- Enum ordinals in persisted models are frozen and computed properties carry `[JsonIgnore]` (`FilterRule.Priority`, `FilterRule.DisplaySummary`, `FolderSearchPattern.Priority`) — [ADR-007](../adr/ADR-007-system-text-json-settings-compatibility.md). Priorities are rebuilt from list order after load, never read from the file.
- `System.Text.Json` ignores unknown properties, so a file written by a newer or older build still loads; absent properties fall back to the C# initializers.
- All settings I/O goes through `IFileSystemService`, which is what allows `SettingsServiceTests` to exercise every path against a `Mock<IFileSystemService>` with no disk ([ADR-004](../adr/ADR-004-ifilesystemservice-io-abstraction.md)).
- A full save → load round trip preserves profiles, the active profile, filter rules and all five window fields — pinned by `SettingsServiceTests.SaveAndLoad_MultiProfile_ShouldRoundTrip`.

**Edge cases**

| Case | Behavior | Pinned by |
|------|----------|-----------|
| No settings file yet | One `Default` profile, `ActiveProfileName = "Default"` | `Load_FileNotExists_ShouldReturnDefaultsWithDefaultProfile` |
| Malformed JSON | Defaults; the file is overwritten by the next save | `Load_InvalidJson_ShouldReturnDefaults` |
| File contains `null` | `new AppSettings()`, then migration produces the `Default` profile | `Load_JsonDeserializesToNull_ShouldReturnDefaults` |
| `ActiveProfileName` missing or unknown | Falls back to `Profiles[0].Name` | `Load_MissingActiveProfile_ShouldFallBackToFirst`, `Load_EmptyActiveProfileName_ShouldFallBackToFirst` |
| Legacy flat file | Migrated into a single `Default` profile | `Load_LegacyFlatJson_ShouldMigrateIntoDefaultProfile` |
| Legacy file whose root is an array | Bare default profile, no fields mapped | `Load_LegacyArrayAtRoot_ShouldYieldEmptyDefaultProfile` |
| Legacy field of the wrong JSON type | That field keeps its default, the rest still migrate | `Load_LegacyNonArrayFields_ShouldIgnoreThem`, `Load_LegacyFilterRulesWithTypeMismatch_ShouldSwallowJsonException` |
| Legacy list containing `null` entries | Nulls are skipped, the rest are kept | `Load_LegacyStringListWithNullEntry_ShouldSkipNull`, `Load_LegacyObjectListWithNullEntry_ShouldSkipNull` |
| Legacy numeric overflow (`MinimumFileSize` beyond `Int64`) | Falls back to the default `1` | `Load_LegacyNumericOverflow_ShouldFallBack` |
| Legacy `false` written explicitly | Respected as `false`, not treated as absent | `Load_LegacyBoolean_ShouldRespectFalseExplicitly` |
| Saved window rectangle entirely off the current monitor set | Position and size are **not** applied (the XAML defaults 1600×900, centered, stand); `IsMaximized` is still applied | — |
| App force-killed | Everything except window geometry is intact up to the last mutation; geometry reverts to defaults | — |
| Two app instances open at once | Both write the whole file; the last save wins and silently discards the other's changes | — |

**Not implemented**

- **Window geometry does not survive an abnormal exit.** Because `SaveWindowState` runs only from `Window.Closing` while every other save writes the still-`null` fields, a crash or force-kill leaves `WindowLeft/Top/Width/Height` as `null` and `IsMaximized` as `false` in the file — the exact failure mode ADR-006 fixed for the rest of the settings remains open for geometry.
- **`ProfileSettings.MinimumFileSize` is a dead field.** It is defaulted, migrated, cloned and serialized, but nothing writes it from the UI and nothing reads it into a scan. See [SPEC-001](SPEC-001-duplicate-detection.md) § Not implemented.
- **Several live settings are not persisted at all** and reset on every launch: `FolderSearchIncludeSubdirectories` (`true`), `FlattenRemoveEmptyFolders` (`true`), the post-scan display filters (`MinFileSizeText`, `SelectedSizeUnit`, `MinDuplicateCount`, extension-filter check states — [SPEC-002](SPEC-002-filtering-and-sorting.md)), the subfolder/file-type filter text and selections, and both discovered inventories ([SPEC-008](SPEC-008-clear-subfolders.md)).
- **No schema version field.** Migration is triggered solely by `Profiles.Count == 0`, so a future schema change has no version to branch on.
- **No backup, and no recovery beyond falling back to defaults.** A corrupt file is silently replaced by the next save; there is no `.bak`, no quarantine copy, and no message telling the user their configuration was reset.
- **No concurrency control.** Nothing detects a second instance, and there is no file lock, mtime check, or merge.
- **The file is plaintext and unredacted.** It holds absolute scanned paths, the move-target path, folder names, user regexes and the full action history with the paths it touched — see [`../SECURITY.md`](../SECURITY.md).
- **The persistence layer's UI paths are untested.** `SettingsService` is fully covered, but `MainViewModel`'s save/load/snapshot/apply/profile code and `MainWindow`'s geometry code are `[ExcludeFromCodeCoverage]` ([ADR-011](../adr/ADR-011-coverage-via-collector-and-script.md)).

## Links

- Decisions: [ADR-006 — Persist settings on every mutation](../adr/ADR-006-persist-settings-on-every-mutation.md) · [ADR-007 — `System.Text.Json` settings with enum-ordinal stability](../adr/ADR-007-system-text-json-settings-compatibility.md) · [ADR-004 — All I/O behind `IFileSystemService`](../adr/ADR-004-ifilesystemservice-io-abstraction.md) · [ADR-011 — coverage measured by coverlet.collector, enforced by script](../adr/ADR-011-coverage-via-collector-and-script.md)
- Module docs: [WindowsFileManager.Application](../modules/application.md) · [WindowsFileManager.Core](../modules/core.md) · [WindowsFileManager (WPF UI)](../modules/ui.md)
- Related specs: [SPEC-001 — Duplicate detection](SPEC-001-duplicate-detection.md) · [SPEC-002 — Filtering and sorting](SPEC-002-filtering-and-sorting.md) · [SPEC-003 — Custom filter rules](SPEC-003-custom-filter-rules.md) · [SPEC-005 — File preview](SPEC-005-file-preview.md) · [SPEC-007 — Folder search](SPEC-007-folder-search.md) · [SPEC-008 — Clear subfolders](SPEC-008-clear-subfolders.md)
- Background: [`../CONTEXT.md`](../CONTEXT.md) · [`../SECURITY.md`](../SECURITY.md)
- Tests: `tests/WindowsFileManager.Tests/Services/SettingsServiceTests.cs` · `tests/WindowsFileManager.Tests/Models/AppSettingsTests.cs` · `tests/WindowsFileManager.Tests/Models/ProfileSettingsTests.cs` · `tests/WindowsFileManager.Tests/Models/ActionHistoryEntryTests.cs`
