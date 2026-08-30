# SPEC-005 — File preview

<!-- Sync contract: `## Current behavior & invariants` is the section a behavior-changing commit must update in the same commit. -->

- Status: Current
- Feature area: shell/UI
- Ships in: 1.0.0 (commits `9d766b8`, `54388d5`) — the per-file `👁 Preview` button landed after 1.0.0 (`Unreleased`, commit `7123c0a`)

## What

Before deleting a duplicate, the user usually wants to see what it actually is. Two surfaces answer that: a small thumbnail beside every duplicate group in the list, and a full-height preview panel on the right of the window.

- **Mini thumbnails** — an 80-pixel thumbnail of each group's first file, rendered inline in the duplicate list. Native image formats are decoded directly; everything else goes through the Windows shell thumbnail provider, so videos, PDFs and Office documents get their real shell thumbnail. When no thumbnail can be produced, an emoji chosen by file extension is shown instead.
- **Preview panel** — shows the selected file's name and size plus a rendering that depends on the file type: an image, a video player, an audio player, a plain-text view, an "info card" for types that cannot be rendered, or an explicit "preview not available".
- **Media transport** — play / pause / stop buttons, a volume slider and a mute toggle for video and audio.
- **Auto-preview** — selecting a duplicate group previews its first file automatically; the behavior can be switched off, and closing the panel switches it off.

## Why

Two identical-by-hash files are, by definition, indistinguishable by content — but they are rarely indistinguishable by *purpose*. Seeing the picture, hearing the clip, or reading the first lines of the file is what turns "these two are the same" into "I know which one I can delete". Without a preview the user has to leave the app for Explorer on every group, which is slower than doing the whole cleanup by hand.

The mini thumbnail exists for the same reason at a lower cost: scanning a list of 200 groups is a visual task, and a 60-pixel picture answers "is this the photo set I meant?" without any click at all. The shell thumbnail provider is used rather than a bundled decoder so the app inherits whatever the machine can already render — including video frames and document first pages — with no extra dependencies.

## Scope

### In

- The `PreviewType` state machine and every classification branch that sets it.
- Extension-based file classification for preview purposes (the ten static extension sets).
- Text sniffing for unknown extensions, and the text preview's truncation rule.
- Image decoding limits, video/audio `MediaElement` wiring, and the info-card fallback.
- Mini thumbnail production and caching (`MiniPreviewConverter`) and the emoji fallback (`FileTypeIconConverter`).
- Media transport controls and the shared volume/mute model.
- Auto-preview, panel visibility, and the auto-play-off first-frame behavior.

### Out

- The `📂 Open` and `🗑 Delete` buttons that sit next to `👁 Preview` on each file row — [SPEC-004](SPEC-004-selection-and-file-actions.md).
- Which groups exist and in what order — [SPEC-001](SPEC-001-duplicate-detection.md) and [SPEC-002](SPEC-002-filtering-and-sorting.md).
- Persisting `IsMiniPreview` / `IsAutoPreview` / `IsAutoPlay` / `Volume` into the active profile — [SPEC-009](SPEC-009-settings-and-window-state-persistence.md).
- The analytics panel that occupies the *other* side column — [SPEC-006](SPEC-006-analytics-and-resource-monitor.md).
- The `?` help popup describing the preview toggles — [SPEC-010](SPEC-010-contextual-help.md).
- **Non-goal:** no editing, no rotation, no zoom, no full-screen, no seek bar. The preview is read-only and fixed-size.
- **Non-goal:** no document rendering. PDFs and Office files get a shell *thumbnail* in the list and an info card in the panel — never a paginated view.

## Current behavior & invariants

Preview state lives in `src/WindowsFileManager/ViewModels/MainViewModel.cs`; the thumbnail converter is `src/WindowsFileManager/Helpers/MiniPreviewConverter.cs`; the emoji fallback is `src/WindowsFileManager/Helpers/FileTypeIconConverter.cs`; transport controls are in `src/WindowsFileManager/Views/MainWindow.xaml.cs`. All four types are marked `[ExcludeFromCodeCoverage]`, so **no automated test covers this feature**; the only covered dependency is `ScannedFile.FormatFileSize`.

**Entry points**

| Trigger | Handler | Notes |
|---------|---------|-------|
| Per-file `👁 Preview` button | `PreviewFileCommand` → `PreviewFile(path)` | `CommandParameter="{Binding FilePath}"` |
| Selecting a duplicate group | `SelectedDuplicateGroup` setter | Calls `PreviewFile(value.FirstFilePath)` only when the value changed, is non-null, **and** `IsAutoPreview` is true |
| `✕ Close` in the panel header | `ClosePreviewCommand` → `ClosePreview()` | Also sets `IsAutoPreview = false` |
| `Mini Preview` toggle | `IsMiniPreview` | Controls the inline thumbnail border's visibility only |
| `File Preview` toggle | `IsAutoPreview` | Its setter mirrors the value into `IsPreviewVisible` |
| `Auto Play` toggle | `IsAutoPlay` | Read by `MediaElement_MediaOpened` |
| Thumbnail binding in the list | `MiniPreviewConverter.Convert` | `{Binding FirstFilePath, Converter={StaticResource MiniPreview}}` |
| Play / Pause / Stop / Mute / volume | code-behind click + `ValueChanged` handlers | `MediaElement` has no bindable transport, so these are not commands |
| Duplicate-list selection change | `DuplicateGroups_SelectionChanged` | Stops both players inside a `try`/`catch` |

**Rules — classification**

1. `PreviewFile(path)` returns immediately — changing **nothing** — when the path is null/empty or `File.Exists` is false. A stale preview therefore survives a click on a file that has since been deleted outside the app.
2. Otherwise it clears `PreviewImage`, `PreviewMediaUri` and `PreviewText`, then sets `PreviewFileName = fileInfo.Name` and `PreviewFileSize = ScannedFile.FormatFileSize(fileInfo.Length)` — the same formatter the rest of the app uses (`< 1 KB → "N B"`, then `F1 KB`, `F1 MB`, `F2 GB`), pinned by `ScannedFileTests.FormatFileSize_ShouldFormatCorrectly`.
3. Classification is a single `if`/`else if` chain over the lower-cased extension, evaluated **in this order** against ten `static readonly HashSet<string>` sets built with `StringComparer.OrdinalIgnoreCase`:

   | Order | Set | Entries | Result |
   |---|---|---|---|
   | 1 | `ImageExtensions` | 42 | `image` when WPF can decode it, else an `infocard` |
   | 2 | `VideoExtensions` | 32 | `video` |
   | 3 | `AudioExtensions` | 43 | `audio` |
   | 4 | `DocumentExtensions` | 55 | `infocard` — `📄 Document` |
   | 5 | `ArchiveExtensions` | 45 | `infocard` — `📦 Archive / Package` |
   | 6 | `FontExtensions` | 10 | `infocard` — `🔤 Font File` |
   | 7 | `ExecutableExtensions` | 19 | `infocard` — `⚙️ Executable / Binary` |
   | 8 | `DatabaseExtensions` | 14 | `infocard` — `🗄️ Database File` |
   | 9 | `ThreeDModelExtensions` | 16 | `infocard` — `🧊 3D Model` |
   | 10 | `TextExtensions` (201 entries) **or** `TryReadAsText` succeeds | — | `text` |
   | — | none of the above | — | `unsupported` |

   The `TextExtensions` initializer lists 203 string literals, but `.csv` and `.toml` each appear twice, so the `HashSet` holds 201 distinct extensions. The duplicates are inert — a harmless code smell, not a behavior difference.

4. `IsWpfNativeImage(ext)` gates real decoding to exactly fifteen extensions: `.jpg .jpeg .jif .jfif .jpe .png .bmp .dib .gif .tiff .tif .ico .wdp .hdp .jxr`. Any other image extension (`.webp`, `.svg`, `.heic`, RAW formats, …) falls to `SetInfoCardPreview("image", "🖼️", "Image File", ext)`.
5. Image decoding builds a `BitmapImage` with `CacheOption = OnLoad`, `DecodePixelWidth = 600`, then `Freeze()`. A decoder exception downgrades to `PreviewType = "unsupported"` with `PreviewText = "Image format not supported by WPF decoder"`.
6. Video and audio set `PreviewMediaUri = new Uri(filePath)`; the `MediaElement`s bind that property. No probing or codec check happens first — an unplayable file simply fails inside `MediaElement`.
7. `SetInfoCardPreview(type, icon, label, ext)` always sets `PreviewType = "infocard"` and `PreviewText = "{icon}\n{label}\n{EXT}\nUse 'Open' to view in default app"`. The `type` parameter is accepted and **never used**.
8. `TryReadAsText(path)` reads up to 8192 bytes; a zero-byte read returns false. It counts NUL bytes and, when `nullCount * 100 / bytesRead < 1`, calls `ReadTextPreview` and returns true. Any exception returns false. Because the integer division truncates, a file with under 1% NUL bytes counts as text.
9. `ReadTextPreview(path)` reads at most 50 000 chars through a default-encoding `StreamReader`; when the buffer fills exactly, it appends `"\n\n--- [Preview truncated] ---"`. A read failure sets `PreviewText = "[Unable to read file]"` while the type stays `text`.
10. `PreviewFile` ends by setting `IsPreviewVisible = true` **for every branch**, including `unsupported` — a preview click always opens the panel.
11. `ClosePreview()` sets `IsAutoPreview = false` (which cascades to `IsPreviewVisible = false` through that property's setter), `PreviewType = "none"`, and nulls the image, media URI, text, name and size. It is called by the `✕ Close` button **and** by `DeleteAllInGroup`, `DeleteSelectedFiles` and `MoveSelectedFiles` — so a bulk action silently turns auto-preview off ([SPEC-004](SPEC-004-selection-and-file-actions.md)).
12. The panel is a fixed `Width="380"` `Border` in `Grid.Column="2"`, shown by `IsPreviewVisible` through `BoolToVisibilityConverter`. Each `PreviewType` value drives one `DataTrigger` in the content grid; every renderer is present in the visual tree at all times and only its `Visibility` changes.

**Rules — mini thumbnails**

13. `MiniPreviewConverter.Convert` returns `null` for a non-string, empty, or non-existent path. It then consults a `static ConcurrentDictionary<string, ImageSource?> ThumbnailCache` keyed by path; a cached `null` means *tried and failed* and short-circuits any retry.
14. Extensions in `DirectLoadExtensions` — the same fifteen as `IsWpfNativeImage` — load as a `BitmapImage` with `DecodePixelWidth = 80`, `CacheOption = OnLoad`, frozen. A failure returns `null`.
15. Everything else goes through COM: `SHCreateItemFromParsingName` → `IShellItem` → cast to `IShellItemImageFactory` → `GetImage(80×80, flags 0)` → `Imaging.CreateBitmapSourceFromHBitmap`, frozen. `DeleteObject` on the HBITMAP and `Marshal.ReleaseComObject` on the shell item both run in `finally`. Both P/Invokes carry `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]`.
16. The converter result — success or `null` — is written back into the cache before returning, so each path is attempted at most once per cache generation.
17. `MiniPreviewConverter.ClearCache()` is called from exactly one place: the top of `ScanAsync`. The cache is otherwise never evicted and has no size bound.
18. In the list template the thumbnail `Image` is `60×60` (`Stretch="Uniform"`, `LowQuality` scaling) and collapses itself when `Source` is `null`; a sibling `TextBlock` bound through `FileTypeIconConverter` becomes visible in that case, showing an extension-derived emoji at font size 32. The whole mini-preview `Border` is gated on `IsMiniPreview`.

**Rules — media transport**

19. `MediaElement_MediaOpened` syncs the player's volume from the matching slider (`VideoVolumeSlider` for `VideoPlayer`, `AudioVolumeSlider` for `AudioPlayer`), then calls `Play()`. When `IsAutoPlay` is false it defers via `Dispatcher.BeginInvoke(DispatcherPriority.Loaded, …)` to `Pause()` and set `Position = 100 ms` — the delay exists so a first frame renders and the player reads as a still thumbnail.
20. Both `MediaElement`s use `LoadedBehavior="Manual"` and `UnloadedBehavior="Stop"`; the video player also sets `ScrubbingEnabled="True"` so the 100 ms seek actually paints.
21. Transport buttons call the players directly (`VideoPlayer.Play()`, `.Pause()`, `.Stop()` and the audio equivalents). They are `Click` handlers, not commands, because `MediaElement` exposes no bindable transport.
22. Both volume sliders two-way bind the **same** `MediaVolume` property (`Minimum=0`, `Maximum=1`, default `0.5`). Video and audio therefore share one volume setting: driving one slider moves the other, whose `ValueChanged` handler then applies the value to its own player and updates its glyph.
23. `VideoVolume_Changed` / `AudioVolume_Changed` set `<player>.Volume = e.NewValue` and swap the mute button content to `🔇` below `0.01`, `🔊` at or above it.
24. Mute is a code-behind toggle: above `0.01` it stashes the current slider value in `_videoVolumeBeforeMute` / `_audioVolumeBeforeMute` (each initialised to `0.5`) and sets the slider to `0`; otherwise it restores the stashed value. The two stashes are independent even though the sliders are not.
25. `DuplicateGroups_SelectionChanged` calls `VideoPlayer.Stop()` and `AudioPlayer.Stop()` on every selection change, wrapped in a `try`/`catch` because the elements may not be initialised yet.

**Invariants**

- `PreviewType` is always one of exactly seven string literals: `none` (the field's initial value), `image`, `video`, `audio`, `infocard`, `text`, `unsupported`. Adding a renderer means adding both the classification branch **and** a matching `DataTrigger` in `MainWindow.xaml`.
- Exactly one renderer is visible at a time: every renderer defaults to `Collapsed` and only its own `PreviewType` `DataTrigger` reveals it.
- A file is classified by extension alone, except for the final text branch, which is the only content-sniffing path.
- Every image the preview or the thumbnail produces is `Freeze()`d before it leaves its factory, so it is safe to hand to the UI thread from any thread.
- The thumbnail cache is keyed by full path and holds `null` for failures, so a file that cannot produce a thumbnail is never retried until the next scan.
- `IsAutoPreview` and `IsPreviewVisible` are kept consistent in one direction only: setting `IsAutoPreview` forces `IsPreviewVisible` to the same value, but `IsPreviewVisible` can be set independently (tab switching does exactly that).
- Defaults: `IsMiniPreview = true`, `IsAutoPreview = true`, `IsAutoPlay = false`, `MediaVolume = 0.5`, `PreviewType = "none"`, `IsPreviewVisible = false`. The profile defaults in `ProfileSettings` match (`IsMiniPreview = true`, `IsAutoPreview = true`, `IsAutoPlay` default `false`, `Volume = 0.5`) — pinned by `ProfileSettingsTests.Constructor_ShouldSetDefaults`.

**Edge cases**

| Case | Behavior |
|------|----------|
| Preview a file that no longer exists | `PreviewFile` returns before touching any state — the previous preview stays on screen. |
| Extensionless file | Falls through every set to the `TryReadAsText` sniff; text-like content previews as `text`, otherwise `unsupported`. |
| `.webp` / `.svg` / `.heic` / RAW image | Recognised as an image but not WPF-decodable, so it renders as the `🖼️ Image File` info card, not a picture. |
| Corrupt file with an image extension | Decoder throws; downgraded to `unsupported` with the "not supported by WPF decoder" message. |
| Text file larger than 50 000 chars | Truncated with a `--- [Preview truncated] ---` marker appended. |
| UTF-16 or UTF-32 text file | Its NUL padding pushes the sniff over the 1% threshold, so an unknown-extension UTF-16 file is classified `unsupported`. A known text extension still previews (and may render with mojibake, since `StreamReader` uses its default encoding detection). |
| Zero-byte file | `TryReadAsText` reads 0 bytes and returns false → `unsupported` unless the extension is in a known set. |
| Video codec the machine cannot play | `PreviewType` is still `video`; the `MediaElement` fails silently and the panel shows an empty player with working transport buttons. |
| `Auto Play` off | The clip opens, plays for one dispatcher turn, then pauses at 100 ms so a frame is visible. |
| Muting the video | Drives `MediaVolume` to 0, which moves the audio slider too and mutes both. Unmuting restores only the stash of the slider that was clicked. |
| Switching duplicate group while media plays | Both players are stopped by `DuplicateGroups_SelectionChanged` before the new preview loads. |
| Switching to the `Folder` tab | `TabControl_SelectionChanged` saves and then clears `IsPreviewVisible`; switching back restores the saved value. The `History` tab's header is a `TextBlock`, not a string, so it takes the *else* branch and **restores** the panel instead of hiding it. |
| Bulk delete or move completes | `ClosePreview()` runs, which turns `IsAutoPreview` off as a side effect. |
| Thumbnail for a file on a slow or disconnected network path | `Convert` runs synchronously on the UI thread; a slow shell provider blocks the list. There is no timeout. |
| A shell thumbnail extension crashes | Caught by the converter's `catch`, cached as `null`, and the emoji fallback is shown. The extension still ran in-process — see [`../SECURITY.md`](../SECURITY.md). |

**Not implemented**

- **The info-card text is wrong about the Open button.** It reads *"Use 'Open' to view in default app"*, but the only `Open` control on a duplicate row is `📂 Open`, which runs `explorer.exe /select,"<path>"` — it reveals the file in Explorer, it does not launch the default application ([SPEC-004](SPEC-004-selection-and-file-actions.md)).
- **`SetInfoCardPreview`'s `type` parameter is dead.** Every call passes a distinct category string (`"document"`, `"archive"`, …) and the method ignores all of them, always setting `PreviewType = "infocard"`. The categories survive only inside the rendered text.
- **The thumbnail cache is unbounded.** It is `static`, never evicted, and cleared only at the start of a scan. A long session over many scans is fine; a single very large scan grows it without limit.
- **There is no document, archive, or 3D-model renderer.** Those categories are classified and labelled but deliberately terminate in an info card.

## Links

- Decisions: [ADR-002 — hand-rolled MVVM](../adr/ADR-002-hand-rolled-mvvm.md) (why transport lives in code-behind rather than in commands), [ADR-010 — WPF on .NET 8](../adr/ADR-010-wpf-net8-desktop-shell.md) (why `MediaElement` and the WPF imaging stack are the rendering primitives)
- Module docs: [UI](../modules/ui.md) (converters, COM interop, code-behind), [Core](../modules/core.md) (`ScannedFile.FormatFileSize`, `ProfileSettings` preview defaults)
- Related specs: [SPEC-001](SPEC-001-duplicate-detection.md) · [SPEC-002](SPEC-002-filtering-and-sorting.md) · [SPEC-004](SPEC-004-selection-and-file-actions.md) · [SPEC-006](SPEC-006-analytics-and-resource-monitor.md) · [SPEC-009](SPEC-009-settings-and-window-state-persistence.md) · [SPEC-010](SPEC-010-contextual-help.md)
- Guardrails: [`../SECURITY.md`](../SECURITY.md) (shell thumbnail extensions run in-process; the cache holds every previewed path)
- Tests: `tests/WindowsFileManager.Tests/Models/ScannedFileTests.cs` (the size formatter) · `tests/WindowsFileManager.Tests/Models/ProfileSettingsTests.cs` (the persisted preview defaults)
