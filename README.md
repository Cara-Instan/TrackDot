# TrackDot

A lightweight Windows 10/11 tray application that shows the **currently playing** media session — title, artist, source, artwork, timeline — and exposes Previous / Play-Pause / Stop / Next commands, without a taskbar button.

TrackDot reads the OS-selected session through the Windows **System Media Transport Controls (SMTC)** API and binds to it through WPF. It follows `GetCurrentSession()` (the same session the OS volume / media keys target). Manual source selection is out of MVP scope — see [Known limitations](#known-limitations).

---

## Features

- **Tray-first.** No taskbar button; no window opens until you click the tray icon.
- **One popover, ~360 × 128 logical pixels.** Borderless, anchored above the notification area on the monitor containing the taskbar. Re-anchors on every show so resolution / monitor swaps are picked up automatically.
- **Metadata + artwork.** Title, artist, source-app label, artwork (decoded to WPF `ImageSource` via `Windows.Graphics.Imaging`, frozen for cross-thread binding).
- **Smooth progress.** 250 ms local interpolation only while the popover is visible *and* playback is `Playing`. The interpolation timer stops on hide / pause / no-session.
- **Capability-gated transport.** Previous / Play-Pause / Stop / Next disable individually when the source reports the capability unsupported. Failed `Try*Async` returns trigger a playback refresh so the buttons re-evaluate.
- **Single instance.** A named mutex (`Local\TrackDot.SingleInstance.v1`) prevents duplicate tray icons.
- **Opt-in launch at sign-in.** Stores a quoted executable path under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (no admin prompt).
- **Crash log.** Unhandled exceptions on the WPF dispatcher, the AppDomain, and the `TaskScheduler` are appended to `%LocalAppData%\TrackDot\crash.log` with channel tag + full chain.

---

## Prerequisites

| | |
|---|---|
| OS | Windows 10 build 19041+ or Windows 11 (x64) |
| .NET runtime | `Microsoft.WindowsDesktop.App 8.0.x` (already installed on a current Windows 11 machine; bundled in the framework-dependent artifact) |
| .NET SDK (for development) | `dotnet 8.0.x` |
| Players | Any SMTC-publishing source — Chrome / Edge, Spotify desktop, Windows Media Player, Groove, **VLC with SMTC integration enabled** (see [Known limitations](#known-limitations)) |

The project is pinned to `net8.0-windows10.0.19041.0`, x64 only, with `<UseWPF>true</UseWPF>`. The tray icon is provided by [`Hardcodet.NotifyIcon.Wpf` 1.1.0](https://github.com/Hardcodet/notifyicon-wpf) (WPF-native, no WinForms dependency).

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

**Expected output from step 2:** `Passed: 227, Failed: 0, Skipped: 0`. Both Debug and Release are 227 / 227. The suite covers 14 modules: smoke, snapshot contracts, mapper, decoder, command dispatch, service guards, progress interpolation, view-model, single-instance, tray-icon service, placement, exception logger, startup service, asset-resource.

**Do not run the published executable from a build directory whose path contains a trailing separator.** The launch-at-sign-in detection path (`StartupService.IsEnabled`) compares with `OrdinalIgnoreCase` + trimmed trailing separators, but the Run-key parser on Windows does *not* — paths stored without a trailing separator are the canonical form.

### What "tray-first" means in practice

The application is started by `OnExplicitShutdown` mode. **There is no main window at startup.** The tray icon is the only visible artifact. Left-click toggles the popover; right-click opens the context menu.

| Tray action | Behaviour |
|---|---|
| Left-click on the icon | Toggle the popover (show / hide). Showing re-anchors it to the current work area. |
| Right-click on the icon | Open the context menu — **Settings**, separator, **Exit TrackDot**. |
| Left-click outside the popover while it is open | Hide the popover (`Deactivated`). Tray stays alive. |
| `X` button on the popover | Hide the popover (`Window_Closing` cancels the close). Tray stays alive. |
| `Alt + F4` on the popover | Same as `X` — hide, do not exit. |
| Tray menu → **Exit TrackDot** | Dispose the media service, dispose the view-model, close the popover and settings window, release the named mutex, dispose the tray icon, then `Application.Shutdown()`. The tray icon disappears from the notification area. |
| Launch `TrackDot.exe` while another instance is running | The second process sees the named mutex as not-acquired and exits with code 1 — no second tray icon. |

### Settings window

Tray menu → **Settings** opens a single-instance `SettingsWindow`. It has one checkbox:

- **Launch at sign-in.** Save-immediately: toggling writes (or removes) `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TrackDot` right away. On exception, the checkbox rolls back and a status line appears under the explanatory text.

Closing the Settings window (X / Esc / **Close**) hides it rather than disposing it — your position is preserved across opens. The window is disposed when the application exits.

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

## Project layout

```
TrackDot/
├── App.xaml(.cs)               Composition root, tray resources, exception-logger bootstrap
├── MainWindow.xaml(.cs)        Floating popover window (DataContext set by composition root)
├── SettingsWindow.xaml(.cs)    Settings dialog (single-instance, hidden on close)
├── AssemblyInfo.cs
├── Assets/
│   ├── AppIcon.ico             32×32 PNG-in-ICO used by the tray icon
│   └── PlaceholderArt.png      1×1 transparent PNG (kept for the build-time resource check)
├── Commands/
│   └── AsyncRelayCommand.cs    ICommand wrapper with re-entrancy latch
├── Converters/
│   └── TimeSpanTextConverter.cs  TimeSpan → "m:ss" / "h:mm:ss" (the VM formats in code too)
├── Models/                     Immutable records: snapshot, playback, capabilities
├── Services/                   IMediaControllerService, MediaControllerService, mapper,
│                               ThumbnailDecoder, ProgressInterpolator, SingleInstanceGuard,
│                               tray icon + handle, WindowPlacementService,
│                               UnhandledExceptionLogger, StartupService, registry adapter
├── ViewModels/                 MainViewModel, SettingsViewModel, IUiTicker + DispatcherUiTicker
├── TrackDot.csproj             TFM net8.0-windows10.0.19041.0, x64, UseWPF=true
├── TrackDot.sln
└── tests/TrackDot.Tests/       227 xUnit tests; EnableDefaultCompileItems=false
```

The test project uses `EnableDefaultCompileItems=false` with explicit `<Compile Include="..." />` entries. The application csproj excludes `tests\**\*.cs` from the WPF design-time temp build (see [Build pitfalls](#build-pitfalls)).

---

## Build pitfalls

These are the ones the development history burned into the codebase. If a fresh checkout behaves oddly, check these first:

1. **Do not add `Microsoft.Windows.SDK.Contracts` as a package.** It errors with `NETSDK1130` on .NET 5+ because the TFM already carries WinRT projection through `Microsoft.Windows.SDK.NET.Ref`. WPF + WinRT (`GlobalSystemMediaTransportControlsSessionManager`, `Windows.Graphics.Imaging.BitmapDecoder`) works out of the box on `net8.0-windows10.0.19041.0`.

2. **`dotnet test --no-build` after editing both production and test files can hold a stale binary.** If a test seems to be running against an old version, drop `--no-build` for one cycle so the test runner rebuilds.

3. **AsyncRelayCommand re-entrancy tests.** The latch (`RunningForTest`) is read by polling. The test must drain the latch via a polling loop on `RunningForTest == 0`, **not** by counting `Task.Yield()` pumps — Release JIT timing varies. New re-entrancy tests must wrap `sut.Execute(null)` in `await Task.Run(() => sut.Execute(null))` so the `async void` body escapes xUnit's `SynchronizationContext`. Direct calls are flaky.

4. **`BitmapDecoder` is ambiguous in WPF projects.** The decoder file aliases the WinRT one as `WinRTBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;`. Do not remove the alias.

5. **`Application.MainWindow` (the instance property) collides with the `TrackDot.MainWindow` type name** inside `App.xaml.cs`. Anywhere the composition root touches both, fully qualify: `TrackDot.MainWindow.IsShuttingDown = true`. The type wins over the property when the property is not set, so the type path always needs qualifying from inside `App`.

6. **`TaskbarIcon.IconSource` resolves via `pack://application:,,,/Assets/AppIcon.ico`.** A bare path like `Assets/AppIcon.ico` only works at design time. The csproj embeds the asset as `<Resource Include="Assets\AppIcon.ico" />`; runtime resolution requires the pack URI.

7. **`HKCU\...\Run` paths with spaces MUST be quoted.** Every per-user install path on Windows contains a space. The Run-key parser splits on whitespace inside an unquoted string. The stored value must be `"C:\Path\To\App.exe"` (surrounded by double quotes). `IsEnabled` accepts both quoted and unquoted stored values (third-party tools frequently write the unquoted form).

8. **`Enable` opens the registry key TWICE — once for the `IsEnabled` read, once for the write.** Idempotency means re-reading on every call, so a foreign-write between the user's prior `Disable` and current `Enable` is picked up. Do not "optimize" by caching the read result.

---

## Privacy

**TrackDot does not network, does not phone home, and does not collect telemetry.** Verified by grep: there is no `HttpClient`, no `WebClient`, no `TcpClient`, no `WebRequest`, no analytics SDK, no telemetry key in the project or in any dependency's runtime surface beyond standard WPF + .NET 8.

- All metadata is read from the OS-selected SMTC session and stays in-process.
- The only file written outside the application directory is `%LocalAppData%\TrackDot\crash.log`, used for unhandled-exception capture. You can delete it; the application recreates it on next failure.
- The only registry value written is `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TrackDot`, only when the user opts in via the Settings window.

No data is sent anywhere. The Settings window does not require internet. The popover does not require internet.

---

## Known limitations

- **OS-selected session only.** TrackDot follows `GetCurrentSession()`. If the OS picks the wrong session (rare — usually when two SMTC sources are running and one has stale audio focus), TrackDot shows the OS's choice. Manual source selection is deferred — see plan §7 (HANDOFF.md, Decision point 3).
- **`AllowsTransparency=True` rendering cost.** The popover uses `WindowStyle=None` + `AllowsTransparency=True` + a rounded `Border`. On some GPUs / drivers this routes through WPF software rendering and can raise idle CPU slightly. The conservative mitigation is to swap to `WindowChrome` rounded corners; deferred to a follow-up. See HANDOFF.md Decision point 2.
- **VLC SMTC is version-dependent.** The "Share media with Windows Media Player" / SMTC integration preference must be enabled in VLC's preferences; otherwise VLC will not appear as a source. Some VLC versions accept Previous / Next but ignore Stop.
- **`Assets/PlaceholderArt.png` is a 1×1 transparent PNG.** It is referenced by the build-time resource check (Task 11) but **not bound in XAML** — the popover's `<Image Source="{Binding Artwork}" />` shows nothing when `Artwork` is null, leaving the artwork border background (`#34373D`) visible. Wiring a visible fallback is a bounded, isolated change if reviewers want one.
- **Single source per process.** The composition root wires one `MediaControllerService`. A future source picker is out of MVP scope; the service contract already exposes the source's `SourceAppUserModelId` so a picker could be added without changing the downstream contract.
- **No installer.** The build produces a framework-dependent artifact (see [Publishing](#publishing)). For per-machine installation with proper Add/Remove Programs entry, an MSIX / WiX follow-up is recommended — see plan §7 (HANDOFF.md, Decision point 4).

---

## Publishing

The plan calls for a **framework-dependent x64 artifact** first; self-contained / single-file is a follow-up once the framework-dependent path is verified.

```bash
# Framework-dependent — small artifact, requires .NET 8 Desktop Runtime on the target machine.
dotnet publish TrackDot.csproj -c Release -r win-x64 --self-contained false \
    -o artifacts/win-x64-framework-dependent
```

**Verification (manual, see `docs/SMOKE_TEST.md` for the full matrix):**

1. Copy `artifacts/win-x64-framework-dependent/` to a Windows 10 19041+ or Windows 11 x64 machine with the .NET 8 Desktop Runtime installed.
2. Launch `TrackDot.exe` from File Explorer (NOT `dotnet run`).
3. Confirm the tray icon appears; click it; verify metadata + transport commands for an SMTC source.
4. Open Settings, enable **Launch at sign-in**. Verify `HKCU\...\Run\TrackDot` points at the **published** executable path, not at `dotnet.exe` or a development DLL.
5. Right-click tray → **Exit TrackDot**. Confirm the tray icon disappears, the process exits, and no zombies remain (`Get-Process TrackDot`).

**Do NOT enable trimming.** WPF / WinRT / reflection paths are trim-sensitive and a trim-broken published build is hard to diagnose. Keep `PublishTrimmed=false` (the default).

A self-contained / single-file follow-up is recommended once the framework-dependent artifact has soak-tested cleanly. The plan's `scripts/publish.ps1` is intentionally not included — the `dotnet publish` command above is the entire workflow.

---

## Manual smoke matrix

The automated xUnit suite (227 / 227) covers contracts, lifecycle, and disposal — but it cannot exercise real SMTC sources, real displays, or real Windows behaviour. For the integration matrix (Windows 10 / 11, Chrome / Edge / Spotify / VLC, multi-monitor, 100/125/150% DPI, 30-minute hidden soak, 15-minute playback soak, taskbar position), see [`docs/SMOKE_TEST.md`](docs/SMOKE_TEST.md).

---

## Repository housekeeping

- `.gitignore` covers `.vs/`, `bin/`, `obj/`, `TestResults/`, `artifacts/`, user files, and standard NuGet / VS outputs.
- No tracked `.vs/`, `bin/`, `obj/`, or `artifacts/` should ever appear in `git status` after a build — if one does, the `.gitignore` is wrong.
- The published artifact directory (`artifacts/`) is git-ignored; the framework-dependent `publish` command above recreates it on demand.

---

## License

See the repository header / package metadata. The tray-icon component is `Hardcodet.NotifyIcon.Wpf` 1.1.0 (MIT); see its own repository for license terms.
