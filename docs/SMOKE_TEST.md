# TrackDot — Windows Integration & Idle-Resource Validation

> Manual smoke matrix for TrackDot. The automated xUnit suite (227 / 227 tests, Debug + Release) covers contracts, lifecycle, and disposal; this document covers **integration against real SMTC sources and real Windows behaviour** that cannot be exercised headless.

**Owner of result:** record findings, reproduction steps, logs, and any bounded-scope patches here. Player-specific failures are defects to isolate — **not** grounds to revert earlier reviewed and passing tasks.

---

## 1. Prerequisites

| Item | Value |
|---|---|
| OS | Windows 10 19041+ or Windows 11 (x64) |
| .NET runtime | `Microsoft.WindowsDesktop.App 8.0.x` (matches the published framework-dependent artifact) |
| Source players (mix-and-match) | Chrome, Edge, Spotify desktop, VLC (SMTC enabled), Windows Media Player |
| Visual check | 100% / 125% / 150% DPI, monitor count 1 / 2, taskbar at bottom + (where practical) top / left / right |
| Tooling | `dotnet 8.0.x` SDK, `Get-Process`, Task Manager / Process Explorer, Visual Studio or `dotnet test` |

Run the production binary from `bin/x64/Release/net8.0-windows10.0.19041.0/TrackDot.exe`, **not** `dotnet run`. The published path is the supported launch path for launch-at-sign-in detection (Task 10).

---

## 2. Pre-flight (every scenario)

1. Confirm `TrackDot.exe` is **not** already running (`Get-Process TrackDot -ErrorAction SilentlyContinue`).
2. Launch from File Explorer. **Expect:** no taskbar button appears. A tray icon (notification-area glyph) appears within ~1 s.
3. Right-click the tray icon → confirm **Settings** and **Exit TrackDot** are present.
4. Open `%LocalAppData%\TrackDot\` and confirm `crash.log` either does not exist or is empty.
5. Record baseline `Get-Process TrackDot` — capture `HandleCount`, `WorkingSet64`, `CPU` (cumulative).

---

## 3. Scenario matrix

For each scenario, click the tray icon once to open the popover, then verify the invariant column. Click again to close.

### 3.1 No active media session

| Step | Expected |
|---|---|
| Launch with no media playing | Tray tooltip = `TrackDot`, popover shows `Nothing playing` placeholder title, artist/AUMID empty, progress bar empty, **all four transport buttons disabled** |
| Start playback in any SMTC source | Popover updates within ~1 s; metadata + progress appear; Play/Pause enables |
| Stop playback / close the player | Popover returns to `Nothing playing`; buttons disable again |
| Close player **during** metadata load (race) | No stale metadata from previous track; popover settles to either the new track or `Nothing playing` |

**Invariants:** empty state is bound safely (no NRE); old-session async results cannot overwrite the new session (generation counter, Task 11).

### 3.2 Chrome / Edge (YouTube + similar)

| Step | Expected |
|---|---|
| Start a video | Title / channel / artwork populated within ~1–2 s; progress advances smoothly while playing |
| Seek the player timeline | Popover snaps to the new position and resumes interpolation from the new baseline |
| Switch tabs while paused | Popover remains on the most recent track or updates to the new tab's metadata; no flash of stale data |
| Close the tab mid-load | Popover returns to `Nothing playing`; no exception in `crash.log` |

**Known:** thumbnails from YouTube are sometimes delayed; the popover should not block on the artwork path (decoder returns `null` on failure).

### 3.3 Spotify desktop

| Step | Expected |
|---|---|
| Start playback | All four transport buttons enable (Spotify supports Previous / Play-Pause / Stop / Next) |
| Click Previous / Next | Spotify advances; popover updates title / artist / artwork |
| Click Stop | Playback halts; progress bar freezes; popover remains on the last track with Stop pressed (`MediaPlaybackState.Stopped`) |
| Pause then resume | Play/Pause glyph follows the authoritative playback state (▶ ⇄ ❚❚) |

### 3.4 VLC (SMTC integration enabled)

| Step | Expected |
|---|---|
| **Pre-check:** `Tools → Preferences → Interface → Main interfaces → Qt → Privacy / Network Interaction → "Share media with Windows Media Player"` (label varies by version) | Confirm enabled; otherwise VLC will not appear as an SMTC source |
| Open a media file | Metadata + artwork + timeline appear |
| Click Previous / Next | Behaviour depends on VLC's interpretation of the Run-key commands; record whether VLC responds |
| Pause then resume | Play/Pause glyph flips |

**Known:** VLC SMTC support is version-dependent. If a command button is disabled when the player supports it, capture VLC version + a `crash.log` snippet. Do not roll back the capability gate to "always enabled" — that hides real failures from users.

### 3.5 Unsupported capabilities

| Step | Expected |
|---|---|
| Trigger a source where `CanGoPrevious` / `CanGoNext` / `CanStop` are `false` | The corresponding button is **disabled** (visibly greyed via the transport-button style), click is a no-op |
| Trigger a source where `CanPlay ∥ CanPause` is `false` | Play/Pause is disabled |
| Hover the disabled button | Tooltip still appears (the disabled state does not remove the tooltip target) |

The capability gate is **snapshot-based**: the cached snapshot decides enable / disable. A failed `Try*Async` triggers a playback refresh, so the next authoritative event re-enables / re-disables the button.

### 3.6 Source transition (churn)

| Step | Expected |
|---|---|
| Play Track A in Spotify; while it is mid-load, play Track B in Edge | Popover ends up on whichever track `GetCurrentSession()` returns; no mixing of A's metadata with B's artwork |
| Close the current player entirely | Popover returns to `Nothing playing` |
| Reopen the same player, start playback again | Popover re-populates from the new snapshot |

The generation counter (`MediaControllerService`) is the only thing standing between this scenario and stale-track display.

### 3.7 Window placement and DPI

| Step | Expected |
|---|---|
| Default taskbar-bottom, single monitor, 100% DPI | Popover appears anchored to bottom-right of the work area, 12 px margin, fully on-screen |
| 125% DPI | Same anchor; popover remains on-screen (DPI-converted coords) |
| 150% DPI | Same |
| Two monitors, taskbar at bottom of the **right** monitor | Popover appears over the right monitor's notification area (the one with the taskbar) |
| Move the taskbar to the top of the primary monitor | Popover follows the new work area on next show |
| Drag the popover to another monitor, hide, reopen | Popover re-anchors (each `ShowPopover` recomputes — no cached stale position) |

The placement service (`WindowPlacementService`) reads `SystemParameters.WorkArea` fresh on every show, so resolution / monitor changes do not require a separate invalidation.

### 3.8 Popover lifecycle (tray first)

| Step | Expected |
|---|---|
| Left-click tray icon | Popover shows (or hides if already visible) |
| Left-click outside the popover | Popover hides (`Deactivated`); tray icon stays |
| Click the system X on the popover | Popover hides (`Window_Closing` cancels the close); process stays alive |
| Alt + F4 on the popover | Same as X — popover hides, process stays alive |
| Tray menu → **Exit TrackDot** | Tray icon disappears; process exits; no zombie in `Get-Process` |
| Tray menu → **Settings** | Settings window opens (single instance — second click activates, does not re-create) |
| Launch TrackDot twice | Second instance exits cleanly (`Shutdown(1)`) and writes nothing to `crash.log` |
| While running, kill `explorer.exe` then restart it | Tray icon reappears on next taskbar creation (Hardcodet NotifyIcon.Wpf handles taskbar recreation) — record behaviour |

### 3.9 Idle / hidden soak (30 minutes)

| Step | Expected |
|---|---|
| Hide the popover (click outside) and pause playback | **Near-zero sustained CPU**. The 250 ms interpolation timer stops when `IsVisible && !IsPlaying`. |
| Record `HandleCount` + `WorkingSet64` at start | — |
| Record again at +15 min and +30 min | No monotonic growth beyond ±5% jitter; no new handles |

If the timer fails to stop, the popover's `ShowPopover` / `HidePopover` toggle of `MainViewModel.IsVisible` is the bug — verify `MainViewModel.UpdateTicker` runs on hide (Task 6 gotcha #1).

### 3.10 Playback soak (15 minutes)

| Step | Expected |
|---|---|
| Play a playlist with frequent track changes (~one change / 30 s) | Thumbnails render correctly; **no handle growth** (decoder opens / closes every stream; nothing held open) |
| Record `HandleCount` + `WorkingSet64` at start, +5 min, +15 min | Steady state; no monotonic growth |
| Force a thumbnail decode failure (kill the player mid-load) | Popover stays usable; failure logged in `crash.log` but does not throw a UI dialog |

### 3.11 Launch at sign-in (Task 10)

| Step | Expected |
|---|---|
| Open Settings → check **Launch at sign-in** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TrackDot` = `"<full path to TrackDot.exe>"` (quoted) |
| Restart Windows / sign out + sign in | TrackDot starts minimized to tray; single-instance mutex prevents duplicate |
| Uncheck the toggle | Registry value is removed; next sign-in does not start TrackDot |
| Verify with `regedit` | Value name is exactly `TrackDot`, value data is quoted, no `dotnet.exe` |

### 3.12 Unobserved exception channels (Task 9)

| Step | Expected |
|---|---|
| Cause a recoverable exception (close the active player mid-load) | `%LocalAppData%\TrackDot\crash.log` gains an entry with the channel tag (Dispatcher / AppDomain / TaskScheduler) + full exception + inner chain |
| Cause an unrecoverable / fatal exception (artificial, e.g. corrupt the registry value for LaunchAtSignIn) | Log line appears; `Application.DispatcherUnhandledException` marks `Handled = true` to avoid the WPF crash dialog loop |

---

### 3.13 Multi-Session Picker (Feature 9)

| Step | Expected |
|---|---|
| Play media in Spotify and Chrome simultaneously | Session picker row (`SWITCH SOURCE`) appears in popover showing pill buttons for Spotify and Google Chrome |
| Currently active source pill | Styled with accent background and dark text (`IsCurrent = true`) |
| Click non-active source pill | Popover snaps metadata, artwork, and transport state to selected player |
| Close one of the source apps | Session picker updates; when only 1 source remains, the session picker row collapses automatically |

---

### 3.14 Volume / Mute Controls (Feature 10)

| Step | Expected |
|---|---|
| Active media playing | Volume control row appears below transport controls with speaker icon, volume slider, and percentage text |
| Drag volume slider | Source application's CoreAudio volume level updates in sync; percentage label updates (0%–100%) |
| Click speaker button | CoreAudio session mutes/unmutes; icon switches between speaker and speaker-with-x glyph |
| No active media session | Volume row collapses automatically |


---

## 4. Resource baseline (record during each run)

```
Run #___  Date ________  OS ________  Build (Debug / Release) ________

At launch:
  HandleCount:   ________
  WorkingSet64:  ________ MB
  CPU:           ________ s (cumulative)

After 5 min hidden + paused:
  HandleCount:   ________
  WorkingSet64:  ________ MB
  CPU delta:     ________ s

After 15 min playback soak:
  HandleCount:   ________
  WorkingSet64:  ________ MB
  CPU delta:     ________ s
```

If the handle count or working set grows monotonically by more than 5% over 15 min without track changes, that's a leak — investigate the thumbnail stream path first (`MediaControllerService.OpenThumbnailAsManagedStreamAsync`).

---

## 5. Known limitations (record here, do not "fix" silently)

- **OS-selected session only.** TrackDot follows `GetCurrentSession()`; manual source selection is out of MVP scope (plan §1.5, §7).
- **DWM rounded corners on Win11 22H2+.** On supported hosts the popover (`MainWindow.xaml.cs:49-64`) and the lyrics window (`LyricsWindow.xaml.cs:92-100`) call `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND)` from `OnSourceInitialized` and drop the layered-alpha HWND path (`AllowsTransparency = false`, `Background = PanelBrush`). On older builds the DWM call returns `NotSupportedOnThisOs` and the XAML defaults (`AllowsTransparency="True"` + `Background="Transparent"` + the inner rounded `Border` `CornerRadius="14"`) are preserved unchanged. The version detector (`Services/DwmInterop.cs`, via `RtlGetVersion` from `ntdll.dll`) is host-conditional — `Environment.OSVersion` lies on Win10, so the helper bypasses it. WindowChrome (`MainWindow.xaml:10-16`, `LyricsWindow.xaml:17-23`) is still in use with `CornerRadius="0"` and `GlassFrameThickness="0"`; it cannot produce client-side rounded corners on its own. Design notes in `.hermes/plans/2026-08-13_134600-dwm-corner-preference-migration.md`.
- **VLC SMTC is version-dependent.** Record findings; do not paper over with a "force-enable" path.
- **1×1 placeholder artwork file.** `Assets/PlaceholderArt.png` is a 1×1 transparent PNG. The popover currently does **not** bind a fallback when `Artwork == null` — it just shows the artwork border background (`#34373D`). If reviewers want a visible fallback, swap the `<Image Source="{Binding Artwork}" />` to a style trigger or use the placeholder as the default — this is a bounded, isolated patch.

---

## 6. Exit criteria for Task 12

All scenarios in §3 pass, **or** any failure is documented with: scenario, expected, actual, reproduction steps, `crash.log` snippet, and an isolation note (does the failure indicate a broader bug, or is it bounded to the specific player / driver version?). Scenarios 3.9 and 3.10 hit the resource-baseline targets — if not, the task is **not complete**.

No implementation file may be rolled back to "make a scenario pass." Patches must be the smallest responsible change.
