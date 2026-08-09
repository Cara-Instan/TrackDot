# TrackDot Implementation — Session Handoff

**Date:** 2026-08-09
**Session:** Resumed from plan `.hermes/plans/2026-08-09_000000-track-dot-windows-smtc-popover.md`
**Goal:** Implement the 14-task plan to turn the empty WPF template into a Windows SMTC tray popover.

Commit author: `Herlandro Tribiakto <herlandrotri@gmail.com>` (already configured in this repo).

---

## Status: Tasks 1 & 2 complete, Tasks 3-14 pending

| # | Task | Status | Commit |
|---|------|--------|--------|
| 1 | Establish clean solution baseline (TFM, .gitignore, test project) | ✅ done | `13f85c1` |
| 2 | Define media state and transport contracts (Models, IMediaControllerService) | ✅ done | `f3f96aa` |
| 3 | Implement SMTC session discovery and event lifecycle | 🔴 not started | — |
| 4 | Decode album artwork safely | 🔴 not started | — |
| 5 | Implement command dispatch and capability gating | 🔴 not started | — |
| 6 | Build view model and progress interpolation | 🔴 not started | — |
| 7 | Construct the floating popover UI | 🔴 not started | — |
| 8 | Add tray icon lifecycle and toggle behavior | 🔴 not started | — |
| 9 | Compose startup, initialization, and error handling | 🔴 not started | — |
| 10 | Implement launch-at-sign-in settings | 🔴 not started | — |
| 11 | Add automated lifecycle tests and resource checks | 🔴 not started | — |
| 12 | Windows integration validation (docs only - manual) | 🔴 not started | — |
| 13 | Document build, usage, limitations, and privacy | 🔴 not started | — |
| 14 | Produce and verify x64 distributable | 🔴 not started | — |

---

## Environment

- **Workspace:** `C:\Users\Herlandro Ando\Documents\Ando\sites_win\TrackDot`
- **Shell:** git-bash / MSYS. Use POSIX syntax in `terminal` calls.
- **Toolchain:** `dotnet 8.0.205` SDK at `C:\Program Files\dotnet\sdk`. `Microsoft.WindowsDesktop.App 8.0.x` runtime present.
- **Branch:** `master` (clean worktree, no uncommitted changes).
- **Git remote:** `https://github.com/herlandroando/TrackDot.git`. No remote pushes made — only local commits.

---

## What was done in detail

### Task 1 — Solution baseline (commit `13f85c1`)

**Files changed:**
- `TrackDot.csproj` — TFM pinned to `net8.0-windows10.0.19041.0`, x64 only, `Hardcodet.NotifyIcon.Wpf 1.1.0`, `InternalsVisibleTo TrackDot.Tests`.
- `TrackDot.sln` — Added `tests/TrackDot.Tests/TrackDot.Tests.csproj` (GUID `{B2DCD288-4A5B-4B9C-9D5B-9F8A7C6D5E4F}`), x64-only platforms.
- `tests/TrackDot.Tests/TrackDot.Tests.csproj` — xUnit 2.9.2, `Microsoft.NET.Test.Sdk 17.11.1`, `xunit.runner.visualstudio 2.8.2`, `coverlet.collector 6.0.2`. TFM same as app. `EnableDefaultCompileItems=false` with explicit `<Compile Include="..." />` entries.
- `tests/TrackDot.Tests/SmokeTests.cs` — 3 smoke tests asserting the test assembly loads and the companion `TrackDot.dll` is in the output directory.

**Gotchas the next session needs to know:**

1. **WPF design-time temp-build scans all .cs files in the project tree.** When the test project sits under `tests/`, the WPF temp build (the `TrackDot_xxx_wpftmp.csproj` IntelliSense target) walks `tests\**\*.cs` and tries to compile them without xunit packages → `CS0246`. The fix is in `TrackDot.csproj`:
   ```xml
   <ItemGroup>
     <Compile Remove="tests\**\*.cs" />
     <EmbeddedResource Remove="tests\**\*" />
     <None Remove="tests\**\*" />
     <Page Remove="tests\**\*.xaml" />
     <ApplicationDefinition Remove="tests\**\*.xaml" />
   </ItemGroup>
   ```
   **If you add a new test file under `tests/`, you do NOT need to touch the main csproj** — those `Remove` globs cover it. But the test csproj still has explicit `<Compile Include="..." />` entries (because `EnableDefaultCompileItems=false`); add new test files there.

2. **`Microsoft.Windows.SDK.Contracts` package is forbidden on .NET 5+.** It errors with `NETSDK1130` (cannot reference `Windows.Foundation.UniversalApiContract.winmd` directly). .NET 8 has WinRT projection built into the `net8.0-windows*` TFM. **Do NOT add this package back.** The WPF/WinRT APIs (`GlobalSystemMediaTransportControlsSessionManager`, etc.) come from the `Microsoft.Windows.SDK.NET.Ref` framework reference that the TFM automatically pulls in.

3. **Build with:** `dotnet build TrackDot.sln -c Debug --no-restore`. **Test with:** `dotnet test TrackDot.sln -c Debug --no-build`. Both work on this machine.

### Task 2 — Media contracts (commit `f3f96aa`)

**Files created:**
- `Models/MediaPlaybackState.cs` — enum {None, Closed, Opened, Changing, Stopped, Playing, Paused}.
- `Models/TransportCapabilities.cs` — record with `CanPlay/CanPause/CanStop/CanGoPrevious/CanGoNext` plus `static TransportCapabilities None`.
- `Models/PlaybackSnapshot.cs` — record with `State/Position/StartTime/EndTime/TimelineUpdatedAt/Capabilities` plus `static PlaybackSnapshot Empty`.
- `Models/MediaSessionSnapshot.cs` — record with `SourceAppUserModelId/Title/Artist/AlbumTitle/Artwork/Playback` plus `static MediaSessionSnapshot Empty`. `Artwork` is `ImageSource?` already frozen for thread safety.
- `Services/IMediaControllerService.cs` — `IAsyncDisposable` interface with `Current`, `SnapshotChanged` event, `InitializeAsync`, `TogglePlayPauseAsync`, `PreviousAsync`, `StopAsync`, `NextAsync`.
- `tests/TrackDot.Tests/MediaSessionSnapshotTests.cs` — 13 tests covering Empty defaults, capability flags, theory tests for all capability states, immutability, and safe-to-bind semantics.

**Total tests:** 16 (3 smoke + 13 snapshot). All pass.

---

## Next: Task 3 — SMTC session discovery and event lifecycle

**Plan said (verbatim):**
> Implement `MediaControllerService` using `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`, subscribe to `CurrentSessionChanged`, `MediaPropertiesChanged`, `PlaybackInfoChanged`, `TimelinePropertiesChanged`, centralize session replacement in `SetCurrentSessionAsync(...)`, unsubscribe all old session handlers before subscribing to new ones, increment a generation counter, ignore stale async results.

**Files to create:**
- `Services/MediaControllerService.cs` — the platform-facing implementation.
- `Services/MediaPropertyMapper.cs` — pure functions mapping SMTC enums (and `GlobalSystemMediaTransportControlsSessionPlaybackControls`) to `MediaPlaybackState` and `TransportCapabilities`. Keeps the platform code thin and the mapping unit-testable.
- `tests/TrackDot.Tests/MediaPropertyMapperTests.cs` — tests for the mapper.

**Concrete steps the next session should follow:**

1. **Verify the SMTC API surface against the installed SDK ref.** The package is at `C:/Users/Herlandro Ando/.nuget/packages/microsoft.windows.sdk.net.ref/10.0.19041.31/lib/net6.0/Microsoft.Windows.SDK.NET.dll`. Use `dotnet ildasm` or grep the XML doc at `Microsoft.Windows.SDK.NET.xml` for `GlobalSystemMediaTransportControlsSessionManager` to confirm exact method names. The .NET projection may use `Task<bool>` instead of `IAsyncOperation<bool>` directly.

2. **Draft tests first (RED).** The plan called for "Write pure mapper tests for playback status and `GlobalSystemMediaTransportControlsSessionPlaybackControls` to `MediaPlaybackState` and `TransportCapabilities`." The mapper should be a pure static class so all tests run without WPF dispatcher.

3. **Implement the mapper.** Three static functions needed:
   - `MediaPlaybackState MapPlaybackStatus(GlobalSystemMediaTransportControlsSessionPlaybackStatus)` — maps SMTC's enum to ours.
   - `TransportCapabilities MapPlaybackControls(GlobalSystemMediaTransportControlsSessionPlaybackControls)` — copies each CanXxx flag.
   - `MediaSessionSnapshot BuildSnapshot(...)` — assemble from session properties, playback info, timeline properties, and decoded artwork (Task 4 will plug in the decoder).

4. **Implement `MediaControllerService`.**
   - Constructor accepts a `SynchronizationContext` (or `Dispatcher`) for marshalling WinRT callbacks to the UI thread. Default to `SynchronizationContext.Current` — captured in `App.OnStartup` on the WPF dispatcher.
   - `InitializeAsync`: calls `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`, subscribes to `CurrentSessionChanged`, then calls `SetCurrentSessionAsync(manager.GetCurrentSession())`.
   - `SetCurrentSessionAsync(...)`: nulls old handlers, increments `_generation`, subscribes to new session's `MediaPropertiesChanged` / `PlaybackInfoChanged` / `TimelinePropertiesChanged`, kicks off initial `TryGetMediaPropertiesAsync` / `GetPlaybackInfo` / `GetTimelineProperties` reads.
   - Each async read carries the generation in its closure; if `_generation` advanced before completion, drop the result silently.
   - All completions marshal to the dispatcher thread before publishing `SnapshotChanged`.
   - `DisposeAsync` unsubscribes everywhere and stops publishing.

5. **Tests:**
   - `MediaPropertyMapperTests` — pure unit tests, no WPF.
   - For the service itself, plan says Task 11 will add lifecycle tests via `InternalsVisibleTo` (already wired in `TrackDot.csproj`). For Task 3, defer the service lifecycle tests to Task 11. **However**, the plan mentions extracting a "small internal coordinator" if generation handling is trapped in WinRT code. The cleanest path here is to keep `MediaControllerService` thin and put the generation/coordinator logic in a separate internal class — let Task 11 surface that.

6. **Build + test:** `dotnet build TrackDot.sln -c Debug --no-restore` then `dotnet test TrackDot.sln -c Debug --no-build --filter MediaPropertyMapperTests`. Both must succeed.

**Commit message:** `feat: track active Windows media session`

---

## Pitfalls to remember

- **WinRT callbacks arrive on arbitrary threads.** The WPF UI thread binding will throw if you update `INotifyPropertyChanged` properties from a non-dispatcher thread. Always marshal through `SynchronizationContext` or `Dispatcher`.
- **Async work crossing session switches.** If a user starts playing Track A, the manager briefly switches to Track B mid-`TryGetMediaPropertiesAsync`, the old completion arrives with stale data. The generation counter is the only thing standing between you and the wrong track displayed. Check it before every publish.
- **Empty state on no session.** When `GetCurrentSession()` returns null, publish `MediaSessionSnapshot.Empty` immediately (don't just leave `Current` as the default initialization value forever).
- **Don't catch all exceptions.** SMTC may throw `COMException` with `HResult 0x800704C7` (no session) on the first read. That is normal and should be treated as "publish Empty", not log spam. Genuine exceptions should still be logged in debug builds.
- **Marshalling `ImageSource` is dangerous.** The WPF UI thread owns all `ImageSource` instances. Decoding happens in Task 4 (`ThumbnailDecoder`) and the result is `Freeze()`'d before publishing — frozen `BitmapImage` is thread-safe.

---

## Files in the workspace

```
TrackDot/
├── .gitignore              (already comprehensive - covers .vs/, bin/, obj/, TestResults/)
├── .gitattributes
├── .hermes/
│   ├── HANDOFF.md          (this file)
│   └── plans/
│       └── 2026-08-09_000000-track-dot-windows-smtc-popover.md
├── App.xaml                (untouched - has StartupUri="MainWindow.xaml", will beedited in Task 9)
├── App.xaml.cs             (untouched empty partial class)
├── AssemblyInfo.cs         (untouched)
├── MainWindow.xaml         (untouched template Grid)
├── MainWindow.xaml.cs      (untouched template)
├── Models/
│   ├── MediaPlaybackState.cs
│   ├── MediaSessionSnapshot.cs
│   ├── PlaybackSnapshot.cs
│   └── TransportCapabilities.cs
├── Services/
│   └── IMediaControllerService.cs
├── TrackDot.csproj
├── TrackDot.sln
├── TrackDot.csproj.user    (untouched)
└── tests/TrackDot.Tests/
    ├── MediaSessionSnapshotTests.cs
    ├── SmokeTests.cs
    └── TrackDot.Tests.csproj
```

Need to be created (planned, do not yet exist):
- `Models/` complete (no more needed)
- `Services/MediaControllerService.cs`
- `Services/MediaPropertyMapper.cs`
- `Services/ThumbnailDecoder.cs` (Task 4)
- `Services/ITrayIconService.cs`, `TrayIconService.cs` (Task 8)
- `Services/WindowPlacementService.cs` (Task 7)
- `Services/IStartupService.cs`, `StartupService.cs` (Task 10)
- `Commands/AsyncRelayCommand.cs` (Task 5)
- `ViewModels/MainViewModel.cs`, `SettingsViewModel.cs` (Tasks 6, 10)
- `Converters/TimeSpanTextConverter.cs` (Task 6)
- `SettingsWindow.xaml` + `.cs` (Task 10)
- `Assets/AppIcon.ico`, `Assets/PlaceholderArt.png` (Tasks 4, 7)
- `ProgressInterpolator` lives inside `Services/` (Task 6)

---

## Verification commands

```bash
cd "C:/Users/Herlandro Ando/Documents/Ando/sites_win/TrackDot"
dotnet restore TrackDot.sln
dotnet build TrackDot.sln -c Debug --no-restore
dotnet test TrackDot.sln -c Debug --no-build
dotnet build TrackDot.sln -c Release
```

Current `dotnet test` status: 16 / 16 passing.

---

## Decision points deferred for the next session

1. **Subagent-driven-development vs. main-thread execution.** The plan calls for one-subagent-per-task with spec + quality reviews. The previous session stayed in the main thread because the per-task files were small and incremental. The next session should pick one approach and apply it consistently. My recommendation: stay in the main thread for code authoring; review the diff against the spec and the quality checklist yourself before each commit. Saves context-switching cost.

2. **`AllowsTransparency` vs. `WindowStyle=None` + rounded border.** Plan §1.2 deferred this until testing. The next session should ship `WindowStyle=None` with `WindowChrome` rounded corners (the conservative path) and document the rendering tradeoff in `docs/SMOKE_TEST.md` once Task 12 lands.

3. **Source auto-switch policy.** Plan §1.5 says "follow `GetCurrentSession()` for MVP, expose source identity so a future source picker can be added." Implement literal `GetCurrentSession()` in Task 3, no auto-switching.

4. **First public distribution format.** Plan §7 recommends framework-dependent ZIP first, then MSIX if installation/startup-registration needs product-grade handling. For Task 14, ship a framework-dependent x64 artifact only; document the MSIX follow-up.
