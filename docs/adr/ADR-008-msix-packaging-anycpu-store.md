# ADR-008: MSIX packaging on AnyCPU targeting the Microsoft Store

## Status

Accepted — 2026-04-04 (commit `9d82e5a` "Add MSIX packaging, signing, and Microsoft Store CI/CD pipeline";
`Package.appxmanifest` added in the same commit)

## Context

The distribution target is the Microsoft Store, which takes MSIX packages. The application is a WPF desktop
tool whose whole purpose is scanning arbitrary user folders and moving, recycling, or flattening files in them
— so a sandboxed AppContainer package with declared file-access capabilities is not viable. It needs the
user's full token.

Separately, the solution had no reason to fix a CPU architecture at build time: the code is fully managed,
has no native dependencies, and every project already built as `Any CPU`.

## Decision

**Package format:** MSIX, assembled in CI, targeting Windows Desktop.

**Architecture stays AnyCPU everywhere except the packaging edge.** The solution defines only
`Debug|Any CPU` and `Release|Any CPU`, and every project maps `Any CPU → Any CPU`
([`../../WindowsFileManager.sln`](../../WindowsFileManager.sln)). No project sets `PlatformTarget`. `win-x64`
appears in exactly three places, all at publish/packaging time:

| Location | Value |
|---|---|
| `src/WindowsFileManager/WindowsFileManager.csproj:10` | `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>` |
| `.github/workflows/msix-pipeline.yml:14` | `RUNTIME_ID: win-x64` |
| `src/WindowsFileManager/Package.appxmanifest:12` | `ProcessorArchitecture="x64"` |

**Full trust, not sandboxed.** The manifest declares the restricted capability
`<rescap:Capability Name="runFullTrust" />` (line 33) and
`EntryPoint="Windows.FullTrustApplication"` (line 40). Identity is `Name="WindowsFileManager"`,
`Version="1.0.0.0"`, `Publisher="CN=WindowsFileManager"`. `TargetDeviceFamily` is `Windows.Desktop` with
`MinVersion 10.0.17763.0` and `MaxVersionTested 10.0.22621.0`.

**The package is assembled by hand in the workflow, not by an MSBuild packaging project.**
[`../../.github/workflows/msix-pipeline.yml`](../../.github/workflows/msix-pipeline.yml):

1. `dotnet publish … -r win-x64 --self-contained true -o publish-output` (lines 87–93)
2. "Prepare MSIX layout" (lines 95–113): copy `publish-output\*` into `msix-layout`, copy
   `Package.appxmanifest` → `msix-layout\AppxManifest.xml`, copy `Assets\*.png`
3. "Create MSIX with MakeAppx" (lines 115–141): locate the newest x64 `makeappx.exe` under
   `C:\Program Files (x86)\Windows Kits\10\bin`, then `pack /d msix-layout /p output\WindowsFileManager.msix /o`
4. Conditional `signtool` signing — only when `secrets.CERTIFICATE_PFX` is present **and** the event is a
   push to `refs/heads/main` (identical `if:` on lines 155 and 162)
5. Upload artifact `msix-package`; job `wack-validation` then runs the Windows App Certification Kit
   (`appcert.exe test -appxpackagepath …`, lines 219–224)

**Signing subject must equal the manifest publisher.** `scripts/New-DevCertificate.ps1` lines 5–6 state the
rule: *"The `-Subject` value MUST match the Publisher in `Package.appxmanifest`. Currently:
`CN=WindowsFileManager`."*

## Consequences

### Positive

- One build configuration. Contributors never pick a platform, and there is no x86/x64 configuration matrix to
  keep in sync across five projects.
- A Store-format artifact is produced on every push and pull request, so packaging breakage is caught at the
  commit that causes it rather than at submission time.
- WACK runs in CI as a separate job, so certification failures surface before a Store submission.
- Signing is conditional on the secret's presence and on a push to `main`, so forks and pull requests still
  build a package without needing (or being able to reach) the certificate.
- `--self-contained true` means no .NET runtime dependency on the target machine.

### Negative

- **`runFullTrust` is a restricted capability.** Store submission requires justification for it, and it grants
  the package the user's full token — which is precisely the authority the app uses to recycle folders and
  move files. The security posture that follows from it is documented in [`../SECURITY.md`](../SECURITY.md).
- **A helper script prints a build command the pipeline does not use.** `scripts/New-DevCertificate.ps1:50`
  prints `dotnet publish src/WindowsFileManager -c Release -r win-x64 -p:WindowsPackageType=MSIX` as step 1
  of its "Next steps" — the only occurrence of that command in the tree — but **that is not the CI path**.
  There is no Windows Application Packaging project; the layout is hand-assembled in PowerShell. A
  contributor following that printed command is not reproducing what CI does.
  [`../../CLAUDE.md`](../../CLAUDE.md) contradicts it in two places: line 17 ("MSIX: two steps — publish,
  then pack … there is no one-line MSIX publish here") and line 23, which records that the project declares
  no `WindowsPackageType` property, no Windows App SDK reference, and no `.wapproj`, so the flag is **inert**
  here.
- **The hand-assembled layout must be maintained by hand.** Any new content file that must ship in the package
  needs a new `Copy-Item` line; nothing derives the layout from the project.
- **Version lives in exactly one place and nothing validates it.** `Package.appxmanifest` carries
  `Version="1.0.0.0"`; no `.csproj` declares `<Version>` or `<AssemblyVersion>`, and `AssemblyInfo.cs` holds
  only a `[assembly: ThemeInfo(...)]` attribute. [`../../CHANGELOG.md`](../../CHANGELOG.md) is maintained
  separately under Keep a Changelog + SemVer. No CI step checks that the manifest version, the changelog, and
  any tag agree.
- **The `makeappx.exe` lookup is environment-dependent** — a recursive `Get-ChildItem` over the Windows Kits
  bin directory, filtered to `*\x64\*`, sorted descending, take first. Whatever SDK the hosted runner happens
  to carry determines which tool is used.
- **The publisher/subject coupling is documented only in a script comment.** Changing
  `Package.appxmanifest`'s `Publisher` without changing the signing certificate's subject (or the reverse)
  breaks signing and install, and no build step catches it.
- `--self-contained true` inflates the artifact with a full runtime copy.

### Neutral

- No `app.manifest` and no `requestedExecutionLevel` exist anywhere in the tree — the app runs non-elevated as
  a plain `WinExe`.
- The `security-scan` job (Semgrep) is a hard `needs:` dependency of `build-and-package`, so a SAST finding
  blocks packaging entirely.
- `scripts/New-DevCertificate.ps1` produces a self-signed code-signing certificate and installs it into
  `Cert:\LocalMachine\TrustedPeople` for local sideloading; its default password is hardcoded in the script,
  which is called out in [`../SECURITY.md`](../SECURITY.md).
- `Package.appxmanifest`'s `DisplayName` is "Folder File Control" — the user-facing name — while the identity
  name and repository name remain `WindowsFileManager`.

## Links

- [ADR-010](ADR-010-wpf-net8-desktop-shell.md) — the platform choice this packages
- [ADR-009](ADR-009-treat-warnings-as-errors.md) — the other CI gate that must pass before packaging
- [`../SECURITY.md`](../SECURITY.md) — `runFullTrust`, certificate handling, and CI secret exposure
- [`../DEV.md`](../DEV.md) — local publish and dev-certificate steps
- [`../../CHANGELOG.md`](../../CHANGELOG.md) — the separately-maintained version record
- Source: [`../../src/WindowsFileManager/Package.appxmanifest`](../../src/WindowsFileManager/Package.appxmanifest) ·
  [`../../.github/workflows/msix-pipeline.yml`](../../.github/workflows/msix-pipeline.yml) ·
  [`../../scripts/New-DevCertificate.ps1`](../../scripts/New-DevCertificate.ps1)
