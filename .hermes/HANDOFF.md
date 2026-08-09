# TrackDot Implementation — Session Handoff

**Date:** 2026-08-09 (Task 5 completed in this session)
**Session:** Resumed from plan `.hermes/plans/2026-08-09_000000-track-dot-windows-smtc-popover.md`
**Goal:** Implement the 14-task plan to turn the empty WPF template into a Windows SMTC tray popover.

Commit author: `Herlandro Tribiakto <herlandrotri@gmail.com>` (already configured in this repo).

**Last verification:** `dotnet test -c Debug --no-build` → 61 / 61 passing (3 smoke + 13 snapshot + 20 mapper + 12 decoder + 13 command). Both Debug and Release build with 0 warnings, 0 errors. Run on 2026-08-09 by the closing session; the next session should treat these numbers as authoritative only after re-running `dotnet test` themselves — counts drift if a test is added and the suite is not re-run.

---

## Status: Tasks 1, 2, 3, 4 & 5 complete, Tasks 6-14 pending

| # | Task | Status | Commit |
|---|------|--------|--------|
| 1 | Establish clean solution baseline (TFM, .gitignore, test project) | ✅ done | `13f85c1` |
| 2 | Define media state and transport contracts (Models, IMediaControllerService) | ✅ done | `f3f96aa` |
| 3 | Implement SMTC session discovery and event lifecycle | ✅ done | `9869f15` |
| 4 | Decode album artwork safely | ✅ done | `82131e0` |
| 5 | Implement command dispatch and capability gating | ✅ done | (this session) |
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

### Task 3 — SMTC session discovery and event lifecycle (commit `9869f15`)

**Files created:**
- `Services/MediaPropertyMapper.cs` — pure static class mapping SMTC enums and shape records into `MediaSessionSnapshot` / `PlaybackSnapshot` / `TransportCapabilities`. Consumes small data shapes (`SessionShape`, `MediaPropertiesShape`, `PlaybackInfoShape`, `ControlsShape`, `TimelineShape`) instead of WinRT runtime classes — the SMTC playback-controls class has no public constructor and read-only properties, so it cannot be substituted in tests.
- `Services/MediaControllerService.cs` — `IMediaControllerService` implementation. Owns the `GlobalSystemMediaTransportControlsSessionManager`, wires the three property-grouped event subscriptions on each session, centralises session replacement behind a generation counter, marshals every WinRT callback through the captured `SynchronizationContext` before publishing, and exposes command methods (`TogglePlayPauseAsync` / `PreviousAsync` / `StopAsync` / `NextAsync`) that forward to the active session.
- `tests/TrackDot.Tests/MediaPropertyMapperTests.cs` — 20 tests covering all six SMTC playback statuses, capability flag combinations, every mapper input null-case, and the timeline-baseline fallback rules.

**Total tests:** 48 (3 smoke + 13 snapshot + 20 mapper + 12 decoder). All pass.

**Gotchas the next session needs to know:**

1. **WinRT runtime classes cannot be `new`'d in tests.** `GlobalSystemMediaTransportControlsSessionPlaybackControls` has no public constructor and read-only properties; the same applies to media-properties and timeline-properties classes. The mapper therefore consumes record shapes, and the service projects SMTC objects into those shapes. **Do not change the mapper's input types to the runtime classes** — the test project cannot supply substitutes.

2. **SMTC type names are exact.** The timeline class is `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionTimelineProperties` (with `Session` in the middle). The last-updated field on the timeline is `LastUpdatedTime`, not `LastUpdated`. The control methods `TryPlayAsync` / `TryPauseAsync` / `TrySkipPreviousAsync` / `TryStopAsync` / `TrySkipNextAsync` return `Windows.Foundation.IAsyncOperation<bool>`, not `<int>`. If a compile error names any of these, check the spelling before assuming a missing API.

3. **`IAsyncOperation<T>` requires `using Windows.Foundation;`** in any file that returns or awaits one. The CS052 error otherwise is "`IAsyncOperation<T>` not found" — easy to misread as a missing package.

4. **`TryGetMediaPropertiesAsync()` returns `IAsyncOperation<MediaProperties?>`** — the result may be null (the source app has not populated metadata yet). Always null-check before reading `Title` / `Artist` / `AlbumTitle` / `Thumbnail`.

5. **Synchronous SMTC reads (`GetPlaybackInfo()`, `GetTimelineProperties()`)** do not need to be `async Task` — they execute inline on the marshaled UI thread. Marking them `async Task` without an `await` triggers CS1998.

6. **The service uses `Volatile.Read` / `Volatile.Write` on `_currentSnapshot` and `_generation`** so the dispatcher-thread publish path and the worker-thread generation check stay coherent. **Do not remove these** — the handoff's "stale async result" hazard is real and the generation check only works if the reads are volatile.

7. **The artwork decode in `DecodeArtworkAsync` is now a real `ThumbnailDecoder` call** (Task 4). The signature is still `Task<ImageSource?>` so downstream code is unchanged. The decoder returns `null` for every failure mode — null stream, throwing delegate, faulted task, pre-cancelled token, malformed bytes, WinRT COM errors. The controller service relies on this contract; do not let the decoder start throwing.

8. **Lifecycle tests for the service itself are deferred to Task 11** (per the handoff plan). `InternalsVisibleTo TrackDot.Tests` is already wired in `TrackDot.csproj`, so the service can be exercised directly from tests when Task 11 lands.

### Task 4 — ThumbnailDecoder (commit `82131e0`)

**Files created:**
- `Services/ThumbnailDecoder.cs` — static class. `public const int MaxPixelSize = 256`. Public method `DecodeAsync(Func<Task<Stream>>, CancellationToken)` returns `Task<ImageSource?>`. Aspect-preserving clamp via private `ComputeScaledSize` (internal-shim `ComputeScaledSizeForTest` exposes it to tests).
- `tests/TrackDot.Tests/ThumbnailDecoderTests.cs` — 12 tests covering `MaxPixelSize` policy (positive power-of-two), `ComputeScaledSize` aspect-preserving clamp (landscape/portrait/square/small), and `DecodeAsync` failure contract (null stream, throwing delegate, faulted task, pre-cancelled token never invokes delegate, malformed bytes swallowed by catch-all, public-return-type smoke test).

**Files modified:**
- `Services/MediaControllerService.cs` — `DecodeArtworkAsync(object? thumbnail)` no longer stubs. Bridges `IRandomAccessStreamReference` → `IRandomAccessStreamWithContentType` → managed `Stream` via a small adapter (`OpenThumbnailAsManagedStreamAsync`) and delegates the decode to `ThumbnailDecoder.DecodeAsync`. Added `using System.IO;`.
- `tests/TrackDot.Tests/TrackDot.Tests.csproj` — added `<Compile Include="ThumbnailDecoderTests.cs" />`.

**Gotchas the next session needs to know:**

1. **`SoftwareBitmap.CopyTo(WriteableBitmap)` does NOT exist in the CsWinRT projection** for the .NET 6 SDK ref (`Microsoft.Windows.SDK.NET.Ref` 10.0.19041.x). It exists in C++/WinRT only. The handoff's Task-4 plan was wrong on this point — the suggested pipeline `SoftwareBitmap → UWP WriteableBitmap` would not compile. The working path is `SoftwareBitmap.CopyToBuffer(Windows.Storage.Streams.Buffer)` → `buffer.AsStream()` → managed `byte[]` → `WPF WriteableBitmap.BackBuffer` via `Marshal.Copy`.

2. **`Windows.UI.Xaml.Media.Imaging.WriteableBitmap` is not usable as a decode target.** It only exposes a `(int, int)` ctor and a `PixelBuffer` (`IBuffer`) — no DPI, no PixelFormat. There is no way to construct one that matches the SMTC pixel data. Use the WPF-native `System.Windows.Media.Imaging.WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null)` and write to `BackBuffer` directly.

3. **The `BitmapDecoder` symbol is ambiguous in `UseWPF` projects.** `System.Windows.Media.Imaging.BitmapDecoder` (WPF) and `Windows.Graphics.Imaging.BitmapDecoder` (WinRT) collide when both namespaces are imported. The decoder file aliases the WinRT one: `using WinRTBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;`. Do not remove the alias.

4. **CsWinRT runtime classes have no public `Dispose`/`Close`.** `BitmapDecoder`, `SoftwareBitmap`, and `Windows.Storage.Streams.Buffer` all implement `IClosable.Dispose` internally but the projection does not expose it to C# code. They are GC-managed. The decoder does not attempt explicit cleanup. `System.Windows.Media.Imaging.WriteableBitmap` IS a managed object — `Lock`/`Unlock` must be paired (try/finally).

5. **`SoftwareBitmap.CopyToBuffer(IBuffer)` requires `Length` set.** A freshly constructed `new Buffer((uint)bufferSize)` has `Length == 0` and `CopyToBuffer` will refuse to write. Set `Length = (uint)bufferSize` before calling.

6. **The xUnit runner cannot exercise the live `BitmapDecoder.CreateAsync` pipeline.** WinRT COM activation only initialises inside a UI process. The unit tests cover the failure contract (`openStream` throws → null) and the clamp math (`ComputeScaledSize`) but the happy-path decode is verified manually via `docs/SMOKE_TEST.md` during Task 12. Do not attempt to add a "decode a real PNG" test — it will throw `COMException` on the test runner.

7. **The decode is intentionally fire-and-forget at the contract level.** `MediaControllerService` swallows all exceptions around `DecodeArtworkAsync` (the surrounding `RefreshMediaPropertiesAsync` already has a top-level catch). The decoder also swallows internally as a defence-in-depth. If you add logging, do it in `MediaControllerService` — the decoder stays a pure mapping from `Stream → ImageSource`.

---

## Task 5 — AsyncRelayCommand (this session)

**Files created:**
- `Commands/AsyncRelayCommand.cs` — `ICommand` impl per the handoff spec. Two ctors (`Func<Task>` and `Func<object?, Task>`), each with an optional canExecute (`Func<bool>` / `Func<object?, bool>`). `Execute` is `async void` with `try/catch/finally` (finally raises `CanExecuteChanged`). `RaiseCanExecuteChanged()` is concrete-only (no `CommandManager.RequerySuggested` hook).
- `tests/TrackDot.Tests/AsyncRelayCommandTests.cs` — 13 tests: parameterless + parameterized ctor null-checks, parameterless + parameterized execute paths, three `CanExecute` cases (no delegate, parameterless, parameterized), `CanExecuteChanged` fires after Execute, two exception-swallow paths (synchronous throw + faulted task), `RaiseCanExecuteChanged` with/without subscribers, `ICommand` surface compatibility.

**Files modified:**
- `tests/TrackDot.Tests/TrackDot.Tests.csproj` — added `<Compile Include="AsyncRelayCommandTests.cs" />`.

**Total tests:** 61 (3 smoke + 13 snapshot + 20 mapper + 12 decoder + 13 command). All pass.

**Gotchas the next session needs to know:**

1. **The parameterless ctor must null-check `execute` BEFORE forwarding to the parameterized ctor via a lambda.** The original `: this(_ => execute(), …)` chain hid the null inside the lambda — the inner ctor's `ArgumentNullException.ThrowIfNull` saw a non-null wrapper and let the bad delegate through to click time. The fix is an explicit check at the top of the parameterless ctor and direct field assignment. The handoff's pseudocode `: this(_ => execute(), …)` was wrong on this point.
2. **`RaiseCanExecuteChanged` is concrete-only by design.** The view-model layer (Task 6) will hold `AsyncRelayCommand` typed concretely to call it; XAML data-binding goes through `ICommand`, which does NOT expose this method. Do not add it to a separate interface or to `ICommand` — the spec is "manual refresh without `CommandManager`".
3. **`async void` is required by `ICommand` but the `try/catch` is not optional.** An uncaught exception in `Execute` would surface as a fail-fast on the dispatcher. The controller service already swallows internally; this is belt-and-suspenders.
4. **`CanExecuteChanged` fires in a `finally` block** so it runs even if the execute delegate throws. The view-model can therefore swap Play ⇄ Pause based on `CanExecuteChanged` after every click without worrying about whether the click succeeded.

---

## Next: Task 6 — Build view model and progress interpolation

Task 6 consumes the building blocks now in place:

- `Commands/AsyncRelayCommand.cs` — wraps each of the four `IMediaControllerService` transport methods (`TogglePlayPauseAsync` / `PreviousAsync` / `StopAsync` / `NextAsync`) for XAML data-binding. The view-model holds them as the concrete `AsyncRelayCommand` type so it can call `RaiseCanExecuteChanged()` after `TransportCapabilities` updates. Each `canExecute` delegate reads the corresponding flag (`CanPlay` / `CanPause` / `CanStop` / `CanGoPrevious` / `CanGoNext`) from `PlaybackSnapshot.Capabilities`, with `TransportCapabilities.None` collapsing every button to disabled.
- `Services/IMediaControllerService.cs` — the four methods to wrap. The service already swallows command exceptions internally via the catch in `InvokeOnSessionAsync` (`Services/MediaControllerService.cs:494-505` region). The command's own `try/catch` is the second line of defence.
- `Models/TransportCapabilities.cs` — the flag record that drives `CanExecute`.
- `Models/MediaPlaybackState.cs` — `Playing` vs not-`Playing` decides whether `TogglePlayPauseAsync` should send Pause or Play (the service already does this internally; the command layer just forwards).

The view-model also introduces the playback-position interpolation: between `TimelinePropertiesChanged` events the `Position` field needs to advance smoothly so the slider doesn't stutter. `ProgressInterpolator` is the planned helper. Watch for the typical bug where the interpolator resets on every event — it should keep ticking from the last event's `Position`/`LastUpdatedTime` instead.

---

## Pitfalls to remember

- **WinRT callbacks arrive on arbitrary threads.** The WPF UI thread binding will throw if you update `INotifyPropertyChanged` properties from a non-dispatcher thread. Always marshal through `SynchronizationContext` or `Dispatcher`.
- **Async work crossing session switches.** If a user starts playing Track A, the manager briefly switches to Track B mid-`TryGetMediaPropertiesAsync`, the old completion arrives with stale data. The generation counter is the only thing standing between you and the wrong track displayed. Check it before every publish.
- **Empty state on no session.** When `GetCurrentSession()` returns null, publish `MediaSessionSnapshot.Empty` immediately (don't just leave `Current` as the default initialization value forever).
- **Don't catch all exceptions.** SMTC may throw `COMException` with `HResult 0x800704C7` (no session) on the first read. That is normal and should be treated as "publish Empty", not log spam. Genuine exceptions should still be logged in debug builds.
- **Marshalling `ImageSource` is dangerous.** The WPF UI thread owns all `ImageSource` instances. Decode happens in Task 4 (`ThumbnailDecoder`) and the result is `Freeze()`'d before publishing — frozen `BitmapSource` is thread-safe.
- **WinRT runtime classes have no public constructors.** Every mapper/decoder that wants to be testable must accept a record / delegate / stream rather than the runtime class. This pattern is established in Task 3 and applies again in Task 4.
- **The `_context.Post` callback may be dropped** if the dispatcher is shutting down. Treat dropped callbacks as "silently no-op" rather than retrying — the service is being torn down anyway.
- **`BitmapDecoder` is ambiguous in WPF projects.** Both `System.Windows.Media.Imaging.BitmapDecoder` and `Windows.Graphics.Imaging.BitmapDecoder` exist. Use a `using Xxx = Windows.Graphics.Imaging.BitmapDecoder;` alias when both namespaces are imported.
- **WinRT runtime classes have no public `Dispose`/`Close` in the CsWinRT projection.** `BitmapDecoder`, `SoftwareBitmap`, and `Buffer` all implement `IClosable` but the projection does not surface it to C#. They are GC-managed. Don't try to call `.Dispose()` on them.
- **`SoftwareBitmap.CopyToBuffer(Buffer)` requires `Length` set on the buffer.** A fresh `new Buffer(capacity)` has `Length == 0` and the call refuses to write.

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
├── App.xaml                (untouched - has StartupUri="MainWindow.xaml", will be edited in Task 9)
├── App.xaml.cs             (untouched empty partial class)
├── AssemblyInfo.cs         (untouched)
├── Commands/
│   └── AsyncRelayCommand.cs
├── MainWindow.xaml         (untouched template Grid)
├── MainWindow.xaml.cs      (untouched template)
├── Models/
│   ├── MediaPlaybackState.cs
│   ├── MediaSessionSnapshot.cs
│   ├── PlaybackSnapshot.cs
│   └── TransportCapabilities.cs
├── Services/
│   ├── IMediaControllerService.cs
│   ├── MediaControllerService.cs
│   ├── MediaPropertyMapper.cs
│   └── ThumbnailDecoder.cs
├── TrackDot.csproj
├── TrackDot.sln
├── TrackDot.csproj.user    (untouched)
└── tests/TrackDot.Tests/
    ├── AsyncRelayCommandTests.cs
    ├── MediaPropertyMapperTests.cs
    ├── MediaSessionSnapshotTests.cs
    ├── SmokeTests.cs
    ├── ThumbnailDecoderTests.cs
    └── TrackDot.Tests.csproj
```

Need to be created (planned, do not yet exist):
- `Models/` complete (no more needed)
- `ViewModels/MainViewModel.cs`, `SettingsViewModel.cs` (Tasks 6, 10)
- `Converters/TimeSpanTextConverter.cs` (Task 6)
- `ProgressInterpolator` lives inside `Services/` (Task 6)
- `Services/WindowPlacementService.cs` (Task 7)
- `Services/ITrayIconService.cs`, `TrayIconService.cs` (Task 8)
- `Services/IStartupService.cs`, `StartupService.cs` (Task 10)
- `SettingsWindow.xaml` + `.cs` (Task 10)
- `Assets/AppIcon.ico`, `Assets/PlaceholderArt.png` (Tasks 4, 7)

---

## Verification commands

```bash
cd "C:/Users/Herlandro Ando/Documents/Ando/sites_win/TrackDot"
dotnet restore TrackDot.sln
dotnet build TrackDot.sln -c Debug --no-restore
dotnet test TrackDot.sln -c Debug --no-build
dotnet build TrackDot.sln -c Release
```

Current `dotnet test` status: 61 / 61 passing (3 smoke + 13 snapshot + 20 mapper + 12 decoder + 13 command).
Current `dotnet build` status: Debug and Release both build with 0 warnings, 0 errors.

---

## Decision points deferred for the next session

1. **Subagent-driven-development vs. main-thread execution.** The plan calls for one-subagent-per-task with spec + quality reviews. The previous two sessions stayed in the main thread because the per-task files were small and incremental. The next session should pick one approach and apply it consistently. My recommendation: stay in the main thread for code authoring; review the diff against the spec and the quality checklist yourself before each commit. Saves context-switching cost.

2. **`AllowsTransparency` vs. `WindowStyle=None` + rounded border.** Plan §1.2 deferred this until testing. The next session should ship `WindowStyle=None` with `WindowChrome` rounded corners (the conservative path) and document the rendering tradeoff in `docs/SMOKE_TEST.md` once Task 12 lands.

3. **Source auto-switch policy.** Plan §1.5 says "follow `GetCurrentSession()` for MVP, expose source identity so a future source picker can be added." Implemented literally `GetCurrentSession()` in Task 3, no auto-switching. Continue this convention in Task 5 (commands) and forward — `MediaControllerService` is the only place that picks a session; everything downstream treats it as a single active source.

4. **First public distribution format.** Plan §7 recommends framework-dependent ZIP first, then MSIX if installation/startup-registration needs product-grade handling. For Task 14, ship a framework-dependent x64 artifact only; document the MSIX follow-up.

5. **ThumbnailDecoder input type — RESOLVED in Task 4.** Chose `Func<Task<Stream>>`. The `IRandomAccessStreamReference` lives behind a small adapter inside `MediaControllerService.OpenThumbnailAsManagedStreamAsync`. The decoder itself stays pure. Same pattern should apply to any future CsWinRT runtime-class input.
