# ADR-006: Persist settings on every mutation rather than on window close

## Status

Accepted — 2026-04-15 (commit `fab9c91` "feat: enable/disable toggles, save-on-change, custom rules UX
improvements")

## Context

Settings were originally written once, from `MainWindow_Closing`
([`../../src/WindowsFileManager/Views/MainWindow.xaml.cs`](../../src/WindowsFileManager/Views/MainWindow.xaml.cs)
lines 92–98), which calls `vm.SaveWindowState(...)` and `vm.SaveSettings()` on the `Window.Closing` event.

That covers exactly one way of leaving the application: an orderly close. It does not cover a force-kill, a
crash, or a power loss — and during development force-kill is the normal way the app ends.

The incident is recorded verbatim in [`../../CLAUDE.md`](../../CLAUDE.md) § Project Notes:

> **[2026-04-15]** `Stop-Process -Force` kills the app without triggering `Window.Closing`, so settings were
> lost. Fixed by saving settings on every mutation, not just on close.

The `fab9c91` commit body states the same fix in its own words:

> Save settings on every mutation instead of only on window close to prevent data loss on force-kill.

What was lost each time was not trivial: target paths, exclude-folder names, the whole custom filter-rule
list, folder-search patterns, and display preferences — an entire session of configuration.

## Decision

Call `SaveSettings()` from every state mutation, not only from `Window.Closing`.

- There are **27 `SaveSettings()` call sites** in `MainViewModel.cs` today (28 occurrences of the
  identifier, counting the method declaration itself) — add/remove/reorder a filter rule,
  add/remove/toggle a target path or exclusion, change a folder-search pattern, flip a display toggle, switch
  the duplicate-match mode, edit the match regex, change folder-result selection, push an action-history entry.
- The rule is recorded as a project convention in [`../../CLAUDE.md`](../../CLAUDE.md) § Key Conventions:
  *"**Save on change**: `SaveSettings()` called on every mutation (add/remove/reorder rules, paths,
  exclusions) — not just on window close."*
- `Window.Closing` still runs, because window geometry is only knowable at close: it uses `RestoreBounds` when
  the window is maximized so the saved size is the *normal* size, then calls `SaveWindowState(...)` followed by
  `SaveSettings()`.
- Two suppression flags exist specifically to stop the rule from firing too often: `_isSwitchingProfile`
  short-circuits saves while a profile swap is applying, and `_isBulkFolderSelectionUpdate` suppresses the
  per-item save storm during `BulkSetFolderSelection`.

Persistence goes through `SettingsService.Save`
([`../../src/WindowsFileManager.Application/Services/SettingsService.cs`](../../src/WindowsFileManager.Application/Services/SettingsService.cs)),
serializing the whole `AppSettings` with `WriteIndented = true` to
`%APPDATA%\WindowsFileManager\settings.json` (`MainViewModel.cs:4949-4955`).

## Consequences

### Positive

- A force-kill, crash, or power loss now costs at most the mutation in flight. The failure mode that motivated
  the change is gone.
- The application has no "unsaved changes" state and therefore needs no save button, no dirty-tracking, and no
  "discard changes?" prompt.
- Profile switching is safe: the outgoing profile's live state is already on disk before the incoming one is
  applied.

### Negative

- **Every toggle rewrites the entire file.** `Save` serializes all profiles plus up to 30 action-history
  entries (`MaxHistoryEntries`) with indentation and calls `WriteAllText`. One checkbox click is a full-file
  write, and the file grows with profile count and history depth.
- **The rule is too eager on its own, and needed explicit dampers.** `_isBulkFolderSelectionUpdate` and
  `_isSwitchingProfile` exist purely because a bulk operation would otherwise trigger one full write per item.
  Any future bulk operation must remember to add its own guard — nothing enforces that.
- **The write is synchronous on the UI thread.** A slow, locked, or roaming `%APPDATA%` stalls the UI at an
  arbitrary moment, in the middle of an unrelated interaction.
- **No atomic write and no backup.** There is no temp-file-plus-rename and no `.bak`; a crash during the write
  can leave a truncated `settings.json`. `SettingsService.Load` catches `JsonException` and returns defaults,
  so the observable failure is a **silent reset to defaults** rather than an error the user can act on — the
  exact data loss this ADR was meant to prevent, arriving by a different route.
- Frequency amplifies exposure of the file's contents: absolute scanned paths, move-target paths, and regex
  patterns are rewritten to a world-readable (by the user) plain-JSON file constantly. See
  [`../SECURITY.md`](../SECURITY.md).

### Neutral

- `Save` creates the parent directory only when `Path.GetDirectoryName(_settingsPath)` is non-empty — a bare
  filename never triggers `DirectoryExists`/`CreateDirectory`. Pinned by
  `SettingsServiceTests.Save_BareFilename_ShouldNotAttemptCreateDirectory`.
- Window geometry and action history live on `AppSettings` (global), not on `ProfileSettings` (per-profile), so
  they are shared across profiles.
- All writes go through `IFileSystemService.WriteAllText`, which is what makes the save path testable
  ([ADR-004](ADR-004-ifilesystemservice-io-abstraction.md)).

## Links

- [ADR-007](ADR-007-system-text-json-settings-compatibility.md) — the format written on every one of these saves
- [ADR-004](ADR-004-ifilesystemservice-io-abstraction.md) — the seam `SettingsService` writes through
- [`../specs/SPEC-009-settings-and-window-state-persistence.md`](../specs/SPEC-009-settings-and-window-state-persistence.md) — the living behaviour contract
- [`../SECURITY.md`](../SECURITY.md) — what `settings.json` contains and who can read it
- Source: [`../../src/WindowsFileManager.Application/Services/SettingsService.cs`](../../src/WindowsFileManager.Application/Services/SettingsService.cs) ·
  [`../../src/WindowsFileManager/Views/MainWindow.xaml.cs`](../../src/WindowsFileManager/Views/MainWindow.xaml.cs)
