# Security Guardrails — Folder File Control

> Mandatory rules for any code change in this repository. Read before touching filesystem
> operations, the settings file, regex handling, shell/COM interop, or the CI workflows.
>
> **Why this document is load-bearing:** this application **deletes and moves the user's
> files**. It runs at the interactive user's full privilege, has no sandbox, no allow-list of
> permitted roots, and no dry-run mode. A defect here does not leak data — it destroys it.
> Every rule below is written as an instruction, not a description.

**How to read the status markers.** Every table in this document marks whether a protection
is real today:

| Marker | Meaning |
|---|---|
| **PRESENT** | The protection exists in code today. Do not remove it. |
| **ABSENT** | The protection does **not** exist. The adjacent rule is what you must do by hand. |
| **PARTIAL** | Exists for some paths, not all. The gap is named. |

An **ABSENT** row is not an oversight to be quietly worked around — it is the reason the rule
next to it is stated. Treat "the code does not stop me" as *"I am the only thing stopping it."*

**Related documents.** Architecture and module boundaries: [`../ARCHITECTURE.md`](../ARCHITECTURE.md).
Build/test/quality-gate commands: [`DEV.md`](DEV.md). Per-decision rationale: [`adr/README.md`](adr/README.md).
Feature behavior contracts: [`specs/_index.md`](specs/_index.md). Agent build conventions: [`../CLAUDE.md`](../CLAUDE.md).
There is **no `QUALITY.md`** in this repo; gate enforcement is documented in [`DEV.md`](DEV.md)
and defined in `Directory.Build.props`, `tests/WindowsFileManager.Tests/WindowsFileManager.Tests.csproj`,
and the two workflows under `.github/workflows/`.

---

## Trust Boundaries

The app is a single-process WPF desktop application. It has **no network egress, no server, no
IPC surface, and no plugin host**. Its trust boundaries are therefore all *local*: the boundary
between "data the user or another local process controls" and "code that then acts on it,
irreversibly, at full user privilege."

### The privilege baseline

| Property | Value | Evidence |
|---|---|---|
| Elevation | Never requested. No `app.manifest`, no `requestedExecutionLevel` anywhere in the tree. | `src/WindowsFileManager/WindowsFileManager.csproj` (no `ApplicationManifest`) |
| Process identity | The interactive Windows user. | `OutputType=WinExe`, plain `Main` |
| MSIX capability | `rescap:Capability Name="runFullTrust"` — a **restricted** capability granting full user-token filesystem access. | `src/WindowsFileManager/Package.appxmanifest` |
| Entry point | `Windows.FullTrustApplication` | same file |

**Consequence you must internalise:** the app can delete anything its user can delete. There is
no second privilege tier to fall back on and no "safe mode." The OS ACL is the *entire*
authorization model — see § Auth & Sessions.

### Boundary inventory

Each row is a place where data the app did not author enters, or where the app reaches out of
its own process.

| # | Boundary | What crosses | Code | Protection today |
|---|---|---|---|---|
| B1 | **User-typed scan root** | An arbitrary path string, unnormalized | `MainViewModel.AddFolderByPath` (`MainViewModel.cs:2351`) | **ABSENT** — `.Trim()` and a duplicate-value check, nothing else |
| B2 | **Picked scan root / move target** | A path chosen through the OS folder picker | `AddFolder` (`:2335`), `BrowseMoveTarget` (`:4835`) — `Microsoft.Win32.OpenFolderDialog` | **PRESENT** — the shell dialog only returns real directories |
| B3 | **Move-target path** | Free-text destination that is **created if missing** | `MoveSelectedFiles` (`:4722`) → `EnsureMoveTargetDirectory` (`:4848`) → `Directory.CreateDirectory` | **ABSENT** — any typed path is silently created; no rooted/sane/inside-anything check |
| B4 | **Directory enumeration** | Filenames and directory names from disk | `FileSystemService.EnumerateFiles` / `EnumerateDirectories` (`src/WindowsFileManager.Infrastructure/Services/FileSystemService.cs:14-22, 52-59`) | **PARTIAL** — `IgnoreInaccessible = true` and `AttributesToSkip = FileAttributes.System`. Reparse points and hidden files are **not** excluded |
| B5 | **File content read for hashing** | The full bytes of every size-colliding candidate | `FileHashService.ComputeHash` (`src/WindowsFileManager.Application/Services/FileHashService.cs:27-32`) — `SHA256.HashData(stream)` | **ABSENT** — no size cap, no location exclusion, no per-file error handling |
| B6 | **File content read for preview** | Arbitrary bytes decoded as image / media / text | `MainViewModel.PreviewFile`, `TryReadAsText` (8 KB NUL-ratio sniff), text capped at 50 000 chars; `BitmapImage` decode gated to 15 extensions by `IsWpfNativeImage` | **PARTIAL** — extension gating and a text cap exist; image decoding of hostile files is still in-process |
| B7 | **Shell thumbnail extraction** | Arbitrary user files handed to **third-party shell extension code, in-process** | `MiniPreviewConverter` — `SHCreateItemFromParsingName` → `IShellItem` → `IShellItemImageFactory.GetImage` (`src/WindowsFileManager/Helpers/MiniPreviewConverter.cs:109-121, 141+`) | **PARTIAL** — `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]` on both P/Invokes blocks DLL planting; the extractor itself is unsandboxed |
| B8 | **Settings file** | Paths, regexes, folder names, action history — read back and **acted on** | `SettingsService.Load` (`src/WindowsFileManager.Application/Services/SettingsService.cs:31-63`); path built in `MainViewModel.CreateDefaultSettings` (`:4949-4956`) | **PARTIAL** — fixed POCOs (no polymorphic deserialization, so no type-confusion), three tiers of `JsonException` tolerance. **No value validation** |
| B9 | **Explorer launch** | A filesystem path interpolated into a command-line string | `OpenFileLocation` (`:2380`), `OpenFolderLocation` (`:3432`) — `Process.Start("explorer.exe", $"/select,\"{filePath}\"")` | **ABSENT** — manual quoting, no `ArgumentList`, no escaping of embedded quotes |
| B10 | **External URL launch** | A URL from bound help text, opened with the shell | `FormattedTextBehavior.AddHyperlink` (`src/WindowsFileManager/Helpers/FormattedTextBehavior.cs:152-176`) — `ProcessStartInfo { FileName = url, UseShellExecute = true }` | **ABSENT** — no scheme allow-list; failures swallowed. URLs are app-authored today, but the behavior is attached to arbitrary bound text |
| B11 | **Recycle Bin COM** | Late-bound `Shell.Application`, `NameSpace(10)`, `GetDetailsOf(item, 1)`, `InvokeVerb("&Restore")` | `RestoreFromRecycleBin` (`:4237`) | **ABSENT** — locale-dependent verb and column index; every failure swallowed, returns 0 |
| B12 | **WScript.Shell COM** | Creates `.lnk` files pointing at arbitrary folders | `ShortcutHelper.CreateFolderShortcut` (`src/WindowsFileManager/Helpers/ShortcutHelper.cs:9-33`) | **PRESENT** — COM objects released in `finally` |
| B13 | **Network** | Nothing. The application makes no HTTP/socket calls. | — | **PRESENT by absence** — preserve it (see § Auth & Sessions) |

### The I/O seam rule

`WindowsFileManager.Core` and `WindowsFileManager.Application` perform **all** filesystem access
through `IFileSystemService` (`src/WindowsFileManager.Core/Services/IFileSystemService.cs`, 11
members). The single real implementation is `FileSystemService` in Infrastructure, which is
`[ExcludeFromCodeCoverage]` and deliberately excluded from the coverage `Include` list — see
[`adr/README.md`](adr/README.md) (ADR-004).

> **Rule 1 — new I/O in Core or Application MUST go through `IFileSystemService`.** If the
> operation you need is not on the interface, add it to the interface, implement it in
> `FileSystemService`, and mock it in tests. Never `using System.IO;` your way around the seam
> in those two projects. Calling `File.*` / `Directory.*` directly there breaks testability
> *and* silently moves untested real I/O into a 100%-covered assembly.

**Honest current state:** the UI layer (`MainViewModel`, `ShortcutHelper`, `MiniPreviewConverter`)
does *not* fully honor this seam. It calls `System.IO.File`/`Directory`,
`Microsoft.VisualBasic.FileIO.FileSystem`, and COM directly — for example `Directory.Delete`
(`:3961`), `File.Move` (`:3918`, `:4781`), `File.Delete` (`:4193`), and `FolderContainsItem`
(`:3394`). That is *why* `MainViewModel` carries `[ExcludeFromCodeCoverage]` and why its
destructive paths have no automated test. **Those call sites are the exception, not the
precedent.** When you touch one, prefer moving the logic behind the interface; at minimum do not
add new ones.

---

## Input Validation

There is no server and no client — validation happens once, at the point the value is used.
Two categories matter: **paths** (because acting on the wrong one destroys data) and **regexes**
(because an unbounded one hangs the app).

### Paths

| Concern | Status | Detail |
|---|---|---|
| Normalization | **ABSENT** | `AddFolderByPath` (`:2351`) stores `NewFolderPath.Trim()` verbatim. No `Path.GetFullPath`, no `Path.IsPathRooted` check, no canonicalization |
| Existence check | **PARTIAL** | Only at scan time: `DuplicateScannerService.Scan` throws `DirectoryNotFoundException` for any target that fails `DirectoryExists` (`DuplicateScannerService.cs:44-50`), **before** any enumeration. Folder search silently skips missing roots |
| UNC / device paths (`\\server\share`, `\\?\`, `\\.\`) | **ABSENT** | Not detected, not rejected |
| Traversal (`..`) | **ABSENT** | Not normalized away. See the note below on why this still matters |
| System-path denylist (`C:\Windows`, `Program Files`) | **ABSENT** | No path is off-limits to scanning or deletion |
| Drive-root guard | **ABSENT** | `C:\` is an acceptable scan root and an acceptable delete scope |
| Junctions / symlinks / reparse points | **ABSENT** | `EnumerationOptions.AttributesToSkip` is set to `FileAttributes.System` only (`FileSystemService.cs:20, 57`). `FileAttributes.ReparsePoint` is **not** included, and `RecurseSubdirectories = true` for `SearchOption.AllDirectories` — so the recursive walk descends *through* junctions and symlinks |
| Hidden files | **ABSENT (deliberate-looking)** | The BCL default for `AttributesToSkip` is `Hidden \| System`; this code overrides it to `System` alone, so hidden files are enumerated, hashed, listed, and deletable |
| Long paths (> `MAX_PATH`) | **ABSENT** | No `app.manifest`, therefore no `longPathAware` opt-in. `PathTooLongException` is not handled anywhere on the scan path |
| Permission-denied during enumeration | **PRESENT** | `IgnoreInaccessible = true` on both enumeration calls |
| Permission-denied during metadata / hashing | **ABSENT** | `GetFileSize` and `GetLastWriteTime` in the collect loop (`DuplicateScannerService.cs:82, 104`) and `OpenRead` in `FileHashService.ComputeHash` have **no** try/catch |

**Why traversal still matters even though the user picks the paths.** The user is the trust
principal for *typed* input, so `..` is not a privilege escalation when typed. It matters
because the same values are **persisted to `settings.json` and reloaded** (B8) — and that file
is writable by any process running as the same user. A path is therefore not "user input"
forever; after the first save it is *file* input.

> **Rule 2 — validate any path that came from `settings.json` the same way you would validate
> one typed by a stranger.** `SettingsService.Load` coerces *types*, never *values*. It will
> happily hand you `\\?\GLOBALROOT\...` or `C:\Windows\System32` as a scan root.

> **Rule 3 — do not add a recursive walk without deciding what it does at a reparse point.**
> Today the walk follows them. If your feature can loop, double-count, or delete through a
> junction, add `FileAttributes.ReparsePoint` to `AttributesToSkip` for *your* enumeration, or
> track visited directories.

> **Rule 4 — wrap per-file `IFileSystemService` calls that can throw.** The scan collect loop
> and `ComputeHash` currently do not. Combined with `async void ScanAsync` (`:2259`) — which
> catches only `OperationCanceledException`, `DirectoryNotFoundException`, and
> `ArgumentException` (`:2310-2321`) — an `UnauthorizedAccessException`, `IOException`,
> `FileNotFoundException` (a TOCTOU delete between enumeration and `GetFileSize`), or
> `PathTooLongException` becomes an **unobserved exception on an `async void` method, which
> terminates the process.** This is the single highest-value validation gap in the codebase.
> Handle it in your feature; do not extend the pattern.

### User-supplied regex

Regex is a first-class user input here: it is typed into the UI, it is **persisted**, and it is
reloaded on every start.

| Site | Bounded timeout? | Behavior on a hostile pattern |
|---|---|---|
| `DuplicateScannerService.GroupByNameRegex` (`DuplicateScannerService.cs:181`) | **PRESENT** — `new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))` | Invalid pattern → `ArgumentException` rethrown with context (`:183-186`). `RegexMatchTimeoutException` → converted to `ArgumentException` naming the offending file and the backtracking cause (`:199-205`), surfaced in `StatusMessage` |
| `MainViewModel.MatchesFilter` (`MainViewModel.cs:3050`) | **PRESENT** — `Regex.IsMatch(input, filter, options, TimeSpan.FromSeconds(1))` | Wrapped in `try { } catch { return false; }` (`:3051-3055`) — a timeout is **silently indistinguishable from a legitimate non-match**. No user feedback |

The scanner's timeout carries an in-code comment (`:178-180`) recording exactly *why* it exists:
the cancellation token is checked only **between** files, never inside `Match`, so without the
timeout the worker thread would hang unkillably.

**Residual exposure — state it plainly.** The timeout is **per match, not per scan**. A pattern
that times out costs up to 1 second × N files in `MatchesFilter` (which swallows and continues),
and aborts the entire scan on the first hit in `GroupByNameRegex`. Neither is a hang, but a
30 000-file filter run against a pathological pattern is a 30 000-second stall in the UI path.

> **Rule 5 — never evaluate a user-supplied regex without a bounded `matchTimeout`.** Both
> existing sites comply *by convention only*; **no analyzer, no test, and no hook enforces it**.
> A new `Regex.IsMatch(input, userPattern)` is one line away from shipping. The correct form is
> always the four-argument overload, or a `Regex` instance constructed with a `TimeSpan`.

> **Rule 6 — a swallowed regex timeout must not be silent.** If you add a third site, surface
> the failure (status message, disabled rule, visible marker). `MatchesFilter`'s silent `false`
> is a known wart, not a pattern to copy: a user whose saved rule times out sees a filter that
> "just stops working" with no explanation, forever, because the pattern is persisted.

### Numeric and text UI inputs

`MinFileSizeText` + `SelectedSizeUnit` are parsed in `ApplyFilters`; `FolderSearchMaxDepthText`
is parsed by `ParseFolderSearchMaxDepth` (blank, non-numeric, or `< 1` → `null` = unlimited).
`Views/MainWindow.xaml.cs` adds digit-only `PreviewTextInput` handlers.

> **Rule 7 — `PreviewTextInput` is a convenience, not validation.** The same fields load from
> `settings.json` with no keystroke filtering. `SettingsService`'s `ReadLong`/`ReadDouble`
> helpers fall back to the property default on a wrong `ValueKind` or an overflow — rely on that
> fallback, and re-check ranges at the point of use.

---

## Auth & Sessions

**There is none. State this honestly and design around it.**

| Concept | Status in this app |
|---|---|
| Authentication | **None.** No login, no account, no credential of any kind |
| Authorization | **None in-app.** The Windows ACL on each file is the complete model |
| Sessions / tokens | **None.** No session state, no token, no cookie, no refresh |
| Network identity | **None.** The app opens no sockets and makes no HTTP calls |
| Multi-user isolation | Provided by the OS: settings live under per-user `%APPDATA%`. **Not** provided against a second process running as the *same* user |

### What that implies — these are the rules, not observations

> **Rule 8 — every operation already runs at the user's full privilege.** There is no
> "elevate for this action" path and none may be added without an ADR. If a feature needs
> elevation, that is a design smell: the app's whole premise is that it acts as the user, on
> files the user already owns.

> **Rule 9 — do not add any network egress without an ADR and a revision of this document.**
> That covers telemetry, crash/error reporting, analytics, auto-update, update-availability
> checks, license validation, cloud sync, and "just fetching an icon." The moment the app
> speaks to a network it acquires: an identity to authenticate, a transport to secure, a server
> certificate to trust, a privacy surface (the settings file is a **map of the user's disk** —
> see § Data Protection), and a supply-chain dependency it does not have today. **B13's
> "PRESENT by absence" is a real security property. Deleting it is a decision, not a feature.**

> **Rule 10 — the settings file is not an authorization boundary.** Do not put a flag in it
> that gates a destructive capability and then treat that flag as a guarantee. Any same-user
> process can flip it.

> **Rule 11 — do not add a credential store, a "connect account" affordance, or a
> "remember me."** The app has nowhere safe to keep a secret: settings are plaintext JSON and
> there is no DPAPI usage anywhere in the tree.

---

## Secrets Management

### The application holds no secrets

Confirmed by inspection: no environment-variable reads for credentials, no embedded keys, no
connection strings, no config secrets, and **zero runtime NuGet packages** that could carry one.
Keep it that way.

### CI secrets — the complete set

Exactly two, both GitHub Actions repository secrets, both used only by
`.github/workflows/msix-pipeline.yml`:

| Secret | Purpose | Consumed at |
|---|---|---|
| `CERTIFICATE_PFX` | Base64-encoded code-signing PFX | `msix-pipeline.yml:137-147` (availability check), `:148-153` (decode to `${{ runner.temp }}\certificate.pfx`) |
| `CERTIFICATE_PASSWORD` | PFX password | `msix-pipeline.yml:170` (`signtool /p`) |

**The branch guard is the important part.** Both the decode step and the sign step carry the
identical condition (lines 147 and 154):

```yaml
if: steps.check-cert.outputs.HAS_CERT == 'true' && github.event_name == 'push' && github.ref == 'refs/heads/main'
```

The workflow also triggers on `pull_request`. **That condition is the only thing keeping the
signing certificate out of PR runs.**

> **Rule 12 — never weaken or remove the `github.event_name == 'push' && github.ref == 'refs/heads/main'`
> condition on the decode or sign steps, and never add a new step that touches `CERTIFICATE_*`
> without reproducing it.** A `pull_request`-triggered step with access to the PFX hands the
> signing identity to anyone who can open a pull request.

### Known weaknesses in the current secret handling — PRESENT, unmitigated

State these so nobody "fixes" them by accident in the wrong direction, and so anybody hardening
them knows the full list:

| Weakness | Location | Nature |
|---|---|---|
| Base64 PFX interpolated into the **body** of a PowerShell script | `msix-pipeline.yml:141`, `:152` | The entire blob becomes part of the executed script text |
| Password interpolated into a **command line** | `msix-pipeline.yml:170` | Visible in the runner's process command line for the duration of `signtool` |
| PFX written to disk, cleaned best-effort | written `:151`, removed `:176` with `-ErrorAction SilentlyContinue` under `if: always()` | A failed cleanup is not an error |
| Availability check runs on **every** event | `msix-pipeline.yml:137-147` — the `Check signing certificate availability` step has **no** `if:` condition | It interpolates the secret into a string comparison on PR runs too |
| `ci.yml` declares **no `permissions:` block** | `.github/workflows/ci.yml` | Inherits the repository default token scope. `msix-pipeline.yml` correctly declares `contents: read` + `security-events: write` |

### The dev certificate script

`scripts/New-DevCertificate.ps1` creates a self-signed code-signing certificate
(`New-SelfSignedCertificate`, EKU `1.3.6.1.5.5.7.3.3`), exports it to `.\certificate.pfx`, and
imports it into `Cert:\LocalMachine\TrustedPeople` for sideloading.

- **`$Password = "DevPassword123!"` is hardcoded as the parameter default (line 12).** It is a
  committed weak credential. Anyone who uses the default *and* uploads that PFX has published
  its password in this repository.
- **The subject-must-match rule (script header, lines 5-6):** `-Subject` **MUST** equal the
  `Publisher` value in `src/WindowsFileManager/Package.appxmanifest` — today `CN=WindowsFileManager`.
  A mismatch makes `signtool` and MSIX installation fail. This rule is documented *only* in that
  script header; nothing validates it in CI.

> **Rule 13 — always pass `-Password` explicitly when generating a certificate you intend to
> upload.** The default is for a throwaway local sideload cert and nothing else.

> **Rule 14 — if you change `Publisher` in `Package.appxmanifest`, change the signing subject in
> the same commit** (and vice versa). Nothing else will catch it.

### What `.gitignore` already blocks

`.env`, `*.pem`, `*.key`, `*.pfx`, `*.cer`, `*.msix`, `*.msixbundle`, `*.appxupload`,
`AppPackages/`, `coverage/`, `*.cobertura.xml`, `.claude/`, `bin/`, `obj/`, `publish/`, `*.nupkg`.

`New-DevCertificate.ps1`'s default output `.\certificate.pfx` is covered by `*.pfx`.

> **Rule 15 — a certificate, a private key, or its base64 encoding is never committed and never
> printed.** `.gitignore` covers the obvious extensions; it does **not** cover a PFX renamed to
> `cert.txt`, a base64 blob pasted into a README or a workflow, or a `Write-Host $Password` added
> "temporarily" to debug a signing step. Never `echo`, `Write-Host`, or `Write-Output` a secret
> in CI — GitHub's log masking is a backstop, not a design.

---

## Data Protection

### What data exists

No PII, no credentials, no payment data, no user accounts, no telemetry. Exactly one persisted
artifact, and it is more sensitive than it looks.

**`%APPDATA%\WindowsFileManager\settings.json`** — path built in
`MainViewModel.CreateDefaultSettings` (`:4949-4956`), read/written by `SettingsService`
(`Load` `:31-63`, `Save` `:70`), serialized with `System.Text.Json`, `WriteIndented = true`.

| Property | Value |
|---|---|
| Encryption at rest | **ABSENT** — plaintext JSON |
| Access control | The default `%APPDATA%` ACL: readable and writable by the user, and by anything running as the user |
| Encryption in transit | Not applicable — never leaves the machine (B13) |
| Contents | Absolute scan-root paths, excluded folder names, move-target path, folder-search patterns and results (full paths), custom filter rules including **user regexes**, per-profile preferences, window geometry, and the **action history** — recycled paths, move source→destination pairs, and created shortcut paths |

**Treat it as mildly sensitive.** Aggregated, those fields are a map of where the user keeps
their files and what they recently deleted or moved.

> **Rule 16 — never put a real secret in `settings.json`, never log its contents, and never
> attach it unredacted to a bug report or a crash dump.**

> **Rule 17 — `SaveSettings()` is called from roughly twenty mutation sites** (every toggle,
> every rule reorder, every folder-selection change — see ADR-006 in [`adr/README.md`](adr/README.md)).
> Each call rewrites the whole file. Do not add a field that is both high-churn and newly
> sensitive; you would be writing it to disk dozens of times per session.

### Destructive operations — recoverability, by operation

This is the table to read before writing any code that removes something.

| Operation | Mechanism | Recoverable? | Confirmation today | In `ActionHistory` (undoable)? |
|---|---|---|---|---|
| Recycle one duplicate file | `RecycleFile` → `VbFileSystem.DeleteFile(..., SendToRecycleBin)` (`:4100-4101`), called from `DeleteFile` (`:2383`) | **Yes** — Recycle Bin | **PRESENT** — MessageBox naming the full path (`:2390`) | **Yes** (`:2405-2411`) |
| Recycle **all** files in a duplicate group | `DeleteAllInGroup` (`:2433`) → `RecycleFile` (`:2460`) | **Yes** | **PRESENT** — MessageBox listing **every** path (`:2441`) | **Yes** (`:2474`) |
| Recycle selected duplicates (bulk) | `DeleteSelectedFiles` (`:4630`) → `RecycleFile` (`:4669`) | **Yes** | **PARTIAL** — count only: *"Send N selected files to the Recycle Bin?"* (`:4641`); paths not shown | **Yes** (`:4711`) |
| Recycle files by extension across selected folders | `ClearSelectedFileTypes` (`:4547`) → `RecycleFile` (`:4586`) | **Yes** | **PRESENT** (`:4558`) | **Yes** (`:4616`) |
| Recycle subfolders across many roots | `ClearSelectedSubfolders` (`:4405`) → `RecycleDirectory` (`:4474`) | **Yes** | **PARTIAL** — names the count, the subfolder names, and the root count (`:4440`), then **recursively deletes N names × M roots behind one Yes** | **Yes** |
| Move files (flatten to folder root) | `FlattenFolder` → `File.Move` (`:3918`) | **Yes** — via Undo replay | **PRESENT** (`:3534`) | **Yes** — `ActionHistoryMove`, replayed in reverse order so `(2)` renames unwind correctly |
| Move selected files to target | `MoveSelectedFiles` (`:4722`) → `File.Move` (`:4781`) | **Yes** — via Undo replay | **PRESENT** — MessageBox naming the target (`:4744`) | **Yes** |
| **Delete empty directories** | `RemoveEmptyDirectoriesRecursive` → **`Directory.Delete(path, recursive: false)`** (`:3961`) | **NO — permanent.** Not sent to the Recycle Bin | **ABSENT** — no confirmation of its own; rides on the flatten confirm when `FlattenRemoveEmptyFolders` is set | **NO — not recorded, therefore not undoable** |
| Delete created shortcuts (undo path) | `File.Delete` (`:4193`) | **NO — permanent** | Implicit (the user asked to undo) | N/A — this *is* the undo |

**`RecycleFile` / `RecycleDirectory` are the main safety net of this entire application.** They
route through `Microsoft.VisualBasic.FileIO.FileSystem` with `RecycleOption.SendToRecycleBin`,
which is what makes almost every destructive action reversible. `UIOption.OnlyErrorDialogs`
suppresses the per-item shell confirmation — the app's own MessageBox is the *only* confirmation
the user sees.

> **Rule 18 — the contract every new destructive operation must satisfy.** All five, no
> exceptions:
> 1. **Explicit user gesture.** Reached only from a command bound to a control the user clicked.
>    Never a side effect of a scan completing, a selection changing, a preview rendering, a
>    profile switching, or settings loading.
> 2. **Confirmation before acting.** A `MessageBox` with `MessageBoxImage.Warning` that names
>    *what* will be affected — paths where the count is small, counts plus scope where it is not.
> 3. **Recycle, do not erase.** Use `RecycleFile` / `RecycleDirectory` (`:4100-4104`). Do **not**
>    reach for `File.Delete` or `Directory.Delete`. `RemoveEmptyDirectoriesRecursive` (`:3961`)
>    is the existing exception and it is a **wart, not a precedent** — it is permanent and
>    un-undoable.
> 4. **Push an `ActionHistoryEntry`.** Via `PushHistory` (`:4106`), so Undo works. An operation
>    with no history entry is an operation the user cannot take back.
> 5. **Never assume an original survives.** `DeleteAllInGroup` (`:2433`) can delete *every* copy
>    in a duplicate group; **nothing in the code enforces "keep at least one."** If your feature
>    implies keeping an original, enforce it yourself.

### Absent safety mechanisms — stated as rules because nothing enforces them

| Missing protection | Consequence | Your obligation |
|---|---|---|
| **No dry-run / preview mode** anywhere | The confirmation dialog is the entire preview | Make the confirmation text carry the information a preview would |
| **No allow-list or deny-list of roots** | `C:\Windows`, `C:\Program Files`, and drive roots are valid scan and delete scopes | Do not add an operation that walks *upward* or defaults to a broad root |
| **No cap on blast radius per confirmation** | One Yes on `ClearSelectedSubfolders` (`:4405`, prompt at `:4440`) authorizes N subfolder names × M root folders | Show the real total in the prompt; do not hide multiplication behind a single number |
| **No error surfacing or logging** | Every destructive loop is `catch { failed++; }` (`:4480`, `:4675`, `:4786`, `:3924`) — a count, never a cause, never a log line, never a path | Report *which* items failed, not just how many |
| **No file-in-use / read-only pre-check** | Failures are discovered mid-loop and swallowed | Same as above |

### Known documentation drift — fix the string, not the code

The Action help popup in `src/WindowsFileManager/Views/MainWindow.xaml` (around line 1484)
claims Delete *"Permanently removes checked files from disk. No Recycle Bin — cannot be undone."*
**The code recycles and pushes history — the operation IS undoable.** The help text understates
the safety, which is the harmless direction, but it is still wrong.

> **Rule 19 — if in-app help and code disagree about destructiveness, correct the help text.**
> Do not "make the code match the docs" by removing the Recycle Bin routing or the history entry.

---

## Dependencies & Supply Chain

### The runtime dependency set is empty — protect that

**No `src/` project references a single NuGet package.** Verified across all four production
csproj files: only `ProjectReference` entries. The application is built entirely from the BCL,
WPF, the in-box `Microsoft.VisualBasic` assembly, and COM interop.

This is the strongest supply-chain property this repository has.

| Scope | Packages | Pinning |
|---|---|---|
| Runtime (`src/`) | **none** | — |
| Build-time analyzer (all projects, via `Directory.Build.props`) | `StyleCop.Analyzers` **1.1.118** (`PrivateAssets=all`) | Exact version |
| Test-only (`tests/`, `IsPackable=false`) | `xunit` 2.8.1 · `xunit.runner.visualstudio` 2.8.1 · `Microsoft.NET.Test.Sdk` 17.10.0 · `Moq` 4.20.70 · `FluentAssertions` 6.12.0 · `coverlet.msbuild` 6.0.2 | Exact versions |

**No lockfile.** There is no `packages.lock.json` and `RestorePackagesWithLockFile` is not set,
so the exact-version pins bind the *direct* set only; transitive versions are resolved at restore
time. If you want reproducible restores, that is the knob — today the guarantee is
direct-dependency-only.

> **Rule 20 — adding a runtime NuGet reference to any `src/` project requires an ADR.** Write
> down what it does, why the BCL cannot, and what its transitive graph pulls in. "Zero runtime
> dependencies" is a property worth an argument before it is spent.

### Scanning in CI

| Gate | Tool | Where | Failure behavior |
|---|---|---|---|
| Dependency vulnerabilities | `dotnet list package --vulnerable --include-transitive` | `.github/workflows/ci.yml:53-61` (pwsh; greps for `"has the following vulnerable packages"`) | `Write-Error` + `exit 1` — job fails |
| SAST | Semgrep, rulesets **`p/default`** + **`p/csharp`** | `.github/workflows/msix-pipeline.yml:34-42`, in the `semgrep/semgrep` container on `ubuntu-latest` | `--error` ⇒ any finding fails the job |
| SAST reporting | SARIF upload | `msix-pipeline.yml:44-48` — `github/codeql-action/upload-sarif`, SHA-pinned, `if: always()`, `sarif_file: semgrep-results.sarif` | Results land in the GitHub Security tab (requires the `security-events: write` permission declared at `msix-pipeline.yml:16-18`) |
| Store certification | WACK `appcert.exe` | `msix-pipeline.yml:211-216` (job `wack-validation`) | Non-zero exit fails the job |

**The `security-scan` job is a hard gate on packaging.** `build-and-package` declares
`needs: security-scan`, and `wack-validation` declares `needs: build-and-package`. A Semgrep
finding therefore blocks the MSIX build *and* certification.

> **Rule 21 — never remove the `needs: security-scan` edge, and never drop `--error` from the
> Semgrep invocation.** Both changes turn a blocking gate into a decorative one.

> **Rule 22 — do not silence a Semgrep finding with an inline `nosemgrep` comment unless the
> comment states why the finding is a false positive in this context.** An unexplained
> suppression is indistinguishable from a bug.

### Action pinning

**PRESENT: SHA pinning** (2026-09-02). All 10 `uses:` references across both workflows are pinned
to a full 40-character commit SHA, with the human-readable version kept in a trailing comment:

| Action | Pinned SHA | Version |
|---|---|---|
| `actions/checkout` | `11d5960a326750d5838078e36cf38b85af677262` | v4.4.0 |
| `actions/setup-dotnet` | `67a3573c9a986a3f9c594539f4ab511d57bb3ce9` | v4.3.1 |
| `actions/upload-artifact` | `ea165f8d65b6e75b540449e92b4886f43607fa02` | v4.6.2 |
| `actions/download-artifact` | `d3f86a106a0bac45b974a628896c90dbdf5c8093` | v4.3.0 |
| `github/codeql-action/upload-sarif` | `6f5948dfacef28e207b48d0905cf90c03365536d` | v3.37.9 |

Previously every reference used a mutable major tag (`@v4`), which accepts upstream mutation
within the major version. Semgrep's `github-actions-mutable-action-tag` rule flagged all 10, and
because the `security-scan` job runs `--error`, those findings blocked the entire MSIX pipeline —
every downstream job declares `needs: security-scan`. This matters more here than in most repos: a
signing certificate is in scope on `main`, so a mutated action runs in the same job as the PFX.

**The cost of pinning is staleness**, and that is owned by
[`../.github/dependabot.yml`](../.github/dependabot.yml) — a weekly `github-actions` update that
bumps the SHAs and their version comments together. Removing that file re-introduces the tail
without re-introducing the finding, which is the silent-failure mode to watch for.

> **Rule 23 — never replace a pinned SHA with a tag.** Re-introducing `@v4` restores the
> supply-chain exposure *and* re-blocks the MSIX pipeline on the Semgrep gate. Bump the SHA and
> its trailing version comment together; let Dependabot do it.

### Analyzer gates that also serve security

Enforced on **every build** via `Directory.Build.props` — these are not optional local niceties:

```
EnableNETAnalyzers=true · AnalysisLevel=latest · EnforceCodeStyleInBuild=true
TreatWarningsAsErrors=true · CodeAnalysisTreatWarningsAsErrors=true
```

Roslyn's built-in analyzers include the `CA2100`/`CA3xxx`/`CA5xxx` security rule families. With
`TreatWarningsAsErrors`, a security analyzer warning is a **build error**, not a suggestion.

> **Rule 23 — do not suppress a CA-prefixed security analyzer in `.editorconfig`.** The file
> already carries 14 StyleCop suppressions, each with a rationale comment — those are *style*
> rules. Adding a `CA5xxx` suppression alongside them silently disables a security gate for the
> whole repository. Fix the finding, or suppress it at the single call site with a justification.

---

## What NOT To Do

Hard rules. Each one is grounded in something real in this codebase. Violating any of them is a
change that must not merge.

1. **Never delete or move a file outside an explicit user action.** No cleanup on scan
   completion, no "tidy up" on exit, no auto-remove of items that "look like" duplicates, no
   deletion triggered by a selection change, a preview, a profile switch, or settings load. The
   user clicks, then and only then does something disappear.

2. **Never perform a destructive operation without a confirmation dialog naming what it
   affects.** Count-only prompts are the existing weak spot (`DeleteSelectedFiles` `:4641`,
   `ClearSelectedSubfolders` `:4440`) — do not add another. Show paths where the list is short;
   show count **and** scope where it is long.

3. **Never use `File.Delete` or `Directory.Delete` for user data.** Use `RecycleFile` /
   `RecycleDirectory` (`:4100-4104`). `RemoveEmptyDirectoriesRecursive`'s
   `Directory.Delete(path, recursive: false)` (`:3961`) is permanent, un-undoable, and the one
   place this rule is already broken — do not cite it as precedent.

4. **Never add a destructive operation without a matching `ActionHistoryEntry`.** If Undo cannot
   reverse it, the user cannot recover from your bug.

5. **Never bypass `IFileSystemService` for real I/O in `WindowsFileManager.Core` or
   `WindowsFileManager.Application`.** Add the method to the interface instead. Direct
   `System.IO` calls in those projects defeat the test seam *and* smuggle uncovered real I/O into
   assemblies the coverage gate reports as 100%.

6. **Never run a user-supplied regex without a bounded match timeout.** Always the
   `TimeSpan`-carrying overload. No analyzer enforces this — you are the enforcement.

7. **Never make a regex failure silent in a new site.** `MatchesFilter`'s `catch { return false; }`
   (`:3051-3055`) turns a timeout into a permanent invisible non-match on a *persisted* pattern.
   Surface it.

8. **Never commit a certificate, private key, or its base64 encoding**, and never print one in
   CI output. `.gitignore` covers `*.pfx` / `*.key` / `*.pem` / `*.cer` / `.env` — it does not
   cover a renamed file or a pasted blob.

9. **Never use `New-DevCertificate.ps1`'s default password (`DevPassword123!`) for a certificate
   you upload anywhere.** It is published in this repository.

10. **Never give a `pull_request`-triggered step access to `CERTIFICATE_PFX` or
    `CERTIFICATE_PASSWORD`.** Preserve the
    `github.event_name == 'push' && github.ref == 'refs/heads/main'` condition on
    `msix-pipeline.yml` lines 147 and 154 exactly as written.

11. **Never widen the app's filesystem reach without a confirmation gate.** No new default scan
    root, no auto-added path, no "scan all drives" without the same explicit-gesture +
    confirmation + recycle + history contract as everything else.

12. **Never broaden `Capabilities` in `Package.appxmanifest` beyond `runFullTrust`.** Adding a
    capability changes the Store review posture and the user-facing permission prompt. It needs
    an ADR.

13. **Never add network egress — telemetry, crash reporting, auto-update, analytics, license
    checks — without an ADR and an update to this document.** "No network" is a documented
    security property of this application (B13), not an unimplemented feature.

14. **Never `Process.Start` with interpolated untrusted input.** The two existing Explorer
    launches (`:2380`, `:3432`) hand-quote a path into a single argument string with no escaping
    — do not copy that shape. Use `ProcessStartInfo.ArgumentList`.

15. **Never extend `FormattedTextBehavior`'s `<link=URL>` grammar to untrusted text without a
    scheme allow-list.** `AddHyperlink` (`FormattedTextBehavior.cs:152-176`) calls
    `Process.Start` with `UseShellExecute = true` and no scheme validation — safe only because
    every URL is currently authored in this repository's own XAML.

16. **Never add `[ExcludeFromCodeCoverage]` to real logic to get past the 100% coverage
    threshold.** The attribute is in the `ExcludeByAttribute` list
    (`WindowsFileManager.Tests.csproj:24`), so the gate would still pass — silently. That makes
    the gate a lie.

17. **Never weaken the coverage threshold** in `tests/WindowsFileManager.Tests/WindowsFileManager.Tests.csproj:20-22`,
    and never wire `-p:CollectCoverage=false` into a CI step. The sibling
    `coverlet.runsettings` file declares `ThresholdType` but **no `Threshold` value** — it would
    not compensate. The csproj MSBuild properties are the only real enforcement.

18. **Never suppress a `CA`-prefixed security analyzer in `.editorconfig`.** Fix it, or suppress
    it at the single call site with a written justification.

19. **Never remove the `needs: security-scan` dependency or the `--error` flag from the Semgrep
    step** in `msix-pipeline.yml`. Both convert a blocking gate into decoration.

20. **Never add a runtime NuGet dependency to a `src/` project without an ADR.** The zero-runtime-
    dependency property is deliberate and worth defending.

21. **Never trust a value read from `settings.json`.** It is attacker-writable by any process
    running as the user, `SettingsService.Load` validates types but not values, and the paths and
    regexes inside it are acted on directly.

22. **Never let an exception escape an `async void` handler on a destructive or scanning path.**
    `ScanAsync` (`:2259`) catches only three exception types; an unhandled `IOException` or
    `UnauthorizedAccessException` from the hashing path terminates the process mid-operation.
    Do not add a new `async void` without a catch-all that reports rather than crashes.
