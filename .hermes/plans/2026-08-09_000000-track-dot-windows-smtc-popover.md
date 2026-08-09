# TrackDot Windows SMTC Popover Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Turn the existing blank .NET 8 WPF project into a lightweight Windows 10/11 tray application that discovers the current system media session through SMTC, displays metadata and timeline state in a floating popover, and sends transport commands.

**Architecture:** Keep native/platform code behind small interfaces. `MediaControllerService` owns `GlobalSystemMediaTransportControlsSessionManager`, session subscriptions, media-property reads, and command dispatch; it emits immutable snapshots rather than exposing WinRT objects. `MainViewModel` owns presentation state and local progress interpolation. `App` is the composition root and lifetime owner, while `TrayIconService` owns the tray menu and popover visibility. This separation makes metadata mapping, source selection, timeline interpolation, and startup behavior unit-testable without an active media player.

**Tech Stack:** C# 12, .NET 8 WPF, TFM `net8.0-windows10.0.19041.0`, Windows SDK contracts/WinRT projection, `Hardcodet.NotifyIcon.Wpf`, xUnit, Microsoft.NET.Test.Sdk.

---

## 1. Scope and decisions

### MVP acceptance criteria

- Starts without showing a taskbar window and remains available from the notification area.
- A left-click on the tray icon toggles one borderless popover positioned above the notification area on the monitor containing the taskbar.
- The popover shows title, artist, source application, album art or packaged fallback art, playback state, and elapsed/total time.
- Previous, play/pause, stop, and next invoke the active SMTC session only when supported.
- Timeline updates smoothly between SMTC timeline events without polling the OS continuously.
- Session and event-handler changes are race-safe, idempotent, and disposed at application shutdown.
- The app remains stable with no session, rapidly changing sessions, missing metadata/art, failed WinRT calls, or unsupported controls.
- Tray context menu exposes **Settings** and **Exit TrackDot**. Settings may initially open a small settings window containing the launch-at-sign-in toggle.
- Launch-at-sign-in is opt-in and implemented with a per-user registry value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

### Deliberate technical choices

1. **Use Hardcodet.NotifyIcon.Wpf.** It avoids a WinForms application dependency and offers WPF-native commands/context menus. Pin the selected package version in the project and record it in `README.md`.
2. **Do not use `AllowsTransparency=True` for the final shell unless testing proves acceptable.** It forces WPF software rendering and conflicts with the idle-CPU objective. Prefer `WindowStyle=None`, transparent background with a rounded root border, and Win32/DWM rounded corners on Windows 11; use a clipped rounded border fallback on Windows 10. If true per-pixel transparency is required, document the rendering tradeoff and benchmark it before adopting it.
3. **Use view-model bindings, not direct code-behind rendering.** Code-behind is reserved for window drag, activation/deactivation, and native positioning hooks.
4. **Use snapshots and monotonic interpolation.** Store the last authoritative timeline position plus a `Stopwatch` timestamp. The UI timer only interpolates while playing and clamps to `[0, EndTime]`.
5. **Do not silently auto-switch to another source after every property event.** Follow `GetCurrentSession()` as the OS-selected session for MVP. Expose source identity so a future source-picker can be added without changing the core contract.
6. **Use a packaged fallback asset generated/added during implementation.** The app must never depend on a developer-machine absolute path.
7. **Settings is in scope only for launch-at-sign-in.** Source pinning, themes, hotkeys, volume, packaging, update checks, and telemetry are out of MVP scope.

## 2. Target structure

The current repository is a non-git, root-level WPF template (`TrackDot.csproj`, `App.xaml`, `MainWindow.xaml`). Preserve that layout instead of moving the project into `src/` midstream.

```text
TrackDot/
├── .gitignore
├── TrackDot.sln
├── TrackDot.csproj
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── SettingsWindow.xaml
├── SettingsWindow.xaml.cs
├── AssemblyInfo.cs
├── Assets/
│   ├── AppIcon.ico
│   └── PlaceholderArt.png
├── Commands/
│   └── AsyncRelayCommand.cs
├── Models/
│   ├── MediaSessionSnapshot.cs
│   ├── PlaybackSnapshot.cs
│   └── TransportCapabilities.cs
├── Services/
│   ├── IMediaControllerService.cs
│   ├── MediaControllerService.cs
│   ├── IStartupService.cs
│   ├── StartupService.cs
│   ├── ITrayIconService.cs
│   ├── TrayIconService.cs
│   └── WindowPlacementService.cs
├── ViewModels/
│   ├── MainViewModel.cs
│   └── SettingsViewModel.cs
├── Converters/
│   └── TimeSpanTextConverter.cs
├── Properties/
│   └── launchSettings.json (only if needed)
├── README.md
└── tests/
    └── TrackDot.Tests/
        ├── TrackDot.Tests.csproj
        ├── MediaSessionSnapshotTests.cs
        ├── MainViewModelTests.cs
        ├── ProgressInterpolationTests.cs
        ├── StartupServiceTests.cs
        └── Fakes/FakeMediaControllerService.cs
```

## 3. State and contracts

Use immutable records so service event handlers can publish a complete, coherent update:

```csharp
public enum MediaPlaybackState { None, Closed, Opened, Changing, Stopped, Playing, Paused }

public sealed record TransportCapabilities(
    bool CanPlay,
    bool CanPause,
    bool CanStop,
    bool CanGoPrevious,
    bool CanGoNext);

public sealed record PlaybackSnapshot(
    MediaPlaybackState State,
    TimeSpan Position,
    TimeSpan StartTime,
    TimeSpan EndTime,
    DateTimeOffset TimelineUpdatedAt,
    TransportCapabilities Capabilities);

public sealed record MediaSessionSnapshot(
    string? SourceAppUserModelId,
    string Title,
    string Artist,
    string AlbumTitle,
    ImageSource? Artwork,
    PlaybackSnapshot Playback)
{
    public static MediaSessionSnapshot Empty { get; } = /* neutral values */;
}
```

The service contract should be narrow and UI-agnostic except for the already-decoded WPF image:

```csharp
public interface IMediaControllerService : IAsyncDisposable
{
    event EventHandler<MediaSessionSnapshot>? SnapshotChanged;
    MediaSessionSnapshot Current { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task TogglePlayPauseAsync();
    Task PreviousAsync();
    Task StopAsync();
    Task NextAsync();
}
```

Implementation rules:

- Marshal `SnapshotChanged` onto WPF's dispatcher, or document that the composition layer always does so. Do not update bound properties from arbitrary WinRT callback threads.
- Protect refreshes with a monotonically increasing generation/session token. Ignore stale media-property tasks after a session switch.
- Unsubscribe from the old session before subscribing to the new one.
- Subscribe to `CurrentSessionChanged`, `MediaPropertiesChanged`, `PlaybackInfoChanged`, and `TimelinePropertiesChanged`.
- Decode thumbnails with `BitmapCacheOption.OnLoad`; copy the random-access stream into managed memory, finish decoding, call `Freeze()`, then dispose all streams.
- Map empty title/artist values to user-facing defaults in the view model (`Nothing playing`, source label), not in the platform layer.
- Treat SMTC failures as recoverable: retain the last coherent snapshot when appropriate, log in debug builds, and publish empty state when the session disappears.

## 4. Task-by-task implementation

### Task 1: Establish a clean, reproducible solution baseline

**Objective:** Make the existing template build reproducibly, ignore generated files, and add a test project before feature work.

**Files:**
- Create: `.gitignore`
- Modify: `TrackDot.csproj`
- Modify: `TrackDot.sln`
- Create: `tests/TrackDot.Tests/TrackDot.Tests.csproj`
- Create: `tests/TrackDot.Tests/SmokeTests.cs`

**Steps:**

1. If version control is desired, initialize Git only after confirming with the user; the current workspace is not a Git repository. Regardless, create a Visual Studio/.NET `.gitignore` covering `.vs/`, `bin/`, `obj/`, `TestResults/`, and user files.
2. Change the application TFM to `net8.0-windows10.0.19041.0`, add `<SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>`, `<Platforms>x64</Platforms>`, and `<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>` after the asset exists. Do not set `RuntimeIdentifier` yet; framework-dependent local builds should stay simple.
3. Add explicit package references for the selected Windows SDK contracts projection and `Hardcodet.NotifyIcon.Wpf`. Verify compatibility against the pinned SDK/package versions rather than guessing namespace availability.
4. Create an xUnit test project targeting the same Windows TFM and reference the app project.
5. Add a smoke test that loads the app assembly.
6. Run:

```bash
dotnet restore TrackDot.sln
dotnet build TrackDot.sln -c Debug --no-restore
dotnet test TrackDot.sln -c Debug --no-build
```

**Expected:** Restore succeeds, build has zero errors, test run reports one passing test.

**Commit:** `chore: establish TrackDot solution baseline`

---

### Task 2: Define media state and transport contracts

**Objective:** Create immutable state records and a narrow media-control interface independent of WinRT session objects.

**Files:**
- Create: `Models/TransportCapabilities.cs`
- Create: `Models/PlaybackSnapshot.cs`
- Create: `Models/MediaSessionSnapshot.cs`
- Create: `Services/IMediaControllerService.cs`
- Create: `tests/TrackDot.Tests/MediaSessionSnapshotTests.cs`

**Steps:**

1. Write tests for `MediaSessionSnapshot.Empty`: neutral strings, no artwork, zero timeline, `None` state, and all controls disabled.
2. Run the targeted test and verify compilation/test failure because the models do not exist.
3. Add the records/enums and service interface shown in Section 3.
4. Run `dotnet test tests/TrackDot.Tests/TrackDot.Tests.csproj --filter MediaSessionSnapshotTests`.
5. Verify all model tests pass and records cannot be mutated after construction.

**Expected:** Empty state is coherent and safe to bind without null-reference checks.

**Commit:** `feat: define media session state contracts`

---

### Task 3: Implement SMTC session discovery and event lifecycle

**Objective:** Initialize the global session manager, track the OS current session, and publish fresh snapshots without event leaks or stale async results.

**Files:**
- Create: `Services/MediaControllerService.cs`
- Create: `Services/MediaPropertyMapper.cs` if mapping becomes large
- Create: `tests/TrackDot.Tests/MediaPropertyMapperTests.cs`

**Steps:**

1. Write pure mapper tests for playback status and `GlobalSystemMediaTransportControlsSessionPlaybackControls` to `MediaPlaybackState` and `TransportCapabilities`.
2. Implement `InitializeAsync()` using `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`.
3. Subscribe once to `CurrentSessionChanged`; centralize session replacement in `SetCurrentSessionAsync(...)`.
4. On replacement, unsubscribe all old session handlers, increment the generation counter, subscribe new handlers, then refresh media, playback, and timeline properties.
5. In callbacks, perform the minimum work and call one guarded refresh method. Ignore completion if its generation/session no longer matches.
6. Implement `DisposeAsync()` to unsubscribe manager and session handlers and prevent further publishes.
7. Add debug logging for recoverable exceptions without displaying modal dialogs.
8. Build and run mapper tests.

**Expected:** Service initialization with no current session publishes `Empty`; session churn cannot let old metadata overwrite the new session.

**Commit:** `feat: track active Windows media session`

> **Manual checkpoint:** Run the app against Chrome/Spotify/VLC and verify Debug output receives current-session, property, playback, and timeline events. This checkpoint is observational; do not build UI yet.

---

### Task 4: Decode album artwork safely

**Objective:** Convert WinRT thumbnails into frozen WPF images while closing every stream promptly.

**Files:**
- Create: `Services/ThumbnailDecoder.cs`
- Modify: `Services/MediaControllerService.cs`
- Create: `tests/TrackDot.Tests/ThumbnailDecoderTests.cs`
- Create: `Assets/PlaceholderArt.png`

**Steps:**

1. Add a small valid PNG fixture and tests asserting decoded dimensions, `BitmapImage.IsFrozen`, and repeat decoding without file locks.
2. Implement decoding with `OpenReadAsync()`, a managed copy, `BitmapCacheOption.OnLoad`, `BeginInit/EndInit`, and `Freeze()`.
3. Dispose WinRT and managed streams using `using`/`await using` as supported by the concrete projection.
4. Return `null` on missing thumbnails; let the XAML/view model select `PlaceholderArt.png`.
5. Wire decoder into media-property refresh, retaining generation checks before publishing.
6. Run targeted tests and a looped memory smoke test (for example 1,000 fixture decodes) to catch stream/file-handle leakage.

**Expected:** Artwork survives stream disposal and can be read on the UI thread.

**Commit:** `feat: decode and cache media artwork safely`

---

### Task 5: Implement command dispatch and capability gating

**Objective:** Send transport commands only to the current session and expose disabled controls when unsupported.

**Files:**
- Modify: `Services/MediaControllerService.cs`
- Create: `Commands/AsyncRelayCommand.cs`
- Create: `tests/TrackDot.Tests/AsyncRelayCommandTests.cs`

**Steps:**

1. Test `AsyncRelayCommand` for `CanExecute`, in-flight reentrancy prevention, property notification, and surfaced/logged exceptions.
2. Implement `PreviousAsync`, `TogglePlayPauseAsync`, `StopAsync`, and `NextAsync` using the current session's `Try*Async` methods.
3. Re-read current playback info before deciding whether play/pause maps to pause or play; never trust a stale button glyph alone.
4. Return normally when no session exists or a capability is false. A failed `Try*Async` result is recoverable and should trigger a playback refresh.
5. Run command tests and build.

**Expected:** Double-clicks cannot create uncontrolled overlapping calls; unsupported buttons are disabled.

**Commit:** `feat: add guarded media transport commands`

---

### Task 6: Build the view model and smooth progress interpolation

**Objective:** Bind coherent presentation state and update progress locally at low CPU cost.

**Files:**
- Create: `ViewModels/MainViewModel.cs`
- Create: `Services/ProgressInterpolator.cs`
- Create: `Converters/TimeSpanTextConverter.cs`
- Create: `tests/TrackDot.Tests/Fakes/FakeMediaControllerService.cs`
- Create: `tests/TrackDot.Tests/MainViewModelTests.cs`
- Create: `tests/TrackDot.Tests/ProgressInterpolationTests.cs`

**Steps:**

1. Write tests for snapshot-to-view-state mapping: no session, playing, paused, unsupported controls, missing title/artist, zero/unknown duration.
2. Write table-driven interpolation tests: playing advances, paused/stopped does not, result clamps to start/end, backward seeks reset the baseline, and long delays do not exceed duration.
3. Inject a time provider/monotonic-clock abstraction into `ProgressInterpolator`; do not use wall-clock time for elapsed interpolation.
4. Implement `MainViewModel` with `INotifyPropertyChanged`, service subscription, and four `AsyncRelayCommand` instances.
5. Use a 250 ms `DispatcherTimer` only while the popover is visible and playback is `Playing`; stop it when hidden/paused/no-session. Restart from each authoritative timeline snapshot.
6. Expose `PositionSeconds`, `DurationSeconds`, elapsed text, duration text, glyph/accessibility label, and control-enabled properties.
7. Dispose/unsubscribe the view model during app shutdown.
8. Run all view-model/interpolation tests.

**Expected:** Deterministic tests prove interpolation without sleeping; hidden idle state creates no recurring UI work.

**Commit:** `feat: add media presentation and timeline interpolation`

---

### Task 7: Construct the floating popover UI

**Objective:** Replace the template window with the dark, compact, accessible controller.

**Files:**
- Modify: `MainWindow.xaml`
- Modify: `MainWindow.xaml.cs`
- Modify: `App.xaml`
- Create: `Services/WindowPlacementService.cs`
- Create: `Assets/AppIcon.ico`

**Steps:**

1. Remove `StartupUri` from `App.xaml`; application lifetime will be explicit in Task 9.
2. Define reusable colors, typography, button styles, focus visuals, and fallback image resource in `App.xaml`.
3. Design a fixed/size-to-content popover approximately 360×128 logical pixels with rounded dark panel, 88×88 artwork, two truncated text rows, source label, transport buttons, and progress/time row.
4. Set `WindowStyle=None`, `ResizeMode=NoResize`, `ShowInTaskbar=False`, `Topmost=True`, and `SizeToContent=WidthAndHeight`. Do not make the whole window draggable over buttons; call `DragMove()` only from a dedicated/header background region after checking the left mouse button.
5. Add named bindings and automation names/tooltips for Previous, Play/Pause, Stop, and Next. Use vector `Path` icons rather than emoji to avoid font-dependent rendering.
6. On `SourceInitialized`, apply the supported DWM corner preference on Windows 11. Gracefully ignore unsupported attributes on Windows 10.
7. Implement `WindowPlacementService` using the nearest monitor's work area and DPI transform, anchoring the popover with an 8–12 px logical margin above/right of the work area. Recalculate on every show and `SystemEvents.DisplaySettingsChanged`.
8. Handle `Deactivated` by hiding the popover unless a context menu/dialog is active. Handle Escape similarly.
9. Build, then run a visual smoke test at 100%, 125%, and 150% scaling.

**Expected:** No taskbar button, no clipping, controls remain keyboard accessible, and the popover opens fully within the active work area.

**Commit:** `feat: build floating media popover`

---

### Task 8: Add tray icon lifecycle and toggle behavior

**Objective:** Make the application tray-first and guarantee that Exit is the only intentional shutdown path.

**Files:**
- Create: `Services/ITrayIconService.cs`
- Create: `Services/TrayIconService.cs`
- Modify: `App.xaml`
- Modify: `App.xaml.cs`
- Modify: `MainWindow.xaml.cs`

**Steps:**

1. Configure `ShutdownMode=OnExplicitShutdown` in application startup code.
2. Create one `TaskbarIcon` with the packaged icon, tooltip, left-click toggle command, and WPF context menu.
3. Implement idempotent `ShowPopover`, `HidePopover`, and `TogglePopover`. Showing activates and repositions the existing window; hiding must not close/dispose it.
4. Make window close requests hide instead, except while application shutdown is in progress.
5. Add **Settings** and **Exit TrackDot** menu commands. Exit sets the shutdown flag, hides/disposes tray icon, disposes view model/media service, closes windows, then calls `Application.Shutdown()`.
6. Verify launching twice is addressed: for MVP, add a named mutex. If another instance exists, exit cleanly; optionally add activation IPC later rather than shipping two tray icons.
7. Manually exercise 50 show/hide cycles and verify exactly one tray icon and one window instance remain.

**Expected:** Closing the popover never exits the process; Exit leaves no stale tray icon.

**Commit:** `feat: add tray-first application lifecycle`

---

### Task 9: Compose startup, initialization, and error handling

**Objective:** Build a predictable application composition root that survives unavailable SMTC and cleans up in reverse order.

**Files:**
- Modify: `App.xaml.cs`
- Modify: `App.xaml`
- Modify: `Services/TrayIconService.cs`

**Steps:**

1. In `OnStartup`, acquire the single-instance mutex, create dispatcher-aware services, view model, hidden `MainWindow`, and tray service.
2. Initialize `MediaControllerService` asynchronously after the tray icon exists so startup failure still leaves an Exit path.
3. Keep the popover hidden at startup; update tray tooltip/status if SMTC initialization fails.
4. Register `DispatcherUnhandledException`, `AppDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException` for debug/file logging as appropriate. Do not swallow fatal state corruption; do avoid modal crash loops from recoverable media calls.
5. In `OnExit`, dispose services idempotently and release mutex.
6. Run with no media apps, then with active media, then close/reopen sources repeatedly.

**Expected:** App starts and exits cleanly regardless of active sessions; no unobserved exceptions appear during churn.

**Commit:** `refactor: centralize application startup and cleanup`

---

### Task 10: Implement launch-at-sign-in settings

**Objective:** Provide an opt-in per-user startup toggle and functional Settings menu target.

**Files:**
- Create: `Services/IStartupService.cs`
- Create: `Services/StartupService.cs`
- Create: `ViewModels/SettingsViewModel.cs`
- Create: `SettingsWindow.xaml`
- Create: `SettingsWindow.xaml.cs`
- Create: `tests/TrackDot.Tests/StartupServiceTests.cs`
- Modify: `Services/TrayIconService.cs`

**Steps:**

1. Abstract registry access behind a tiny key/value adapter so tests never mutate the real registry.
2. Test disabled/enabled detection, quoted executable paths containing spaces, removal, and idempotent enable/disable.
3. Store a quoted executable path under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with value name `TrackDot`. Do not require administrator rights.
4. Add a compact Settings window with one checkbox, explanatory text, and Close button. Save immediately on toggle or provide explicit Apply; choose one and test it.
5. Ensure startup arguments/path detection works for framework-dependent development runs and published `.exe`; disable with an explanation if a stable executable path cannot be determined.
6. Manually verify the exact registry value, restart the app, disable it, and confirm the value is removed.

**Expected:** No HKLM writes or elevation prompt; toggling is reversible and idempotent.

**Commit:** `feat: add launch at sign-in setting`

---

### Task 11: Add automated lifecycle tests and resource checks

**Objective:** Prove the high-risk state transitions and disposal paths independently of real SMTC.

**Files:**
- Modify: `tests/TrackDot.Tests/Fakes/FakeMediaControllerService.cs`
- Create: `tests/TrackDot.Tests/ServiceGenerationTests.cs` (extract coordinator if necessary)
- Create: `tests/TrackDot.Tests/ViewModelLifecycleTests.cs`
- Modify: `TrackDot.csproj`

**Steps:**

1. If generation/session handling is currently trapped in WinRT code, extract a small internal coordinator and expose internals to the test assembly with `InternalsVisibleTo`.
2. Test stale metadata completion after a session switch, repeated initialize/dispose, event unsubscription, and no updates after disposal.
3. Test that hiding pauses UI interpolation and showing resumes from the latest authoritative baseline.
4. Add a build-time check that `Assets/AppIcon.ico` and `Assets/PlaceholderArt.png` exist with correct WPF resource actions.
5. Run the full suite with hang/crash dump diagnostics enabled if supported.

**Expected:** Tests fail if an old session can overwrite a new one or if disposed subscribers still receive events.

**Commit:** `test: cover session churn and application lifecycle`

---

### Task 12: Perform Windows integration and idle-resource validation

**Objective:** Validate real SMTC behavior, UI positioning, and idle CPU/memory without rolling back completed implementation if a particular player is noncompliant.

**Files:**
- Create: `docs/SMOKE_TEST.md`
- Modify: implementation files only for reproduced defects

**Steps:**

1. Build Release: `dotnet build TrackDot.sln -c Release`.
2. Execute a matrix on Windows 10 and Windows 11 where available:
   - no active media session;
   - Chrome/Edge YouTube;
   - Spotify desktop;
   - VLC with SMTC integration enabled;
   - switch between two active sources;
   - close the current player during metadata/art loading;
   - tracks with and without thumbnails;
   - unsupported previous/next/stop capabilities;
   - multi-monitor and 100/125/150% DPI;
   - taskbar on bottom, left/right/top if practical;
   - Explorer restart while app is running.
3. For each source, verify metadata, artwork fallback, play/pause, previous, stop, next, timeline interpolation, seeking initiated in the player, and source transition.
4. Run a 30-minute hidden/paused soak and record CPU working set/handle count at start and end. Target near-zero sustained CPU while hidden or paused; investigate monotonic handle or memory growth.
5. Run a 15-minute playback soak with frequent track changes and verify thumbnail streams do not cause handle growth.
6. Document source-specific limitations as integration findings. A player integration failure is a defect to isolate, not grounds to revert earlier proven work.

**Expected:** All invariant checks pass; any player-specific failure has reproduction steps, logs, and bounded scope.

**Commit:** `test: document Windows media integration matrix`

---

### Task 13: Document build, usage, limitations, and privacy

**Objective:** Make the project usable by a new developer and transparent to end users.

**Files:**
- Modify: `README.md`
- Modify: `docs/SMOKE_TEST.md`

**Steps:**

1. Document prerequisites: Windows 10 19041+, x64, .NET 8 SDK for development, and players exposing SMTC.
2. Add exact restore/build/test/run commands and note that this is a tray-first app.
3. Explain tray controls, Settings, launch-at-sign-in registry location, and Exit behavior.
4. State privacy behavior: metadata remains local; no telemetry/networking is introduced.
5. Document known SMTC limitations (OS-selected current session, source-specific command support, VLC configuration variability).
6. Add screenshots only after final UI validation; do not commit placeholder screenshots.
7. Run every README command from a clean checkout/worktree.

**Expected:** A fresh developer can build, test, and run without undocumented steps.

**Commit:** `docs: add TrackDot setup and usage guide`

---

### Task 14: Produce and verify an x64 distributable

**Objective:** Generate a release artifact suitable for Windows 10/11 x64 and verify it outside the development launch path.

**Files:**
- Modify: `TrackDot.csproj`
- Create: `scripts/publish.ps1` only if a script adds value
- Modify: `README.md`

**Steps:**

1. Add explicit publish properties via command line or a dedicated publish profile; keep development build settings unaffected.
2. First produce a framework-dependent x64 artifact:

```bash
dotnet publish TrackDot.csproj -c Release -r win-x64 --self-contained false -o artifacts/win-x64-framework-dependent
```

3. Optionally produce a self-contained artifact after measuring size/startup:

```bash
dotnet publish TrackDot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/win-x64-self-contained
```

4. Do not enable trimming unless a real published build is smoke-tested; WPF/WinRT/reflection paths can be trim-sensitive.
5. Launch the published executable, verify tray icon/assets/SMTC commands/settings, then exit and confirm no process remains.
6. Verify launch-at-sign-in writes the published executable path, not `dotnet.exe` or a development DLL.

**Expected:** Published artifact starts on a clean supported Windows environment with the declared runtime prerequisites.

**Commit:** `build: add verified Windows x64 publish workflow`

## 5. Required verification gates

Run these after each relevant task and before declaring the project complete:

```bash
dotnet restore TrackDot.sln
dotnet build TrackDot.sln -c Debug --no-restore
dotnet test TrackDot.sln -c Debug --no-build
dotnet build TrackDot.sln -c Release
dotnet publish TrackDot.csproj -c Release -r win-x64 --self-contained false -o artifacts/win-x64-framework-dependent
```

Final invariant checklist:

- [ ] Repository contains no tracked `.vs/`, `bin/`, `obj/`, `TestResults/`, or `artifacts/` output.
- [ ] App TFM is exactly `net8.0-windows10.0.19041.0` and build target is x64.
- [ ] Starting with no session does not crash or show stale metadata.
- [ ] Exactly one tray icon and one process instance exist.
- [ ] Popover never creates a taskbar button.
- [ ] Old-session callbacks cannot overwrite the selected session.
- [ ] Every service/view-model/tray subscription is removed on disposal.
- [ ] Artwork remains valid after all input streams are disposed.
- [ ] Play/pause glyph and command follow authoritative playback state.
- [ ] Progress is clamped, pauses correctly, and stops ticking while hidden/idle.
- [ ] Buttons are disabled when SMTC reports the capability unsupported.
- [ ] Popover remains within the nearest monitor work area at tested DPIs.
- [ ] Launch-at-sign-in writes only HKCU and uses a quoted stable executable path.
- [ ] Exit removes the tray icon and terminates the process cleanly.
- [ ] Published x64 executable loads packaged icon and fallback art.
- [ ] README commands have been executed successfully as written.

## 6. Risks and mitigation

| Risk | Impact | Mitigation |
|---|---|---|
| WinRT callbacks arrive on arbitrary threads | Binding exceptions/races | Publish immutable snapshots through the WPF dispatcher. |
| Async media-property read completes after session switch | Wrong track displayed | Generation/session identity check before committing results. |
| `AllowsTransparency=True` forces software rendering | Idle CPU/GPU overhead | Prefer native borderless window + DWM corners; benchmark before opting in. |
| Thumbnail streams/file handles leak | Memory/handle growth | Decode `OnLoad`, freeze, dispose, and soak-test repeated track changes. |
| Different players expose inconsistent capabilities | Controls appear broken | Capability-gate buttons and record source-specific smoke results. |
| WPF coordinates differ from Win32 pixels under DPI | Off-screen placement | Convert monitor work-area pixels through the window DPI transform on every show. |
| Taskbar/Explorer restarts | Missing tray icon | Verify Hardcodet taskbar recreation behavior and handle taskbar-created message if required. |
| Registry path points at development host | Startup fails | Enable only for a stable executable and validate published-path behavior. |
| Single-file/trimming breaks WPF or WinRT assets | Published app fails | Keep trimming off; test published output, not only `dotnet run`. |
| No existing Git repository | Commit steps unavailable | Initialize Git only with user approval or treat commit lines as future execution checkpoints. |

## 7. Open questions that do not block the MVP plan

- Whether the first public distribution should be framework-dependent ZIP, self-contained ZIP, or MSIX. Recommendation: validate a framework-dependent ZIP first, then add MSIX only when installation/uninstallation and startup registration need product-grade handling.
- Whether a click outside should always hide the popover while Settings is open. Recommendation: Settings is an independent normal window; only popover deactivation hides the popover.
- Whether users need manual source selection. Recommendation: defer until real testing shows OS current-session selection is inadequate.

## 8. Execution protocol

Execute with a fresh subagent per task. After each task:

1. Re-run that task's tests/verification.
2. Dispatch a spec-compliance reviewer; fix every finding.
3. Dispatch a code-quality reviewer only after spec compliance passes; fix every finding.
4. Re-run the targeted tests plus the solution build.
5. Commit only if the workspace has been initialized as a Git repository.
6. Proceed only when both reviews approve.

Task 12 is an integration rollback boundary: player-specific smoke-test failures must not cause rollback of previously reviewed and passing model/service/UI tasks. Isolate, reproduce, and patch the smallest responsible component.
