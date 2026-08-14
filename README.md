# TrackDot

A lightweight Windows 10/11 tray application that shows the **currently playing** media session — title, artist, source, artwork, timeline, volume — and exposes Previous / Play-Pause / Stop / Next / Volume / Mute commands, without a taskbar button.

TrackDot reads the OS-selected session through the Windows **System Media Transport Controls (SMTC)** API and binds to it through WPF. It follows `GetCurrentSession()` (the same session the OS volume / media keys target). Manual source selection is out of MVP scope — see [Known limitations](#known-limitations).

Around the SMTC core, TrackDot ships a small suite of companion surfaces — a **synced lyrics window** with Japanese romaji / furigana, **system-wide global hotkeys** (toggle, transport, volume), **popover opacity**, **drag-to-move** popover, **dark / light / system theme**, and a packaged **Installer** + **Portable** distribution.

---

## Features

- **Tray-first.** No taskbar button; no window opens until you click the tray icon.
- **One popover, ~360 × 128 logical pixels.** Borderless, anchored above the notification area on the monitor containing the taskbar. Re-anchors on every show so resolution / monitor swaps are picked up automatically. Drag-to-move via the popover body; "Pin" toggle in the header keeps it open across blur.
- **Metadata + artwork.** Title, artist, source-app label, artwork (decoded to WPF `ImageSource` via `Windows.Graphics.Imaging`, frozen for cross-thread binding).
- **Smooth progress.** 250 ms local interpolation only while the popover is visible *and* playback is `Playing`. The interpolation timer stops on hide / pause / no-session.
- **Capability-gated transport.** Previous / Play-Pause / Stop / Next disable individually when the source reports the capability unsupported. Failed `Try*Async` returns trigger a playback refresh so the buttons re-evaluate.
- **Per-app volume + mute.** Reads / writes the audio session volume and mute state of the *current SMTC source* (matched by PID via `IAudioSessionManager2`); 5% volume steps. Failure-safe: if the COM lookup misses, the volume slider just shows the last value and write is dropped.
- **Synced lyrics window.** Optional companion window: pulls time-synced LRC from `lrclib.net`, falls back to plain lyrics, and renders Japanese as romaji + per-segment furigana via [Kawazu](https://github.com/herlandroando/Kawazu). Position, size, opacity, topmost, furigana visibility, and "was open" state persist across runs.
- **System-wide global hotkeys.** `Alt+Shift+T` toggles the popover; `Ctrl+Alt+Space` / `+←` / `+→` / `+.` / `+↑` / `+↓` / `+M` / `+S` drive Play-Pause / Prev / Next / Stop / Volume ±5% / Mute / Settings. Registered with `RegisterHotKey` against the popover's HWND. Toggle in Settings → Shortcuts.
- **Theming.** Dark / Light / System (follows Windows app mode). Theme is a pure state machine (`ThemeService`) and a WPF palette applier (`WpfThemePaletteApplier`) — see [Architecture notes](#architecture-notes).
- **Single instance.** A named mutex (`Local\TrackDot.SingleInstance.v1`) prevents duplicate tray icons.
- **Opt-in launch at sign-in.** Stores a quoted executable path under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (no admin prompt).
- **Portable mode.** When the executable directory contains a `portable.dat` marker, all settings live in `settings.json` next to the binary and no registry is touched. See [Portable mode](#portable-mode).
- **Installer & Portable editions.** Two official distributions: framework-dependent **Installer** (Inno Setup, per-user, Add/Remove Programs entry) and self-contained **Portable** ZIP. See [docs/RELEASE.md](docs/RELEASE.md).
- **Crash log.** Unhandled exceptions on the WPF dispatcher, the AppDomain, and the `TaskScheduler` are appended to `%LocalAppData%\TrackDot\crash.log` with channel tag + full chain.

---

## Prerequisites

| | |
|---|---|
| OS | Windows 10 build 19041+ or Windows 11 (x64) |
| .NET runtime | `Microsoft.WindowsDesktop.App 8.0.x` (already installed on a current Windows 11 machine; bundled in the framework-dependent artifact) |
| .NET SDK (for development) | `dotnet 8.0.x` |
| Players | Any SMTC-publishing source — Chrome / Edge, Spotify desktop, Windows Media Player, Groove, **VLC with SMTC integration enabled** (see [Known limitations](#known-limitations)) |

The project is pinned to `net8.0-windows10.0.19041.0`, x64 only, with `<UseWPF>true</UseWPF>`. The tray icon is provided by [`Hardcodet.NotifyIcon.Wpf` 1.1.0](https://github.com/Hardcodet/notifyicon-wpf) (WPF-native, no WinForms dependency). Japanese romaji / furigana conversion in the lyrics window is provided by [`Kawazu` 1.0.0](https://github.com/herlandroando/Kawazu) — its IPA dictionary is copied to `<bin>\IpaDic\` by a `CopyKawazuIpaDic` build target.

Lyrics lookups talk to the public [lrclib.net](https://lrclib.net) API (`GET /api/get` then `GET /api/search`); outbound HTTPS is required **only when the lyrics window is actually opened**. Everything else (popover, transport, settings, hotkeys) is fully offline.

---

## Build, test, run

From a clean checkout in **git-bash** (or any POSIX shell):

```bash
cd "C:/Users/Herlandro Ando/Documents/Ando/sites_win/TrackDot"

# 1. Restore + build Debug (development).
dotnet restore TrackDot.sln
dotnet build TrackDot.sln -c Debug --no-restore

# 2. Run the full test suite (Debug).
dotnet test TrackDot.sln -c Debug --no-build

# 3. Confirm Release builds clean too.
dotnet build TrackDot.sln -c Release

# 4. Launch from the built binary (NOT `dotnet run`).
./bin/x64/Release/net8.0-windows10.0.19041.0/TrackDot.exe
```

**Expected output from step 2:** `Passed: 273, Failed: 0, Skipped: 0`. Both Debug and Release are 273 / 273. The suite covers 27 modules: smoke, snapshot contracts, mapper, decoder, command dispatch, service guards, progress interpolation, view-model (main + lyrics + settings), single-instance, tray-icon service, placement, exception logger, startup service, asset-resource, **global-hotkey service**, **hotkeys window**, **lyrics service**, **lyrics view-model**, **theme service**, **about window**, **session picker**, **volume control**, **portable mode**, **play-pause icon converter**, **WPF test bootstrap / pack-URI contract**, **popover show-raise contract**, **view-model lifecycle**.

**Do not run the published executable from a build directory whose path contains a trailing separator.** The launch-at-sign-in detection path (`StartupService.IsEnabled`) compares with `OrdinalIgnoreCase` + trimmed trailing separators, but the Run-key parser on Windows does *not* — paths stored without a trailing separator are the canonical form.

### What "tray-first" means in practice

The application is started by `OnExplicitShutdown` mode. **There is no main window at startup.** The tray icon is the only visible artifact. Left-click toggles the popover; right-click opens the context menu.

| Tray action | Behaviour |
|---|---|
| Left-click on the icon | Toggle the popover (show / hide). Showing re-anchors it to the current work area. |
| Right-click on the icon | Open the context menu — **Keyboard Shortcuts**, **Settings**, **About TrackDot**, separator, **Exit TrackDot**. |
| Left-click outside the popover while it is open | Hide the popover (`Deactivated`). Tray stays alive. (Suppressed when popover is pinned.) |
| `X` button on the popover | Hide the popover (`Window_Closing` cancels the close). Tray stays alive. |
| `Alt + F4` on the popover | Same as `X` — hide, do not exit. |
| Tray menu → **Exit TrackDot** | Dispose the media service, dispose the view-model, close the popover, lyrics and settings windows, release the named mutex, dispose the tray icon, then `Application.Shutdown()`. The tray icon disappears from the notification area. |
| Launch `TrackDot.exe` while another instance is running | The second process sees the named mutex as not-acquired and exits with code 1 — no second tray icon. |

### Settings window

Tray menu → **Settings** opens a single-instance `SettingsWindow`. It has four sections (top to bottom):

| Section | Controls | Storage |
|---|---|---|
| **Appearance** | Radio buttons: System default / Dark / Light | `HKCU\Software\TrackDot` (DWORD) |
| **Popover Opacity** | Slider 20 – 100% (snap to 5%) + live "xx%" label | `HKCU\Software\TrackDot\OpacityPercent` (DWORD) |
| **Shortcuts** | Checkbox: Enable system-wide global hotkeys (`Ctrl+Alt+Space` etc.) | `HKCU\Software\TrackDot\EnableGlobalHotkeys` (DWORD) |
| **Startup** | Checkbox: Launch TrackDot at sign-in | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TrackDot` (REG_SZ, quoted exe path) |

All settings are **save-immediately** (no Apply button). A `StatusMessage` row under Startup rolls the checkbox back and surfaces the exception on registry failure.

Closing the Settings window (X / Esc / **Close**) hides it rather than disposing it — your position is preserved across opens. The window is disposed when the application exits.

### About window

Tray menu → **About TrackDot** opens a single-instance `AboutWindow` with the app name, version, and a link to the project repository. Same hide-on-close lifecycle as Settings.

---

## Tray controls reference

The popover binds to four transport commands. Each button is disabled individually when the active SMTC source reports the capability unsupported:

| Button | Capability flag | Source player note |
|---|---|---|
| Previous | `CanGoPrevious` | Spotify supports it; YouTube (Chrome) usually does not. |
| Play / Pause | `CanPlay ∥ CanPause` | The glyph flips on every authoritative snapshot — **the button does not decide play-vs-pause from its own click**, the service reads the current `MediaPlaybackState` and dispatches `TryPlayAsync` or `TryPauseAsync` accordingly. |
| Stop | `CanStop` | Some sources disable Stop (e.g. UWP media). |
| Next | `CanGoNext` | Same pattern as Previous. |

The progress bar advances by **local interpolation** between SMTC timeline events. While hidden or paused, no timer ticks. While playing and visible, a 250 ms `DispatcherTimer` reads `ProgressInterpolator.Evaluate(...)` and raises `PropertyChanged` on `PositionSeconds`. The bar is clamped to `[0, DurationSeconds]` and freezes at `EndTime` if the player stops reporting updates.

---

## Keyboard Shortcuts & Global Hotkeys

TrackDot provides system-wide global hotkeys (when enabled in Settings) and local popover key bindings:

### Global Hotkeys (System-Wide)

| Action | Hotkey | Description |
|---|---|---|
| Toggle Popover | `Alt + Shift + T` | Show / hide the TrackDot popover window |
| Play / Pause | `Ctrl + Alt + Space` | Toggle media playback globally |
| Next Track | `Ctrl + Alt + Right` | Skip to next track globally |
| Previous Track | `Ctrl + Alt + Left` | Skip to previous track globally |
| Stop Track | `Ctrl + Alt + .` | Stop track playback globally |
| Mute / Unmute | `Ctrl + Alt + M` | Toggle audio mute globally |
| Volume Up / Down | `Ctrl + Alt + Up / Down` | Adjust volume by 5% globally |
| Open Settings | `Ctrl + Alt + S` | Open TrackDot Settings window |

### Local Popover Shortcuts (When Popover is Focused)

| Action | Key(s) |
|---|---|
| Play / Pause | `Space`, `K`, or hardware `Media Play/Pause` |
| Next Track | `Right Arrow`, `L`, or hardware `Media Next` |
| Previous Track | `Left Arrow`, `J`, or hardware `Media Previous` |
| Stop Track | `S`, or hardware `Media Stop` |
| Volume Up / Down | `Up Arrow` / `Down Arrow` |
| Mute / Unmute | `M` |
| Toggle Pin | `P` |
| Open Settings | `O` or `,` |
| Hide Popover | `Esc` |

---

## Launch at sign-in

TrackDot stores a quoted executable path under:

```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
Value name:  TrackDot
Value data:  "<full path to TrackDot.exe>"
```

`HKCU` requires no elevation — TrackDot never writes to `HKLM` and never shows a UAC prompt.

The detection path (`IsEnabled`) compares the stored value against the current executable with:

- `OrdinalIgnoreCase` (Windows file paths are case-insensitive),
- a single-pair-of-quotes strip (so quoted and unquoted stored values both register as ours),
- a trailing-separator trim (so `C:\App\` and `C:\App` match).

The "current executable" comes from `Environment.ProcessPath` (the .NET 6+ replacement for `Process.MainModule.FileName`). When this returns `null` (rare; mostly unusual test hosts) the toggle refuses with a status message rather than writing an empty value.

Verify with `regedit` or `Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' | Select-Object -ExpandProperty TrackDot`. The stored path should point at **the published binary**, never `dotnet.exe` or a development DLL.

---

## Portable mode

TrackDot supports a fully-portable layout for the **Portable Edition** ZIP. The runtime checks for `portable.dat` in the executable's directory (`AppDomain.CurrentDomain.BaseDirectory`):

```csharp
// Services/PortableMode.cs (excerpt)
public static class PortableMode
{
    public static readonly bool IsPortable =
        File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "portable.dat"));
}
```

When `IsPortable == true`:

- **No registry is written or read.** All settings — Pin, Opacity, GlobalHotkeys, Lyrics (visible, opacity, topmost, furigana, position, size) — persist to `settings.json` next to the executable. Reads tolerate a missing / corrupt file (defaults are returned). Writes are serialised through a `lock`.
- **`Launch at sign-in` is a no-op** — there is no fixed install path to register.
- **Crash log path is also re-rooted** to `<base>\logs\crash.log` (see `FileUnhandledExceptionSink`).
- **The Installer Edition does *not* include** `portable.dat` and runs the normal registry-backed code path.

Use the portable edition for USB sticks, locked-down workstations, or testing a new build without polluting your real settings.

---

## Lyrics window

A second single-instance window (`LyricsWindow`) shows time-synced lyrics for the current track:

- **Source.** Lyrics are fetched from the public [lrclib.net](https://lrclib.net) API — first `/api/get` with `track_name`, `artist_name`, `album_name`, `duration`; on miss, `/api/search` with a scored best-match (synced preferred; Japanese lyrics preferred when the query contains kana / kanji; ±2 / ±5 / ±10 s duration buckets score +60 / +40 / +20; >20 s mismatch costs −50).
- **Cache.** Successful and failed fetches are cached per `(artist, title, album)` key for the process lifetime. The cache key is culture-insensitive (OrdinalIgnoreCase).
- **Japanese rendering.** Detected via `[぀-ゟ゠-ヿ一-鿿]`. Each Kanji/Kana word is converted twice via [Kawazu](https://github.com/herlandroando/Kawazu) (Spaced Romaji + per-segment reading) and rendered as `<ruby>` segments. Conversion failures fall back to the raw text + empty reading.
- **Sync.** `LyricsViewModel` ticks on the same `DispatcherTimer` cadence as progress; the active line is the largest timestamp ≤ `PositionSeconds`. Lines are clamped to a fixed window centred on the active line; non-active lines dim.
- **Persistence.** Visible, opacity (20 – 100%), topmost, furigana-on/off, and rectangle (left / top / width / height) are persisted via `IWindowSettingsService` (registry in installed mode, JSON in portable mode). "Was open before exit" is the canonical visibility default at next launch.
- **Failure safety.** Network / parse / Kawazu errors are swallowed; the lyrics window simply shows the placeholder. The popover never depends on lyrics being available.

Open / close behaviour matches the other companion windows (hide-on-close; disposed on app exit).

---

## Architecture notes

- **ThemeService + WpfThemePaletteApplier split.** `IThemeService` is a pure state machine: it owns the current `AppThemeMode` (System / Dark / Light) and exposes a `ThemeChanged` event. `WpfThemePaletteApplier` subscribes and translates that into the actual WPF `Application.Resources` palette swap (`PanelBrush`, `TextBrush`, `MutedBrush`, `BadgeBackgroundBrush`, …). The split keeps the state-machine unit-testable without WPF and lets the applier be swapped (e.g. for the `WpfTestAssemblyInit` boot).
- **IPopoverHost.** The popover implements `IPopoverHost` and the tray icon's `Show / Hide` decisions read `IsPopoverVisible` through the host — *not* from a cached bool on the tray service. A stale cache was the root cause of "I have to click the tray icon twice"; see commit `2941f2e`.
- **Settings storage is split by concern.** `StartupService` owns `HKCU\...\Run\TrackDot` (launch-at-sign-in only); `WindowSettingsService` owns `HKCU\Software\TrackDot` (Pin / Opacity / GlobalHotkeys / Lyrics). The portable path consolidates both into `settings.json`.
- **Global hotkeys are pinned to the popover HWND.** `GlobalHotkeyService` registers each `RegisterHotKey` against the popover's `HwndSource`. The `WM_HOTKEY` WndProc hook dispatches into `MainViewModel`. A popover that was never opened has no HWND → global hotkeys silently no-op. The shortcut is therefore gated on the popover having been constructed at least once.
- **Lyrics is the only network feature.** `LyricsService` is the only place that constructs an `HttpClient`. Everything else is in-process. The popover, the tray icon, and the transport commands are 100% offline; you can disable lyrics by never opening the lyrics window and it will not be touched.

---

## Project layout

```
TrackDot/
├── App.xaml(.cs)               Composition root, tray resources, exception-logger bootstrap
├── Views/                       View layer (renamed from top-level XAML/code-behind)
│   ├── MainWindow.xaml(.cs)     Floating popover window (DataContext set by composition root)
│   ├── SettingsWindow.xaml(.cs) Settings dialog (single-instance, hidden on close)
│   ├── HotkeysWindow.xaml(.cs)  Keyboard-shortcuts reference window
│   ├── AboutWindow.xaml(.cs)    About dialog
│   └── LyricsWindow.xaml(.cs)   Synced lyrics window (Kawazu romaji/furigana)
├── AssemblyInfo.cs
├── Assets/
│   ├── AppIcon.ico             32×32 PNG-in-ICO used by the tray icon
│   └── PlaceholderArt.png      1×1 transparent PNG (kept for the build-time resource check)
├── Commands/
│   └── AsyncRelayCommand.cs    ICommand wrapper with re-entrancy latch
├── Converters/
│   ├── TimeSpanTextConverter.cs    TimeSpan → "m:ss" / "h:mm:ss"
│   └── PlayPauseIconConverter.cs   MediaPlaybackState → play / pause glyph
├── Models/                      Immutable records: snapshot, playback, capabilities,
│                                LyricLine, FuriganaSegment, AppThemeMode
├── Services/                    IMediaControllerService, MediaControllerService, mapper,
│                                ThumbnailDecoder, ProgressInterpolator, SingleInstanceGuard,
│                                tray icon + handle, WindowPlacementService,
│                                UnhandledExceptionLogger, StartupService, registry adapter,
│                                AudioVolumeService (CoreAudio IAudioSessionManager2),
│                                GlobalHotkeyService (RegisterHotKey + WndProc hook),
│                                LyricsService (lrclib.net + Kawazu), WindowSettingsService,
│                                ThemeService (pure state machine) + WpfThemePaletteApplier,
│                                IPopoverHost, PortableMode, FileUnhandledExceptionSink
├── ViewModels/                  MainViewModel, SettingsViewModel, LyricsViewModel,
│                                IUiTicker + DispatcherUiTicker
├── TrackDot.csproj              TFM net8.0-windows10.0.19041.0, x64, UseWPF=true
├── TrackDot.sln
└── tests/TrackDot.Tests/        273 xUnit tests; EnableDefaultCompileItems=false
```

The test project uses `EnableDefaultCompileItems=false` with explicit `<Compile Include="..." />` entries. The application csproj excludes `tests\**\*.cs` from the WPF design-time temp build (see [Build pitfalls](#build-pitfalls)).

---

## Build pitfalls

These are the ones the development history burned into the codebase. If a fresh checkout behaves oddly, check these first:

1. **Do not add `Microsoft.Windows.SDK.Contracts` as a package.** It errors with `NETSDK1130` on .NET 5+ because the TFM already carries WinRT projection through `Microsoft.Windows.SDK.NET.Ref`. WPF + WinRT (`GlobalSystemMediaTransportControlsSessionManager`, `Windows.Graphics.Imaging.BitmapDecoder`) works out of the box on `net8.0-windows10.0.19041.0`.

2. **`dotnet test --no-build` after editing both production and test files can hold a stale binary.** If a test seems to be running against an old version, drop `--no-build` for one cycle so the test runner rebuilds. WPF test changes especially are sensitive (pack URI + STA bootstrap).

3. **AsyncRelayCommand re-entrancy tests.** The latch (`RunningForTest`) is read by polling. The test must drain the latch via a polling loop on `RunningForTest == 0`, **not** by counting `Task.Yield()` pumps — Release JIT timing varies. New re-entrancy tests must wrap `sut.Execute(null)` in `await Task.Run(() => sut.Execute(null))` so the `async void` body escapes xUnit's `SynchronizationContext`. Direct calls are flaky.

4. **`BitmapDecoder` is ambiguous in WPF projects.** The decoder file aliases the WinRT one as `WinRTBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;`. Do not remove the alias.

5. **`Application.MainWindow` (the instance property) collides with the `TrackDot.MainWindow` type name** inside `App.xaml.cs`. Anywhere the composition root touches both, fully qualify: `TrackDot.MainWindow.IsShuttingDown = true`. The type wins over the property when the property is not set, so the type path always needs qualifying from inside `App`.

6. **`TaskbarIcon.IconSource` resolves via `pack://application:,,,/TrackDot;component/Assets/AppIcon.ico`.** A bare path like `Assets/AppIcon.ico` only works at design time. The csproj embeds the asset as `<Resource Include="Assets\AppIcon.ico" />`; runtime resolution requires the pack URI. The same pack URI applies to every other embedded asset (SettingsWindow icon, AboutWindow icon, etc.).

7. **`HKCU\...\Run` paths with spaces MUST be quoted.** Every per-user install path on Windows contains a space. The Run-key parser splits on whitespace inside an unquoted string. The stored value must be `"C:\Path\To\App.exe"` (surrounded by double quotes). `IsEnabled` accepts both quoted and unquoted stored values (third-party tools frequently write the unquoted form).

8. **`Enable` opens the registry key TWICE — once for the `IsEnabled` read, once for the write.** Idempotency means re-reading on every call, so a foreign-write between the user's prior `Disable` and current `Enable` is picked up. Do not "optimize" by caching the read result.

9. **`SettingsViewModel` save-immediately contract.** Every checkbox / slider / radio write goes straight to `IWindowSettingsService` and then to the registry. A failing write must roll back the VM value AND show a `StatusMessage`. Tests assert both halves (state unchanged, error surfaced).

10. **Portable mode toggle is file-system based, not a runtime flag.** The check is `File.Exists(<base>\portable.dat)`. To test the portable code path on a developer machine, drop an empty `portable.dat` into `bin\x64\Debug\net8.0-windows10.0.19041.0\` and launch the binary from there. Removing it requires restart — there is no live reload.

11. **Kawazu is a NuGet package whose IPA dictionary needs to land next to the binary.** `LyricsService` references `Kawazu.KawazuConverter` from the `Kawazu` 1.0.0 NuGet. The csproj target `CopyKawazuIpaDic` copies the package's `IpaDic\*` files into `<TargetDir>\IpaDic\` after Build / Publish. **Do not** try to `new KawazuConverter()` from `App.OnStartup` on a thread that has not loaded the dictionary — the lazy initialisation in `LyricsService.GetKawazuConverter()` is what keeps the popover startup snappy. If you see "could not find IpaDic" at runtime, the package's `contentFiles\any\...` layout changed upstream — check `PkgKawazu\content*` paths in the csproj.

12. **Global hotkeys need the popover to have been opened once.** `RegisterHotKey` requires a window handle. The popover constructs lazily — until the user opens it once, no HWND exists and `GlobalHotkeyService.Register` silently no-ops. The smoke-test path therefore clicks the tray icon before exercising hotkeys.

13. **`MediaControllerService` volume lookup is PID-based, not AUMID-based.** SMTC exposes `SourceAppUserModelId` but not PID. We enumerate every audio session on the default render endpoint, read each `IAudioSessionControl2.GetProcessId` and `IAudioSessionControl2.GetDisplayName`, and run a two-stage heuristic match against the AUMID: (a) primary — `.exe`-stem equality or reverse-DNS segment-substring against the process name; (b) secondary — same reverse-DNS segment-substring rules applied to the OS-set session display name. Stage (b) covers players whose audio is produced by a separate renderer process whose name has no overlap with the AUMID (Spotify's `SpotifyRenderer.exe` vs AUMID `com.spotify.client`, some Electron-based players, etc.). A player that fails both stages still fails — `VolumeControlTests` and `AudioVolumeMatcherTests` cover both the success and failure paths.

---

## Privacy

**TrackDot does not phone home and does not collect telemetry** beyond one opt-in feature: the synced lyrics window, which fetches `track_name`, `artist_name`, `album_name`, and `duration` from `lrclib.net` to look up lyrics. The request carries a static `User-Agent` (`TrackDot/0.1.0 (https://github.com/herlandroando/TrackDot)`); nothing else is sent, and the request never includes your IP-bound identifiers beyond what HTTP exposes by default. No analytics SDK, no telemetry key, no auto-update beacon.

- All SMTC metadata (title / artist / album / artwork / timeline / volume) is read from the OS-selected session and stays in-process.
- The only files written outside the application directory are:
  - `%LocalAppData%\TrackDot\crash.log` — unhandled-exception capture (or `<base>\logs\crash.log` in portable mode). You can delete it; the application recreates it on next failure.
  - `<base>\settings.json` — **portable mode only** (settings snapshot). Not written in installer mode.
- The only registry values written are:
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TrackDot` — when the user opts in via the Settings → Startup checkbox.
  - `HKCU\Software\TrackDot\*` — pin, opacity, global-hotkeys toggle, lyrics settings. Always written by the save-immediately settings UI.

No analytics. The Settings window does not require internet. The popover does not require internet. The lyrics window is the only component that touches the network, and it only talks to `lrclib.net`.

---

## Known limitations

- **OS-selected session only.** TrackDot follows `GetCurrentSession()`. If the OS picks the wrong session (rare — usually when two SMTC sources are running and one has stale audio focus), TrackDot shows the OS's choice. Manual source selection is deferred — see plan §7 (HANDOFF.md, Decision point 3).
- **DWM rounded corners on Win11 22H2+.** On supported hosts the popover (`MainWindow.xaml.cs:49-64`) and the lyrics window (`LyricsWindow.xaml.cs:92-100`) call `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND)` from `OnSourceInitialized` and drop the layered-alpha HWND path (`AllowsTransparency = false`, `Background = PanelBrush`). On older builds the DWM call returns `NotSupportedOnThisOs` and the XAML defaults (`AllowsTransparency="True"` + `Background="Transparent"` + the inner rounded `Border` `CornerRadius="14"`) are preserved unchanged. The version detector (`Services/DwmInterop.cs`, via `RtlGetVersion` from `ntdll.dll`) is host-conditional — `Environment.OSVersion` lies on Win10, so the helper bypasses it. See `docs/SMOKE_TEST.md` §5 and `.hermes/plans/2026-08-13_134600-dwm-corner-preference-migration.md` for the migration design.
- **VLC SMTC is version-dependent.** The "Share media with Windows Media Player" / SMTC integration preference must be enabled in VLC's preferences; otherwise VLC will not appear as a source. Some VLC versions accept Previous / Next but ignore Stop.
- **`Assets/PlaceholderArt.png` is a 1×1 transparent PNG.** It IS bound as a fallback at `Views/MainWindow.xaml:50-58` via `<Binding Path="Artwork"><Binding.FallbackValue><BitmapImage UriSource="pack://application:,,,/TrackDot;component/Assets/PlaceholderArt.png" /></Binding.FallbackValue></Binding>`. The 1×1 transparent pixel stretches under `Stretch="UniformToFill"`, so when `Artwork` is null the placeholder contributes nothing visible and the artwork border background (`#26282E`, `ArtworkBackgroundBrush` from `App.xaml:26`) is what the user actually sees — i.e. the popover looks empty by design. Swapping the placeholder for a real visible glyph (an 8th-note or an opaque accent disc) is the bounded follow-up if the empty-state looks worse than expected.
- **Single source per process.** The composition root wires one `MediaControllerService`. A future source picker is out of MVP scope; the service contract already exposes the source's `SourceAppUserModelId` so a picker could be added without changing the downstream contract. (See `tests/TrackDot.Tests/SessionPickerTests.cs` for the contract surfaces already covered.)
- **Lyrics lookup is best-effort.** `lrclib.net` is community-maintained; not every track has synced lyrics, Japanese tracks occasionally surface an English translation instead, and the heuristic scorer is intentionally permissive (duration-tolerance ±20 s before penalty). The lyrics window is decoration, not a contract.
- **Volume control requires a session with a matching audio endpoint.** Some SMTC sources (rare; mostly UWP media) do not own an audio session. The volume slider then shows the last value and writes are silently dropped.

---

## Publishing

TrackDot ships in **two editions** (see [docs/RELEASE.md](docs/RELEASE.md) for the full release pipeline):

| Edition | Format | Runtime | Settings storage | Auto-update |
|---|---|---|---|---|
| **Installer** (`TrackDot-Setup-v{ver}-x64.exe`) | Inno Setup per-user installer | Framework-dependent (.NET 8 Desktop Runtime) | Registry (`HKCU\Software\TrackDot`) + Run key | Manual |
| **Portable** (`TrackDot-v{ver}-Portable-x64.zip`) | Self-contained ZIP with `portable.dat` marker | Self-contained (bundles .NET 8 runtime) | `settings.json` next to the binary | Manual |

The end-to-end build is `scripts\build-release.ps1 -Version "0.1.0"` (PowerShell 7). It runs the tests, publishes, packages the Portable ZIP, and invokes Inno Setup if `ISCC.exe` is on the standard install path.

For a quick one-off local publish (framework-dependent):

```bash
# Framework-dependent — small artifact, requires .NET 8 Desktop Runtime on the target machine.
dotnet publish TrackDot.csproj -c Release -r win-x64 --self-contained false \
    -o artifacts/win-x64-framework-dependent
```

**Verification (manual, see `docs/SMOKE_TEST.md` for the full matrix):**

1. Copy `artifacts/win-x64-framework-dependent/` to a Windows 10 19041+ or Windows 11 x64 machine with the .NET 8 Desktop Runtime installed.
2. Launch `TrackDot.exe` from File Explorer (NOT `dotnet run`).
3. Confirm the tray icon appears; click it; verify metadata + transport commands for an SMTC source.
4. Open Settings, change Appearance / Opacity / Shortcuts / Startup. Confirm the values are written (`HKCU\Software\TrackDot\*` and `HKCU\...\Run\TrackDot`).
5. Right-click tray → **Exit TrackDot**. Confirm the tray icon disappears, the process exits, and no zombies remain (`Get-Process TrackDot`).

For the **Portable edition** ZIP: drop `portable.dat` into the extracted directory, launch `TrackDot.exe`, change a setting, exit, and confirm `settings.json` was written (and `HKCU\Software\TrackDot` was *not*).

**Do NOT enable trimming.** WPF / WinRT / reflection paths are trim-sensitive and a trim-broken published build is hard to diagnose. Keep `PublishTrimmed=false` (the default).

---

## Manual smoke matrix

The automated xUnit suite (273 / 273) covers contracts, lifecycle, and disposal — but it cannot exercise real SMTC sources, real displays, or real Windows behaviour. For the integration matrix (Windows 10 / 11, Chrome / Edge / Spotify / VLC, multi-monitor, 100/125/150% DPI, 30-minute hidden soak, 15-minute playback soak, taskbar position), see [`docs/SMOKE_TEST.md`](docs/SMOKE_TEST.md).

---

## Repository housekeeping

- `.gitignore` covers `.vs/`, `bin/`, `obj/`, `TestResults/`, `artifacts/`, `release/`, `installer/Output/`, user files, and standard NuGet / VS outputs.
- No tracked `.vs/`, `bin/`, `obj/`, `artifacts/`, or `release/` should ever appear in `git status` after a build — if one does, the `.gitignore` is wrong.
- The published artifact directories (`artifacts/`, `release/`) are git-ignored; the `scripts\build-release.ps1` script (or the `dotnet publish` command above) recreates them on demand.

---

## License

MIT. See the repository header / package metadata.

- Tray icon component: `Hardcodet.NotifyIcon.Wpf` 1.1.0 — MIT, see [Hardcodet/notifyicon-wpf](https://github.com/Hardcodet/notifyicon-wpf).
- Japanese romaji / furigana conversion: [`Kawazu` 1.0.0](https://github.com/herlandroando/Kawazu) — NuGet package used by `LyricsService`; the IPA dictionary ships with the package and is copied next to the binary by the `CopyKawazuIpaDic` build target.
- Lyrics lookup: [lrclib.net](https://lrclib.net) — public community-maintained LRC API.
