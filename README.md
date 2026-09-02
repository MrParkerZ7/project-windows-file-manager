# Folder File Control

> Ships as **Folder File Control** — the `Package.appxmanifest` `DisplayName` and the main
> window title. The repository, solution, assemblies and namespaces remain
> `WindowsFileManager`; that is the code identity, not the product name.

A .NET 8 WPF desktop application for managing folders and files on Windows. It ships three tabs: **Folder** (search folders, clear repeated subfolders, flatten, link siblings), **Duplication** (find identical files by content hash and reclaim wasted disk space), and **History** (undo any destructive action it performed).

## Features

### Duplicate Detection
- **SHA256 Content Hashing** — Three-stage filter (size grouping → hash computation → duplicate confirmation) for fast scanning
- **Multi-Folder Scanning** — Add folders by typing paths or browsing, scan across multiple directories, detect cross-folder duplicates
- **Overlapping Path Deduplication** — Adding both `D:\` and `D:\subfolder` won't produce false duplicates
- **Regex Match Mode** — Optional toggle that groups by a filename regex capture instead of size + content hash; falls back to hash mode when the pattern is empty

### Filtering & Sorting
- **Extension Filters** — Show/hide file types with per-extension toggle, show all / clear all
- **Minimum File Size Filter** — Filter by size with selectable unit (B, KB, MB, GB)
- **Minimum Duplicate Count** — Only show groups with N+ duplicates
- **Sort Options** — Sort by size, file count, wasted space, type, or name (ascending/descending)

### Custom Rules
- **Pattern-based rules** — Create rules with Contains (select) or Ignore (deselect) actions on filename or filepath
- **Regex support** — Toggle regex mode per rule with `.*` indicator and contextual help link to regex101.com
- **Case sensitivity** — Toggle ignore-case per rule with `Aa` indicator
- **Enable/Disable toggle** — Checkbox on each rule for quick temporary on/off without deleting
- **Priority ordering** — Move rules up/down, highest priority match wins
- **Bulk controls** — Enable All / Disable All / Apply buttons
- **Persistent rules** — Rules saved to settings.json on every change, survive app restarts

### Selection & Actions
- **Smart Selection** — Select all, select newer, select older duplicates (keep best copy unselected)
- **Move Files** — Move selected duplicates to a target folder (browse or type path)
- **Recycle Files** — Send an individual file, or every file in a group, to the Recycle Bin after confirmation (undoable from the History tab)
- **Open in Explorer** — Open file location in Windows Explorer
- **Per-Group Selection** — Select All / Clear buttons per duplicate group

### Preview
- **File Preview Panel** — Preview images, video, audio, and text files inline
- **Mini Preview** — Thumbnail previews in the file list (Shell thumbnail for video/docs, direct load for images)
- **Auto Preview** — Automatically preview selected files
- **Media Playback** — Play/pause/stop controls with volume slider for video and audio

### Analytics & Monitoring
- **Analytics Dashboard** — Total files, duplicates found, groups, scan time, wasted space %, top extensions, size distribution
- **Resource Monitor** — Live memory, CPU, and thread-count readouts in the status bars

### Folder Management

- **Folder Search** — Find folders by six match types (`Include`, `Match`, `Contains`, `Exclude`, `Mismatch`, `NotContain`) combined with AND logic, with an optional max-depth limit and stop/clear controls
- **Clear Subfolders** — Discover repeated subfolder names across search results (with occurrence counts and parent locations), then bulk-send the selected ones to the Recycle Bin
- **Flatten Folders** — Move nested files up to the folder root with `(2)` / `(3)` conflict renaming, an optional file-type filter, and optional removal of the emptied directories
- **Link Sibling Folders** — Create `.lnk` shortcuts between sibling folders via `ShortcutHelper` (WScript.Shell COM)

### Profiles

- **Named Setting Bundles** — Multiple profiles, each holding its own target paths, filters, rules, and duplicate match mode
- **Profile Commands** — Create, clone, switch, rename, and delete from the profile bar at the top of the window
- **Persisted** — Profiles and the active profile name live in the same `settings.json` as the rest of the preferences

### Action History & Undo

- **History Tab** — Each destructive action is recorded as an `ActionHistoryEntry` in one of four kinds — `MoveFiles` (also used by Flatten), `RecycleFiles`, `RecycleDirectories`, `CreateShortcuts` — capped at the 30 most recent
- **Undo** — Undo the last action or any specific entry: moved files are moved back, recycled items are restored from the Recycle Bin, created `.lnk` shortcuts are deleted
- **History Analytics** — Per-kind operation and item counts across the recorded history

### UX Features
- **Contextual Help** — `?` buttons with rich popup explanations for complex features, including clickable links to external docs (e.g., regex101.com)
- **Window State Persistence** — Remembers window position, size, and maximized state across sessions with multi-monitor fallback
- **Inline Responsive Layout** — Filter, rules, and action bars use WrapPanel — wraps to multiple rows on narrow windows
- **Enable/Disable Toggles** — Target paths and exclude folders have checkboxes for temporary on/off without removing

### General
- **Settings Persistence** — All preferences saved on every change to `%APPDATA%/WindowsFileManager/settings.json` — target folders, filters, rules, sort options, window state
- **Cancellation Support** — Cancel long-running scans at any time
- **Progress Reporting** — Live file count with throttled UI updates (every 100 files)

## Documentation

| Document | What it holds |
|----------|---------------|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Module map, dependency rules, build outputs, ADR summary |
| [docs/README.md](docs/README.md) | Documentation portal — the index of everything below |
| [docs/CONTEXT.md](docs/CONTEXT.md) | Project background: problem, domain primer, key user flows, non-goals |
| [docs/DEV.md](docs/DEV.md) | Local dev loop — prerequisites, build, run, test, package |
| [docs/SECURITY.md](docs/SECURITY.md) | Guardrails: trust boundaries, destructive-operation rules, settings-file handling |
| [docs/GLOSSARY.md](docs/GLOSSARY.md) | Domain terms used across the code (duplicate group, match type, profile, …) |
| [docs/adr/](docs/adr/) | Architectural Decision Records — *why* each load-bearing choice was made |
| [docs/specs/](docs/specs/) | Feature specs (`SPEC-NNN-*`) — the current-truth behavior contract per feature |
| [docs/modules/](docs/modules/) | Per-module mechanics — how the code in each project works |
| [CHANGELOG.md](CHANGELOG.md) | Release history (Keep a Changelog format) |
| [CLAUDE.md](CLAUDE.md) | AI-agent instructions: conventions, quality gates, project notes |

**Sync rule:** a change to a feature's behavior updates that feature's spec in `docs/specs/` in the same commit.

## Platform Support

| Architecture | Status |
|-------------|--------|
| x64 (64-bit) | Supported and shipped |
| x86 (32-bit) | Not configured |
| ARM64 | Not configured |

Only `win-x64` is built, packaged, and tested. `src/WindowsFileManager/WindowsFileManager.csproj` declares `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`, `Package.appxmanifest` declares `ProcessorArchitecture="x64"`, and the MSIX pipeline publishes a self-contained `win-x64` package. There is no x86 or ARM64 RID, package, or CI job — adding one is a deliberate change, not an existing capability.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (developed against 8.0.422)
- Windows 10/11 (WPF requires Windows)

> **`dotnet` must be on `PATH`.** With a portable/side-by-side SDK install the `dotnet` command is not registered globally, and every command below fails with "command not found". Prepend the SDK folder for the session first — e.g. `$env:PATH = "D:\_env_storeage\dotnet;$env:PATH"` in PowerShell. See [docs/DEV.md](docs/DEV.md).

## Getting Started

```bash
# Clone the repository
git clone <repo-url>
cd project-windows-file-manager

# Build
dotnet build

# Run the application
dotnet run --project src/WindowsFileManager

# Run tests — coverage collection and the 100% threshold are always on
dotnet test
```

## Project Structure

The solution follows **Clean Architecture** with four modules:

```
project-windows-file-manager/
├── src/
│   ├── WindowsFileManager.Core/           # Models + Interfaces (zero dependencies)
│   │   ├── Models/
│   │   │   ├── ScannedFile.cs             # File metadata with formatted size
│   │   │   ├── DuplicateGroup.cs          # Group of identical files
│   │   │   ├── ScanOptions.cs             # Scan configuration (paths, filters)
│   │   │   ├── ScanResult.cs              # Scan output with statistics
│   │   │   ├── ScanAnalytics.cs           # Computed analytics, extensions, size buckets
│   │   │   ├── FilterRule.cs              # Dynamic filter rule with enable/disable
│   │   │   ├── FolderSearchPattern.cs     # Folder search pattern + FolderMatchType enum
│   │   │   ├── FolderSearchResult.cs      # One folder-search hit
│   │   │   ├── SubfolderItem.cs           # Discovered subfolder + SubfolderLocation parents
│   │   │   ├── ProfileSettings.cs         # One named profile's workflow state
│   │   │   ├── ActionHistoryEntry.cs      # Undoable action + ActionHistoryKind / ActionHistoryMove
│   │   │   └── AppSettings.cs             # Persisted user preferences (profiles, history, window state)
│   │   └── Services/
│   │       └── IFileSystemService.cs      # File system abstraction
│   │
│   ├── WindowsFileManager.Application/    # Business logic (depends on Core only)
│   │   └── Services/
│   │       ├── DuplicateScannerService.cs # Duplicate detection algorithm
│   │       ├── FileHashService.cs         # SHA256 file hashing
│   │       └── SettingsService.cs         # JSON settings persistence
│   │
│   ├── WindowsFileManager.Infrastructure/ # Real implementations (depends on Core only)
│   │   └── Services/
│   │       └── FileSystemService.cs       # System.IO file operations
│   │
│   └── WindowsFileManager/                # WPF UI layer (depends on all modules)
│       ├── App.xaml / App.xaml.cs         # Application entry point
│       ├── AssemblyInfo.cs                # Assembly-level attributes
│       ├── Package.appxmanifest           # MSIX package manifest (identity, version, logos)
│       ├── Assets/                        # app-icon.ico + Store logos (Square150, Square44, StoreLogo)
│       ├── ViewModels/
│       │   ├── ViewModelBase.cs           # INotifyPropertyChanged base
│       │   ├── MainViewModel.cs           # Main window state + commands
│       │   ├── ExtensionFilter.cs         # File type filter toggle
│       │   └── ToggleItem.cs              # Enable/disable wrapper for paths & exclusions
│       ├── Views/
│       │   ├── MainWindow.xaml(.cs)       # Three-tab UI layout + window code-behind
│       │   └── ProfileNameDialog.xaml(.cs) # Profile name entry dialog
│       └── Helpers/
│           ├── RelayCommand.cs            # ICommand implementation
│           ├── Converters.cs              # Bool/Visibility, Percent/Width converters
│           ├── FileTypeIconConverter.cs   # File extension → category icon converter
│           ├── MiniPreviewConverter.cs    # File path → thumbnail (Shell + direct load)
│           ├── FormattedTextBehavior.cs   # Rich text markup parser (<b>,<h>,<w>,<link>)
│           ├── ShortcutHelper.cs          # .lnk folder shortcut creation via WScript.Shell
│           └── TextBoxEnterKeyBehavior.cs # Enter key attached behavior
│
├── tests/
│   └── WindowsFileManager.Tests/          # Unit tests (217 tests, 100% line/branch/method)
│       ├── WindowsFileManager.Tests.csproj  # Coverage Include/Exclude + Threshold=100
│       ├── coverlet.runsettings           # XPlat Code Coverage settings (used by CI)
│       ├── GlobalUsings.cs                # Shared usings (xUnit, FluentAssertions, Moq)
│       ├── xunit.runner.json              # xUnit runner configuration
│       ├── Models/                        # Core model tests (AppSettings, FilterRule, SubfolderItem, …)
│       ├── Services/                      # Application service tests with Moq (Scanner, Hash, Settings)
│       └── Helpers/                       # UI-layer tests (Converters, RelayCommand, ViewModelBase)
│
├── docs/                                  # Project documentation — see the Documentation section above
├── scripts/
│   └── New-DevCertificate.ps1             # Self-signed dev certificate for MSIX signing
├── .github/workflows/
│   ├── ci.yml                             # Quality Gate pipeline (format → build → test → audit)
│   └── msix-pipeline.yml                  # MSIX Store pipeline (Semgrep → package/sign → WACK)
├── WindowsFileManager.sln                 # Solution — 5 projects in src/ + tests/ solution folders
├── Directory.Build.props                  # Shared analyzers (StyleCop, .NET Analyzers)
├── .editorconfig                          # Code style rules
├── stylecop.json                          # StyleCop configuration
├── .gitignore                             # Build output, coverage, IDE artifacts
├── CHANGELOG.md                           # Release history
├── CLAUDE.md                              # AI agent instructions
└── README.md                              # This file
```

### Dependency Flow

```
UI (WindowsFileManager) → Application → Core ← Infrastructure
```

- **Core** has zero dependencies — models and interfaces only
- **Application** depends on Core interfaces, not implementations
- **Infrastructure** implements Core interfaces with real I/O
- **UI** wires everything together

## Architecture

### Duplicate Detection Algorithm

The scanner uses a three-stage filter for efficiency:

1. **Collect & Filter** — Enumerate files across all target paths, apply size/extension filters, deduplicate overlapping paths
2. **Group by Size** — Files with unique sizes cannot be duplicates (O(n) filter eliminates most files)
3. **Hash Same-Size Files** — Only compute SHA256 for files that share a size, then group by hash

This avoids hashing every file — the expensive operation only runs on candidates.

### MVVM Pattern

- **Models** — Pure data classes in Core, no UI dependencies
- **ViewModels** — Bind to Views via `INotifyPropertyChanged`, expose `ICommand` for actions
- **Views** — XAML-only UI, code-behind limited to window lifecycle events

### Testability

All file system operations go through `IFileSystemService`, allowing complete mock-based testing without touching the real file system. The real implementation (`FileSystemService`) is isolated in the Infrastructure module and excluded from coverage.

## Testing

```bash
# Run all 217 tests — coverage and the 100% threshold are enforced by the test .csproj
dotnet test

# Same run, plus the XPlat cobertura report CI uploads as an artifact
dotnet test --collect:"XPlat Code Coverage" \
  --settings tests/WindowsFileManager.Tests/coverlet.runsettings

# Quick run without the coverage threshold (the only way to skip it)
dotnet test -p:CollectCoverage=false
```

`WindowsFileManager.Tests.csproj` sets `CollectCoverage=true`, `Threshold=100`, and `ThresholdType=line,branch,method`, so a bare `dotnet test` *does* collect coverage and *does* fail the build below 100%.

**Coverage:** 100% line, 100% branch, 100% method — 217 tests, 0 failed, 0 skipped.

| Module | Line | Branch | Method |
|--------|------|--------|--------|
| WindowsFileManager.Core | 100% | 100% | 100% |
| WindowsFileManager.Application | 100% | 100% | 100% |
| WindowsFileManager (ViewModels + Helpers) | 100% | 100% | 100% |

The coverage `Include` filter covers `WindowsFileManager.Core`, `WindowsFileManager.Application`, and the UI assembly's `Helpers` + `ViewModels` namespaces. Infrastructure is excluded, and the UI types that only touch WPF/COM surfaces (`MainViewModel`, `ExtensionFilter`, `ToggleItem`, `FileTypeIconConverter`, `FormattedTextBehavior`, `MiniPreviewConverter`, `ShortcutHelper`, `TextBoxEnterKeyBehavior`) carry `[ExcludeFromCodeCoverage]`.

**Test stack:** xUnit + Moq + FluentAssertions

## CI/CD

### Quality Gate Pipeline (`.github/workflows/ci.yml`)

Runs on `windows-latest` for every push and PR to `main`, in this order:

1. **Restore** — `dotnet restore`
2. **Format check** — `dotnet format --verify-no-changes --no-restore`
3. **Build** — `dotnet build -c Release --no-restore /p:TreatWarningsAsErrors=true`
4. **Test with coverage** — `dotnet test -c Release --no-build --collect:"XPlat Code Coverage" --settings tests/WindowsFileManager.Tests/coverlet.runsettings`
5. **Dependency audit** — `dotnet list package --vulnerable --include-transitive`, failing the job when any vulnerable package is reported
6. **Upload coverage** — `coverage.cobertura.xml` published as the `coverage-report` artifact (runs even on failure)

### MSIX Store Pipeline (`.github/workflows/msix-pipeline.yml`)

Runs on every push and PR to `main` (also supports `workflow_dispatch` for manual triggers):

| Job | Runner | Purpose |
|-----|--------|---------|
| **security-scan** | `ubuntu-latest` | Semgrep SAST with `p/default` + `p/csharp` rulesets, SARIF uploaded to GitHub Security tab |
| **build-and-package** | `windows-latest` | Build, test, publish self-contained x64 MSIX, sign with certificate (main branch only) |
| **wack-validation** | `windows-latest` | Run Windows App Certification Kit, upload report as artifact |

## Microsoft Store Pipeline

### Setup GitHub Secrets

Two secrets are required for code signing (main branch pushes only):

| Secret | Description |
|--------|-------------|
| `CERTIFICATE_PFX` | Base64-encoded `.pfx` certificate file |
| `CERTIFICATE_PASSWORD` | Password for the `.pfx` file |

**Generate a dev certificate for testing:**

```powershell
# Run from project root (elevated PowerShell)
.\scripts\New-DevCertificate.ps1

# Encode as base64 for GitHub Secrets
[Convert]::ToBase64String([IO.File]::ReadAllBytes(".\certificate.pfx")) | Set-Clipboard
```

**For production (Microsoft Store):** Replace the self-signed certificate with a real code signing certificate from DigiCert, Sectigo, or another trusted CA. The `Publisher` in `Package.appxmanifest` must exactly match the certificate's Subject (e.g., `CN=Your Company Name, O=Your Company, L=City, S=State, C=US`).

### Reading the WACK Report

The WACK report (`wack-report.xml`) is uploaded as a GitHub Actions artifact after each pipeline run. Download it from the Actions tab:

1. Go to **Actions** → select the workflow run → **Artifacts** → download `wack-report`
2. Open `wack-report.xml` — look for `<TEST>` elements with `RESULT="FAIL"`
3. Each failed test includes a description and remediation guidance

### Manual Trigger

Go to **Actions** → **MSIX Store Pipeline** → **Run workflow** → select branch → **Run workflow**.

### After MSIX is Ready

1. Download the signed `.msix` artifact from the successful pipeline run
2. Go to [Microsoft Partner Center](https://partner.microsoft.com/dashboard)
3. Create or update your app submission
4. Upload the `.msix` package under **Packages**
5. Complete the store listing, pricing, and certification options
6. Submit for certification

## Code Quality

- **StyleCop Analyzers** — Enforced via `Directory.Build.props`
- **EditorConfig** — 4-space indentation, file-scoped namespaces, PascalCase methods, `_camelCase` fields
- **.NET Analyzers** — Latest analysis level enabled

## License

This project is for personal use.
