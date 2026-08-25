# TrackDot

**A tiny Windows tray app that controls your music.** Click the icon, get a floating popover with the current track, artwork, and playback controls — no taskbar button, no window to manage.

![Logo](Assets/AppIcon.ico)
<br>

---

## The Story Behind It
 
Why build this? Simple: sheer frustration.

If you’ve ever switched between OS environments, you know that macOS and Linux make global media controls effortless. On Windows, however, controlling your background music on the fly without dedicated keyboard keys is surprisingly clunky. I wanted a quick, no-nonsense way to control playback globally without interrupting my workflow. That’s why this lives in cara-instan—scratching an itch with a fast, practical solution.

---

## What it does

TrackDot sits quietly in your system tray and surfaces **what's playing right now** through Windows' built-in media system (SMTC). It works with any player that publishes to SMTC — Spotify, Chrome, Edge, Apple Music, Tidal, Groove, Windows Media Player, and VLC (with SMTC enabled).

| | |
|---|---|
| 🎵 **Now playing** | Title, artist, source app, high-res artwork, and dynamic ambient glow |
| 🎮 **Transport** | Previous / Play–Pause / Stop / Next, plus Shuffle & Repeat modes |
| 🔊 **Volume & Seek** | Per-source slider + mute, 5% steps, and timeline scrubbing |
| 📌 **Always there** | Tray-only — no taskbar clutter, draggable, and pinnable |
| ⌨️ **Configurable Hotkeys** | Fully customizable system-wide and local popover shortcuts |
| 🎤 **Synced Lyrics & HUD** | Synced lyrics window + floating karaoke HUD overlay with Japanese romaji/furigana & translations |
| 🎮 **Discord Rich Presence** | Optional, privacy-first Discord RPC status with album art resolution & app whitelist |

---

## See it in action

### The popover
![TrackDot popover](docs/images/player-tracker-window.png)

*The floating popover — artwork, track info, progress bar, transport buttons, and volume.*

### Keyboard shortcuts
![TrackDot keyboard shortcuts window](docs/images/keyboard-shortcut.png)

*A built-in reference window for every shortcut — rebindable in Settings.*

### Synced lyrics (with Japanese support)
![TrackDot lyrics window](docs/images/lyrics-window.png)

*Time-synced lyrics with romaji readings above each kanji/kana word and optional translation lines.*

---

## Quick start

### 1. Install

Download the latest release from the [Releases](../../releases) page — pick **one**:

| Edition | Best for | What you get |
|---|---|---|
| **Installer** (`TrackDot-Setup-*.exe`) | Most users | Standard Windows installer; requires .NET 8 Desktop Runtime |
| **Portable** (`TrackDot-*-Portable.zip`) | USB sticks, locked-down machines | Self-contained ZIP; no install, no registry |

> The Installer is a small framework-dependent build. The Portable ZIP bundles everything and includes a `portable.dat` marker so settings stay next to the binary.

### 2. Launch

Double-click `TrackDot.exe` (or use the Start Menu shortcut from the installer).

**You won't see a window.** Look for the TrackDot icon in the notification area — usually at the right edge of the taskbar. It might be hidden under the `^` overflow arrow on Windows 11.

### 3. Click the icon

The popover appears above the tray. Play some music in any SMTC-aware player and the popover fills in. Click the icon again to hide.

---

## How to use it

### Tray icon

| Click | What happens |
|---|---|
| **Left-click** | Show / hide the popover |
| **Right-click** | Open the menu (Lyrics, Floating HUD, Keyboard Shortcuts, Settings, About, Exit) |
| **Drag the popover** | Move it anywhere — your position is remembered |
| **Pin icon in the popover** | Keep it open across clicks elsewhere |

### Key Features

#### 🎤 Synced Lyrics & Floating HUD Overlay
- **Lyrics Window:** Click the lyrics icon in the popover or tray menu. Fetches time-synced lyrics from [Unison](https://unison.boidu.dev) with automatic fallback to [LRCLIB](https://lrclib.net).
- **Floating HUD (Karaoke Overlay):** A lightweight, resizable overlay that stays above full-screen apps and games. Supports click-through locking (`Lock HUD`), adjustable font size (14px–60px), and opacity.
- **Japanese Furigana & Romaji:** Japanese tracks automatically display ruby furigana and romaji readings above kanji/kana words.
- **Secondary Translations:** Toggle secondary translated lyric lines when available.
- **Timing Offset Adjustment:** Fine-tune lyric synchronization on the fly with ±0.5s offset controls.
- **Local File Support:** Drag and drop `.lrc` or `.ttml` files directly into the lyrics window.

#### 🎮 Discord Rich Presence (RPC)
- Broadcast your current playback status directly to Discord via local IPC named pipes without requiring personal tokens or accounts.
- Displays high-resolution album artwork automatically resolved via iTunes Search API and Deezer fallback.
- **Privacy First:** Newly detected media applications are disabled by default. You choose exactly which apps share activity via the Allowed Source Applications whitelist.
- Configurable display options: elapsed/remaining time counters, album name, and paused status.

#### 🎨 Dynamic Album Art Glow & Palette Tinting
- Automatically extracts dominant colors from the current track's album art.
- Renders an ambient soft glow behind the popover and lyrics HUD.
- Subtly tints badges, highlights, and controls with the album's dynamic accent palette.

---

### Settings

**Tray menu → Settings** opens a comprehensive settings window organized into sections:

- **Appearance** — System / Dark / Light theme mode, and Dynamic Album Art Palette Tinting & Ambient Glow toggle.
- **Lyrics & Floating HUD** — Secondary translation toggle, click-through mode lock, HUD Opacity, Popover Opacity, and HUD Font Size slider.
- **Shortcuts & Global Hotkeys** — Enable/disable system-wide hotkeys, interactive shortcut recorder to rebind any hotkey, and a **Reset Defaults** button.
- **Discord Rich Presence & Privacy** — Master toggle for Discord RPC, options for timestamps, album name, pause state, and the **Allowed Source Applications** whitelist manager.
- **Startup** — Launch TrackDot automatically when you sign in to Windows.

All changes save instantly — no Apply button needed.

---

## Requirements

| | |
|---|---|
| **OS** | Windows 10 (build 19041 or later) or Windows 11 — 64-bit |
| **.NET 8 Desktop Runtime** | Pre-installed on modern Windows 11. Bundled in the Portable edition. |
| **Player** | Any SMTC-publishing source — Spotify, Apple Music, Tidal, Chrome/Edge, Groove, WMP, or VLC with SMTC integration enabled |

---

## What stays on your computer & Privacy

**TrackDot is built with a local-first, zero-telemetry architecture.** TrackDot has no user accounts, no telemetry SDKs, no analytics trackers, and no background reporting.

- **Local Storage:** Settings are stored in the Windows registry (`HKCU\Software\TrackDot`) — except in **Portable mode**, where everything lives in `settings.json` next to the executable.
- **Local IPC:** Discord RPC communicates purely over local Windows Named Pipes (`\\.\pipe\discord-ipc-0`) to your running Discord client.
- **Network Calls (Optional Features Only):**
  - **Lyrics:** Fetches lyrics from `unison.boidu.dev` and `lrclib.net` only when a lyrics window is open.
  - **Discord Artwork Lookup:** Queries `itunes.apple.com` and `api.deezer.com` only when Discord RPC is enabled for an allowed app to resolve album art URLs.
- **Crash Logs:** Written locally to `%LocalAppData%\TrackDot\crash.log` only if an unhandled error occurs. Never uploaded automatically.

For more details, see [`PRIVACY.md`](PRIVACY.md) and [`TERMS.md`](TERMS.md).

---

## Keyboard Shortcuts & Global Hotkeys

### Global Hotkeys (System-Wide, Customizable)

All global shortcuts can be customized in **Settings → Shortcuts & Global Hotkeys**:

| Action | Default Hotkey |
|---|---|
| Toggle Popover | `Alt + Shift + T` |
| Play / Pause | `Ctrl + Alt + Space` |
| Next Track | `Ctrl + Alt + Right` |
| Previous Track | `Ctrl + Alt + Left` |
| Stop Track | `Ctrl + Alt + .` |
| Volume Up / Down | `Ctrl + Alt + Up` / `Down` (5% steps) |
| Mute / Unmute | `Ctrl + Alt + M` |
| Open Settings | `Ctrl + Alt + S` |
| Toggle Lyrics Window | `Ctrl + Alt + L` |
| Toggle Floating Lyrics HUD | `Ctrl + Alt + H` |

### Local Popover Shortcuts (when the popover is focused)

| Action | Keys |
|---|---|
| Play / Pause | `Space`, `K`, or hardware Media Play/Pause |
| Next Track | `Right Arrow`, `L`, or hardware Media Next |
| Previous Track | `Left Arrow`, `J`, or hardware Media Previous |
| Stop Track | `S`, or hardware Media Stop |
| Volume Up / Down | `Up Arrow` / `Down Arrow` |
| Mute / Unmute | `M` |
| Toggle Pin | `P` |
| Open Settings | `O` or `,` |
| Hide Popover | `Esc` |

---

## For developers

<details>
<summary><strong>Build from source</strong></summary>

From a clean checkout in **git-bash** (or PowerShell / Command Prompt):

```bash
# 1. Restore + build Debug
dotnet restore TrackDot.sln
dotnet build TrackDot.sln -c Debug --no-restore

# 2. Run the full test suite (Debug)
dotnet test TrackDot.sln -c Debug --no-build

# 3. Confirm Release builds clean too
dotnet build TrackDot.sln -c Release

# 4. Launch from the built binary (NOT dotnet run)
./bin/x64/Release/net8.0-windows10.0.19041.0/TrackDot.exe
```

Expected test result: **Passed: 365, Failed: 0, Skipped: 0** (both Debug and Release).
</details>

<details>
<summary><strong>Project layout</strong></summary>

```
TrackDot/
├── App.xaml(.cs)               Composition root, tray resources, exception-logger bootstrap
├── Views/                       View layer
│   ├── MainWindow.xaml(.cs)     Floating popover window (ambient glow, marquee, transport)
│   ├── SettingsWindow.xaml(.cs) Settings dialog (theme, HUD, hotkeys, Discord RPC)
│   ├── HotkeysWindow.xaml(.cs)  Keyboard-shortcuts reference window
│   ├── LyricsWindow.xaml(.cs)   Synced lyrics window (Kawazu romaji/furigana, translation)
│   ├── LyricsHudWindow.xaml(.cs) Always-on-top floating lyrics HUD overlay (click-through)
│   └── AboutWindow.xaml(.cs)    About dialog
├── Commands/                    AsyncRelayCommand (ICommand wrapper with re-entrancy latch)
├── Converters/                  TimeSpanTextConverter, PlayPauseIconConverter, SliderFillWidthConverter
├── Models/                      Immutable records: snapshot, playback, capabilities,
│                                LyricLine, FuriganaSegment, AppThemeMode, HotkeyBinding,
│                                HotkeyAction, SourceAppSetting
├── Services/                    SMTC core, mapper, ThumbnailDecoder, ProgressInterpolator,
│                                SingleInstanceGuard, tray icon + handle, WindowPlacementService,
│                                UnhandledExceptionLogger, StartupService, registry adapter,
│                                AudioVolumeService, GlobalHotkeyService, LyricsService,
│                                DiscordRpcService, DiscordNamedPipeIpcClient, ArtworkLookupService,
│                                ColorExtractor, WindowSettingsService, ThemeService + WpfThemePaletteApplier,
│                                IPopoverHost, PortableMode, FileUnhandledExceptionSink
├── ViewModels/                  MainViewModel, SettingsViewModel, LyricsViewModel, HotkeysViewModel
├── TrackDot.csproj              TFM net8.0-windows10.0.19041.0, x64, UseWPF=true
├── TrackDot.sln
└── tests/TrackDot.Tests/        365 xUnit tests; EnableDefaultCompileItems=false
```
</details>

<details>
<summary><strong>Architecture highlights</strong></summary>

- **ThemeService + WpfThemePaletteApplier split.** `IThemeService` is a pure state machine (System / Dark / Light); `WpfThemePaletteApplier` subscribes and translates into the WPF palette swap. The split keeps the state machine unit-testable without WPF.
- **Dynamic Color Extraction & Ambient Glow.** `ColorExtractor` samples album artwork colors to calculate dominant and accent brushes, driving the ambient radial backdrop in both the popover and lyrics HUD.
- **Discord RPC via Local Named Pipes.** `DiscordRpcService` uses `DiscordNamedPipeIpcClient` to communicate directly with Discord's IPC socket (`discord-ipc-0`..`9`). High-resolution album artwork is resolved through `ArtworkLookupService` (iTunes Search API + Deezer fallback) and cached in-memory.
- **Privacy-First App Whitelist.** Discovered SMTC source applications are saved to registry/JSON settings but disabled by default for Discord Rich Presence until explicitly allowed by the user.
- **Interactive Global Hotkey Re-binding.** `GlobalHotkeyService` dynamically registers Win32 hotkeys via `RegisterHotKey` on the popover HWND, backed by serialized bindings and an interactive key-capture loop in `SettingsViewModel`.
- **IPopoverHost.** The popover implements `IPopoverHost` and the tray icon's `Show / Hide` decisions read `IsPopoverVisible` through the host — *not* from a cached bool.
- **Settings storage split by concern.** `StartupService` owns `HKCU\…\Run\TrackDot`; `WindowSettingsService` owns `HKCU\Software\TrackDot`. Portable mode consolidates both into `settings.json`.
- **Lyrics & HUD Multi-Format Support.** `LyricsService` parses synchronized LRC timestamps and TTML XML formats with automatic Kawazu Japanese romaji/furigana ruby rendering.
- **Volume lookup is PID-based, not AUMID-based.** A two-stage heuristic against `IAudioSessionManager2` matches the AUMID to a process and its renderer's display name — covers Spotify's renderer-process split and Electron players.
</details>

<details>
<summary><strong>Build pitfalls (development history)</strong></summary>

1. **Do not add `Microsoft.Windows.SDK.Contracts`** — `NETSDK1130` on .NET 5+; the TFM already carries WinRT projection.
2. **`dotnet test --no-build` after editing production + test files can hold a stale binary** — drop `--no-build` for one cycle if results look off.
3. **AsyncRelayCommand re-entrancy tests** — wrap `sut.Execute(null)` in `await Task.Run(...)` so the `async void` body escapes xUnit's `SynchronizationContext`.
4. **`BitmapDecoder` is ambiguous in WPF projects** — the decoder file aliases the WinRT one as `WinRTBitmapDecoder`. Do not remove the alias.
5. **`Application.MainWindow` collides with the `TrackDot.MainWindow` type name** — fully qualify as `TrackDot.MainWindow.IsShuttingDown = true`.
6. **`TaskbarIcon.IconSource` resolves via `pack://application:,,,/TrackDot;component/Assets/AppIcon.ico`** — bare paths only work at design time.
7. **`HKCU\…\Run` paths with spaces MUST be quoted** — the parser splits on whitespace otherwise.
8. **`Enable` opens the registry key TWICE** — once for `IsEnabled`, once for the write. Don't "optimize" by caching.
9. **Settings save-immediately contract** — every checkbox / slider / radio write goes straight to storage; failures must roll back the VM value AND show a `StatusMessage`.
10. **Portable mode is file-system based** — `File.Exists(<base>\portable.dat)`. To test on a developer machine, drop the marker into the bin output; removing it requires restart.
11. **Kawazu is a NuGet package whose IPA dictionary needs to land next to the binary** — the `CopyKawazuIpaDic` build target handles this. Do not `new KawazuConverter()` from `App.OnStartup` on a thread that has not loaded the dictionary.
12. **Global hotkeys need the popover opened once** — `RegisterHotKey` requires a window handle.
13. **`MediaControllerService` volume lookup is PID-based** — see Architecture above.
</details>

<details>
<summary><strong>Manual smoke matrix</strong></summary>

The automated xUnit suite covers contracts, lifecycle, and disposal — but it cannot exercise real SMTC sources, real displays, or real Windows behaviour. For the full integration matrix (Windows 10 / 11, Chrome / Edge / Spotify / VLC, multi-monitor, 100/125/150% DPI, 30-minute hidden soak, 15-minute playback soak, taskbar position), see [`docs/SMOKE_TEST.md`](docs/SMOKE_TEST.md).
</details>

<details>
<summary><strong>Publishing</strong></summary>

The end-to-end build is `scripts\build-release.ps1 -Version "0.1.0"` (PowerShell 7). It runs the tests, publishes, packages the Portable ZIP, and invokes Inno Setup if `ISCC.exe` is on the standard install path.

For a one-off local publish (framework-dependent):

```bash
dotnet publish TrackDot.csproj -c Release -r win-x64 --self-contained false \
    -o artifacts/win-x64-framework-dependent
```

**Do NOT enable trimming.** WPF / WinRT / reflection paths are trim-sensitive. Keep `PublishTrimmed=false` (the default).

See [`docs/RELEASE.md`](docs/RELEASE.md) for the full release pipeline.
</details>

---

## Known limitations

- **OS-selected session only.** TrackDot follows `GetCurrentSession()`. If Windows picks the wrong session (rare; usually when two SMTC sources are running and one has stale audio focus), TrackDot shows Windows' choice.
- **VLC needs SMTC enabled.** Toggle "Share media with Windows Media Player" in VLC's preferences, otherwise VLC won't appear as a source. Some VLC versions accept Previous / Next but ignore Stop.
- **Volume control requires an audio session.** Some SMTC sources (rare; mostly UWP media) do not own an audio session — the slider then shows the last value and writes are silently dropped.
- **Single source per process.** Manual source selection is deferred — the service contract already exposes `SourceAppUserModelId` so a picker could be added without changing the downstream contract.
- **Lyrics & Artwork lookup is best-effort.** Unison, LRCLIB, and iTunes/Deezer search APIs are community/public services; not every song has synced lyrics or matchable high-res artwork.

---

## License & Legal

- **License:** MIT. See [`LICENSE`](LICENSE) for details.
- **Terms of Service:** See [`TERMS.md`](TERMS.md) for terms regarding usage, third-party integrations, and disclaimers.
- **Privacy Policy:** See [`PRIVACY.md`](PRIVACY.md) for data handling and zero-telemetry commitments.

### Third-Party Components & APIs
- Tray icon component: [`Hardcodet.NotifyIcon.Wpf`](https://github.com/Hardcodet/notifyicon-wpf) 1.1.0 — MIT.
- Japanese romaji / furigana conversion: [`Kawazu`](https://github.com/Cutano/Kawazu) 1.1.4 — IPA dictionary is bundled with the package.
- Lyrics lookup: [Unison](https://unison.boidu.dev) (ODbL-1.0) and [lrclib.net](https://lrclib.net) — public community-maintained lyrics APIs.
- Album Artwork resolution: [iTunes Search API](https://itunes.apple.com) and [Deezer Search API](https://api.deezer.com) — public search endpoints for high-res cover art lookup.
- Discord Rich Presence: Discord IPC named pipes via local client integration.
