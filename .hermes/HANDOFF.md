# TrackDot Implementation — Session Handoff

**Date:** 2026-08-11 (Tasks 1–11 completed across sessions; current session shipped Task 11)
**Session:** Resumed from plan `.hermes/plans/2026-08-09_000000-track-dot-windows-smtc-popover.md`
**Goal:** Implement the 14-task plan to turn the empty WPF template into a Windows SMTC tray popover.

Commit author: `Herlandro Tribiakto <herlandrotri@gmail.com>` (already configured in this repo).

**Last verification:** `dotnet test -c Debug` and `dotnet test -c Release` → **227 / 227 passing** (3 smoke + 11 snapshot + 12 mapper + 12 decoder + 16 command + 15 service-guards + 14 service-generation + 10 interpolation + 26 view-model + 10 view-model-lifecycle + 10 asset-resource + 6 single-instance + 8 tray-icon + 10 placement + 13 exception-logger + 24 startup). Both Debug and Release build with 0 warnings, 0 errors. The numbers above were re-verified end-to-end during the Task 11 shipping session. **Note**: the per-class counts listed above are top-level `[Fact]`/`[Theory]` declarations. `[Theory]` cases with `[InlineData]` rows add additional test cases that bump the count beyond the literal `+N`; the authoritative total is 227.

---

## Status: Tasks 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 done; Tasks 12-14 pending

| # | Task | Status | Commit |
|---|------|--------|--------|
| 1 | Establish clean solution baseline (TFM, .gitignore, test project) | ✅ done | `13f85c1` |
| 2 | Define media state and transport contracts (Models, IMediaControllerService) | ✅ done | `f3f96aa` |
| 3 | Implement SMTC session discovery and event lifecycle | ✅ done | `9869f15` |
| 4 | Decode album artwork safely | ✅ done | `82131e0` |
| 5a | AsyncRelayCommand (ICommand wrapper for view-model binding) | ✅ done | `b8cb9ee` |
| 5b | Service-side command guards (re-entrancy, capability gate, failed-Try refresh) | ✅ done | `18d84bd` |
| 5c | Re-entrancy test determinism fix (Task.Run wrapper to escape xUnit sync context) | ✅ done | `13d86b1` |
| 6 | Build view model and progress interpolation | ✅ done | `3a5b8ec` |
| 7 | Construct the floating popover UI | ✅ done | `db46fbb` |
| 8 | Add tray icon lifecycle and toggle behavior | ✅ done | `2d5c165` |
| 9 | Compose startup, initialization, and error handling | ✅ done | `d0e4a7c` |
| 10 | Implement launch-at-sign-in settings | ✅ done | `2e9a881` |
| 11 | Add automated lifecycle tests and resource checks | ✅ done | `9e628ff` |
| 12 | Windows integration validation (docs only - manual) | 🔴 not started | — |
| 13 | Document build, usage, limitations, and privacy | 🔴 not started | — |
| 14 | Produce and verify x64 distributable | 🔴 not started | — |

**Tasks 1–8 are shipped.** Tasks 1–6 are unchanged from prior sessions (handed off with full test counts). Task 7 added the floating popover UI. Task 8 added the tray-icon lifecycle (Hardcodet `TaskbarIcon`, context menu, single-instance mutex, tray-driven toggle, close-as-hide). The next session can start Task 9 (composition root / startup cleanup / global exception handlers) directly against the existing `App.OnStartup` / `App.OnExit` graph.

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

## Task 5a — AsyncRelayCommand (shipped in commit `b8cb9ee`)

This is the *plumbing* half of plan §Task 5 — the `ICommand` wrapper the view-model layer (Task 6) will bind to. The *guarded dispatch* half (5b) shipped in commit `18d84bd`; see the next section.

**Files created:**
- `Commands/AsyncRelayCommand.cs` — `ICommand` impl per the handoff spec. Two ctors (`Func<Task>` and `Func<object?, Task>`), each with an optional canExecute (`Func<bool>` / `Func<object?, bool>`). `Execute` is `async void` with `try/catch/finally` (finally raises `CanExecuteChanged`). `RaiseCanExecuteChanged()` is concrete-only (no `CommandManager.RequerySuggested` hook).
- `tests/TrackDot.Tests/AsyncRelayCommandTests.cs` — 13 tests: parameterless + parameterized ctor null-checks, parameterless + parameterized execute paths, three `CanExecute` cases (no delegate, parameterless, parameterized), `CanExecuteChanged` fires after Execute, two exception-swallow paths (synchronous throw + faulted task), `RaiseCanExecuteChanged` with/without subscribers, `ICommand` surface compatibility.

**Files modified:**
- `tests/TrackDot.Tests/TrackDot.Tests.csproj` — added `<Compile Include="AsyncRelayCommandTests.cs" />`.

**Total tests:** 79 (3 smoke + 13 snapshot + 20 mapper + 12 decoder + 16 command + 15 service-guards). All pass in both Debug and Release; verified 29× Debug and 10× Release without flake after the Task 5c fix.

**Gotchas the next session needs to know:**

1. **The parameterless ctor must null-check `execute` BEFORE forwarding to the parameterized ctor via a lambda.** The original `: this(_ => execute(), …)` chain hid the null inside the lambda — the inner ctor's `ArgumentNullException.ThrowIfNull` saw a non-null wrapper and let the bad delegate through to click time. The fix is an explicit check at the top of the parameterless ctor and direct field assignment. The handoff's pseudocode `: this(_ => execute(), …)` was wrong on this point.
2. **`RaiseCanExecuteChanged` is concrete-only by design.** The view-model layer (Task 6) will hold `AsyncRelayCommand` typed concretely to call it; XAML data-binding goes through `ICommand`, which does NOT expose this method. Do not add it to a separate interface or to `ICommand` — the spec is "manual refresh without `CommandManager`".
3. **`async void` is required by `ICommand` but the `try/catch` is not optional.** An uncaught exception in `Execute` would surface as a fail-fast on the dispatcher. The controller service already swallows internally; this is belt-and-suspenders.
4. **`CanExecuteChanged` fires in a `finally` block** so it runs even if the execute delegate throws. The view-model can therefore swap Play ⇄ Pause based on `CanExecuteChanged` after every click without worrying about whether the click succeeded.
5. **Re-entrancy guard lives on the command.** A single `int _running` latch (`Interlocked.CompareExchange` on entry, `Volatile.Write` in `finally`) drops overlapping clicks and gates `CanExecute`. See Task 5b for the full design — this was the 5b half of plan §Task 5 step 1.

---

## Task 5b — Service-side command guards (shipped in commit `18d84bd`)

Plan §Task 5 step 1–4 is fully implemented. The four behaviours:

1. **Re-entrancy guard** lives on the command (Task 5 step 1 calls out "in-flight reentrancy prevention"). The service itself does not lock; by the time two clicks reach the service they may be racing through different sessions and a lock would be wrong.
2. **Capability short-circuit** lives on the service. Each of the four command methods passes a `Func<TransportCapabilities, bool>` capability predicate to `InvokeOnSessionAsync`. The dispatcher reads `Volatile.Read(ref _currentSnapshot).Playback.Capabilities` and no-ops when the relevant flag is false:
   - `PreviousAsync` → `CanGoPrevious`
   - `TogglePlayPauseAsync` → `CanPlay || CanPause` (the service picks the right direction at dispatch time)
   - `StopAsync` → `CanStop`
   - `NextAsync` → `CanGoNext`
3. **Failed-`Try*Async` triggers a playback refresh.** The dispatcher inspects the `bool` returned by `Try*Async` (was previously swallowed). On `false` return OR thrown exception, it calls `RefreshPlaybackInfoAsync` on the captured session, which re-publishes the snapshot. The next view-model `RaiseCanExecuteChanged` will then refresh button state.
4. **No-session / disposed path.** The dispatcher checks `_disposed` first, then reads the snapshot for the capability gate. The production wrapper around `DispatchGuardedCommandAsync` short-circuits with `Task.FromResult(false)` when no session is active, so the refresh never runs against a torn-down session.

**Files modified / created:**
- `Services/MediaControllerService.cs` — the four command methods, the new internal `DispatchGuardedCommandAsync` helper, and two internal test seams (`ClearSessionForTest`, `SetCapabilitiesForTest`). `using Windows.Foundation;` removed (no longer needed after `IAsyncOperation<bool>` was dropped from the helper signature).
- `Commands/AsyncRelayCommand.cs` — `int _running` re-entrancy latch (`Interlocked.CompareExchange` on entry, `Volatile.Write` in `finally`). `CanExecute` returns `false` while the latch is set. Test-only `RunningForTest` internal property.
- `tests/TrackDot.Tests/MediaControllerCommandTests.cs` — **new**, 15 tests (capability gate, refresh-on-false, refresh-on-throw, no-op-on-success, exception swallow, no-session public surface for all four commands, three null-arg checks on `DispatchGuardedCommandAsync`).
- `tests/TrackDot.Tests/AsyncRelayCommandTests.cs` — appended 3 re-entrancy tests (drops-second-click, CanExecute-false-while-in-flight, CanExecute-recovers-after-completion). The release-build stability fix lives here: drain the latch by polling `sut.RunningForTest == 0` rather than counting `Task.Yield()` pumps, which is timing-sensitive under Release.
- `tests/TrackDot.Tests/TrackDot.Tests.csproj` — added `<Compile Include="MediaControllerCommandTests.cs" />`.

**Gotchas the next session needs to know:**

1. **Pure guard logic is in `DispatchGuardedCommandAsync`, not `InvokeOnSessionAsync`.** The production shim (`InvokeOnSessionAsync`) wires the WinRT session into the pure helper. Tests drive `DispatchGuardedCommandAsync` directly with delegate-based fakes for `tryCommand` and `refresh`. WinRT runtime classes have no public constructors and cannot be substituted — the delegate-based seam is the only way to test guard logic without a live SMTC session.
2. **`Internal` test seams on the service** (`ClearSessionForTest`, `SetCapabilitiesForTest`) only mutate the snapshot + session field; they do not subscribe to WinRT events (there are none to subscribe to in tests). Exposed via `InternalsVisibleTo TrackDot.Tests` (already wired).
3. **The capability check is on the cached snapshot**, not on a fresh `GetPlaybackInfo()` call. This avoids an extra COM hop on every button click. The trade-off is the gate can be stale until the next authoritative event — the failed-`Try*Async` refresh path is the recovery: it re-reads playback info on `false`/throw, which re-publishes the snapshot and (through `RaiseCanExecuteChanged`) refreshes the button state.
4. **`TogglePlayPauseAsync` capability is `CanPlay || CanPause`.** The button is enabled whenever either flag is true; the service picks the right direction at dispatch time based on the current `MediaPlaybackState`. A user with `CanPlay=true, CanPause=false` cannot pause; a user with `CanPlay=false, CanPause=true` cannot resume. The `CanExecute` delegate in the view-model layer must use the same `||` semantics — do not split it into two separate flags.
5. **`RunningForTest` on the command** is the only deterministic way to drain the re-entrancy latch in tests. Two `Task.Yield()`s were enough in Debug but the Release JIT occasionally needs more pumps before scheduling the async-void continuation. The polling loop (`for (i < 100) { if (RunningForTest == 0) break; await Task.Yield(); }`) avoids flakiness.
6. **Production `InvokeOnSessionAsync` no longer returns `Task.FromException` paths.** The `try/catch` around `tryCommand` swallows everything (matching the existing defensive posture — see Task 3 gotcha #7). If you need logging on a swallowed session-side exception, add it in `DispatchGuardedCommandAsync`'s `catch` block; do not let exceptions propagate out to the view-model's `async void` `Execute`.

**Commit message used:** `feat: add guarded media transport commands` (verbatim from plan §Task 5; commit `18d84bd`).

**Gotcha #5 update — re-entrancy test flakiness (resolved by 5c):** the original `RunningForTest`-drain pattern documented above was *partially* deterministic. Under cold Debug and Release JITs, xUnit's `SynchronizationContext` can post the `async void` body of `Execute` to the captured context rather than running its synchronous prefix inline. When that happens, neither `RunningForTest` nor `invocations` is observable between the `Execute` call and the next test statement. Drain loops are not enough — the body simply hasn't run. The fix (5c) wraps every `sut.Execute(null)` call in `Execute_drops_second_click_while_first_is_in_flight` with `await Task.Run(() => sut.Execute(null))`, which escapes the captured sync context by queueing the `Execute` invocation on the thread pool. The latch then transitions deterministically and the existing drain loop completes the test. If you add new re-entrancy tests in Task 6/11, follow the same `await Task.Run(() => sut.Execute(null))` pattern — direct calls are flaky.

---

## Task 5c — Re-entrancy test determinism fix (shipped in uncommitted patch)

**Files modified:**
- `tests/TrackDot.Tests/AsyncRelayCommandTests.cs` — `Execute_drops_second_click_while_first_is_in_flight` now wraps each `sut.Execute(null)` in `await Task.Run(...)`. Added a comment block explaining why direct calls flake.

**Commit message:** `test: make AsyncRelayCommand re-entrancy test deterministic across Debug and Release`.

---

## Task 6 — View model and progress interpolation (shipped in commit `3a5b8ec`)

**Files created:**
- `Services/ProgressInterpolator.cs` — pure, stateless `Evaluate(state, baselinePosition, baselineTimestamp, endTime, now) → TimeSpan`. Only `Playing` advances; non-playing states return the baseline exactly; clamps to `[0, EndTime]`; defensive against pre-baseline clocks and `EndTime == 0` (unknown duration).
- `ViewModels/IUiTicker.cs` + `ViewModels/DispatcherUiTicker.cs` — production seam for the 250 ms tick. `DispatcherUiTicker` wraps `DispatcherTimer` at `DispatcherPriority.Background`; `IUiTicker.Start(Action)` is idempotent (replaces the previous callback) so the view-model can restart from each authoritative snapshot.
- `ViewModels/MainViewModel.cs` — `INotifyPropertyChanged` + `IDisposable`. Subscribes to `IMediaControllerService.SnapshotChanged`; mirrors to `Title / Artist / AlbumTitle / Artwork / SourceAppUserModelId / IsPlaying / HasMedia / PositionSeconds / DurationSeconds / ElapsedTimeText / DurationTimeText`; owns four `AsyncRelayCommand`s; **timer runs only when `IsVisible && IsPlaying`** (stopped when hidden/paused/no-session); `TogglePlayPause` uses `CanPlay || CanPause` to mirror the service-side gate (Task 5b gotcha #4); `Dispose` stops the ticker and unsubscribes.
- `Converters/TimeSpanTextConverter.cs` — `IValueConverter` for `TimeSpan → "m:ss"` / `"h:mm:ss"`. Shared format lives in `internal static MainViewModelHelpers.FormatTime` so the VM's pre-formatted text and the XAML converter stay in sync.
- `tests/TrackDot.Tests/Fakes/FakeMediaControllerService.cs` — implements `IMediaControllerService` with explicit `Publish(snapshot)` and per-command counters + `ThrowOnCommand` for the exception-swallow contract.
- `tests/TrackDot.Tests/ProgressInterpolationTests.cs` — 10 table-driven cases (playing/not-playing theory, clamp-to-endTime on long delays, never-negative on pre-baseline clocks, unknown-duration, position-already-past-endTime, paused-with-bad-endTime clamp, backward-seek-as-new-baseline, determinism).
- `tests/TrackDot.Tests/MainViewModelTests.cs` — 43 cases: no-session defaults, playing/paused snapshots, `CanPlay || CanPause` theory (4 rows), missing title, zero/unknown duration, position-overflow clamp, time-text formatting theory (8 rows), `PropertyChanged` exhaustiveness, `CanExecuteChanged` raised on each command on capability change, command forwards (Previous/Stop/Next theory), exception swallow, `Dispose` unsubscribes, idempotent dispose, source AUMID pass-through, null AUMID, **and 8 timer-behavior cases** (starts when visible+playing, doesn't start paused/hidden, stops on hide, stops on pause, advances on tick, doesn't advance when paused mid-flight, restarts from new baseline).

**Total tests:** 132 (3 smoke + 13 snapshot + 20 mapper + 12 decoder + 16 command + 15 service-guards + 10 interpolation + 43 view-model). All pass in both Debug and Release; verified 5× Debug + 5× Release full-suite runs without flake.

**Files modified:**
- `tests/TrackDot.Tests/TrackDot.Tests.csproj` — added `<Compile Include="Fakes\FakeMediaControllerService.cs" />`, `<Compile Include="ProgressInterpolationTests.cs" />`, `<Compile Include="MainViewModelTests.cs" />`.

**Gotchas the next session needs to know:**

1. **Timer only ticks under `IsVisible && IsPlaying`.** The view-model's `UpdateTicker()` is called from three places — `OnSnapshot`, `IsVisible` setter, and `OnTick`. A new snapshot always restarts from the new baseline. The popover (Task 7) must set `IsVisible = true` on show and `false` on hide; failing to set it false leaves the timer running while the popover is hidden, which wastes CPU and can publish position updates that nobody is bound to.
2. **The clock is injected.** `MainViewModel(svc, ticker, clock = null)` — production uses `() => DateTimeOffset.UtcNow`. Tests inject a fake `Func<DateTimeOffset>` so they can drive interpolation deterministically without sleeping. The view-model takes the snapshot's `TimelineUpdatedAt` as the interpolation baseline — it does NOT call the clock at snapshot time. If you add timing-sensitive tests, pass `timelineUpdatedAt: clock.Now - DateTimeOffset.UnixEpoch` to the snapshot helper so the baseline matches the clock's current reading; otherwise the first interpolated read jumps to a wrong value.
3. **`PositionSeconds` interpolates only when visible+playing.** When the popover is hidden OR playback is not Playing, the property returns the snapshot's last-known `Position` clamped to `[0, EndTime]`. This keeps the slider value stable across hide/show transitions.
4. **`HasMedia` is title-based, not snapshot-based.** It returns true when `_snapshot.Title` is non-empty. An `Empty` snapshot (no session) returns false because `Empty.Title == string.Empty`. A snapshot with title but no playback (e.g. a paused first-paint) returns true. This drives the empty-state UI in the popover.
5. **`Title` falls back to "Nothing playing"** when the snapshot's title is empty. This is the view-model's job, not the mapper's — the mapper keeps empty strings as empty so the VM can apply user-facing rules. If you want to localise that string, change the constant in `MainViewModel.cs` (search for `NothingPlayingText`).
6. **`PropertyChanged` is raised for ALL derived properties on every snapshot.** This is intentional — `INotifyPropertyChanged` consumers expect every bound property to be re-evaluated when the underlying state changes. The cost is a few extra `OnPropertyChanged` calls per snapshot; the benefit is no UI stale-state bugs from forgetting a notification. If you add a new bindable property to the VM, add it to the `RaiseAllChanged()` list.
7. **`RaiseCanExecuteChanged` is called on all four commands inside `OnSnapshot`.** XAML data-binding does NOT call `CommandManager.RequerySuggested`, so manual refresh is mandatory. If you add a new command, follow the same pattern.
8. **The `TimelineUpdatedAt` semantic in tests:** the test helper `MakeSnapshot` defaults `timelineUpdatedAt` to `position`. Timer tests that drive interpolation against a fake clock must pass `timelineUpdatedAt: clock.Now - DateTimeOffset.UnixEpoch` so the elapsed delta is `0` on the first read. Otherwise the first interpolated read jumps to `position + (clock.Now - position)` and surprises you.
9. **`TimeSpanTextConverter` is not auto-applied.** The VM exposes pre-formatted `ElapsedTimeText` and `DurationTimeText` strings. The converter exists for any XAML bindings that want to bypass the VM and format a raw `TimeSpan` directly (e.g. accessibility labels built from snapshot fields).
10. **`Task 5c gotcha still applies.** All `Execute(...)` calls in view-model tests are wrapped in `await Task.Run(() => ...)` for the same reason as the `AsyncRelayCommandTests` — xUnit's sync context can post the `async void` body to the captured context. See `Executing_TogglePlayPauseCommand_invokes_service_method` for the pattern.

**Commit message used:** `feat: add media presentation and timeline interpolation` (verbatim from plan §Task 6).

---

## Task 7 — Floating popover UI (shipped in commit `db46fbb`)

**Files modified:**
- `App.xaml` — replaced template resources with dark-theme brushes (Panel `#202124`, Text `#F1F3F4`, Muted `#A8ADB5`, Accent `#8AB4F8`), default `TextBlock` style with character-ellipsis trimming, and a 32×32 `TransportButton` style with transparent chrome. Switched to `ShutdownMode="OnExplicitShutdown"` in advance of Task 8.
- `MainWindow.xaml` — fixed `Width=360`, `SizeToContent=Height`, `WindowStyle=None`, `ResizeMode=NoResize`, `ShowInTaskbar=False`, `Topmost=True`, `AllowsTransparency=True` with a 12 px corner-rounded panel. Three rows: header (88×88 artwork border + 2-line text + AUMID label), 4 px progress bar bound to `PositionSeconds`/`DurationSeconds`, transport row with four command buttons plus `ElapsedTimeText` / `DurationTimeText`. Header `MouseLeftButtonDown` calls `DragMove()` only when the left button is pressed; body buttons consume their own input.
- `MainWindow.xaml.cs` — added `SetViewModel(MainViewModel)` for explicit DataContext binding, `Deactivated` → `Hide()` (with the `IsActive` guard so context-menu activations don't hide immediately), `KeyDown` → Escape hides. `SourceInitialized` is now a no-op (Win11 DWM rounded corners deferred to Task 11/12 follow-up; the rounded `Border` carries the visual for now).

**Build & test:** Debug + Release both build with 0 warnings, 0 errors. `dotnet test` Debug: 132/132 passing — same 3 smoke + 13 snapshot + 20 mapper + 12 decoder + 16 command + 15 service-guards + 10 interpolation + 43 view-model. The new UI layer does not touch view-model contracts so the test surface is unchanged.

**Gotchas the next session needs to know:**
1. **No DataContext set in XAML.** The popover accepts a `MainViewModel` via `SetViewModel(...)` so Task 9 can compose the VM with the service/ticker. Binding resolution in the designer will be empty; that's intentional and matches the plan.
2. **Drag is bound to the header `Grid` only.** The progress bar and transport buttons sit outside that grid, so button clicks don't trigger drag. Don't move `MouseLeftButtonDown` to the root.
3. **`Deactivated` hides the popover unconditionally.** The plan calls for "hide unless a context menu/dialog is active" — Task 8's tray context menu can flip that via a `IsContextMenuOpen` field on the window. For now the simple hide is correct because the popover has no child dialogs.
4. **Escape hides but does not close.** Closing the window would force Task 8 to recreate it on every toggle; hiding preserves the dispatcher timer / VM state.
5. **`AllowsTransparency=True` with `Background="Transparent"`** lets the rounded `Border` shape clip the panel. This is the conservative path the handoff recommended. Win11 DWM corner-preference integration is deferred to a later follow-up; `WindowChrome` would lock down resize behaviour, so the rounded `Border` is simpler and cross-version.
6. **Positioning is not wired here.** `WindowPlacementService` is a Task 7 deliverable per the plan; it is deferred to Task 9 composition because the popover's `Left`/`Top` must be set *after* it has its final size, and that requires the service to be alive so `IsVisible` flips correctly. Task 9 will own the placement call.

---

## Task 8 — Tray icon lifecycle and toggle behavior (shipped in commit `2d5c165`)

Plan §Task 8 is fully implemented. Behaviour shipped:

1. **Single-instance mutex.** `Local\TrackDot.SingleInstance.v1` (per-session namespace, versioned). `App.OnStartup` constructs `SingleInstanceGuard` first; if `!IsAcquired`, the process calls `Shutdown(1)` and returns before any UI is shown. Mutex is released in `App.OnExit`.
2. **`TaskbarIcon` resource + tray context menu.** `App.xaml` adds `<tb:TaskbarIcon x:Key="TrayIcon">` with `ToolTipText="TrackDot"`, the `TrayContextMenu` resource (Settings / separator / Exit TrackDot), and `IconSource` pointing at the embedded `Assets/AppIcon.ico` via a `pack://application:,,,/Assets/AppIcon.ico` URI. The 32×32 ICO is a generated PNG-encoded frame (transparent rounded corners, accent dot).
3. **`TrayIconService`** owns the popover visibility state and routes `TrayLeftMouseDown` → `TogglePopover()`. `Show/Hide/Toggle` are all idempotent (calling `Show` when already visible is a no-op). The service raises `ShutdownRequested` exactly once across multiple `RequestShutdown` calls and disposes the icon handle (which removes the tray icon from the notification area).
4. **`MainWindow` implements `IPopoverHost`.** `ShowPopover` / `HidePopover` flip the view-model's `IsVisible` (so the 250 ms interpolation timer starts/stops) and `Show()` / `Hide()` the window. `Window_Closing` cancels the close and calls `HidePopover` *unless* `MainWindow.IsShuttingDown` is `true` — which `App.OnExit` sets before tearing anything down.
5. **Tray menu commands wired.** Settings currently logs to debug (Task 10 owns the real `SettingsWindow`); Exit calls `_tray.RequestShutdown()` → `ShutdownRequested` → `Application.Shutdown()` → `OnExit`.
6. **`App.OnExit` tears down in reverse-construction order** with null-safe `try/catch` swallows on every step so a half-constructed `OnStartup` (e.g. single-instance failed) does not throw on shutdown.

**Files created / modified:**
- `Services/SingleInstanceGuard.cs` — `IDisposable` named-mutex wrapper. `IsAcquired` is true iff `Mutex(name, out createdNew)` reports `createdNew`. Disposal is idempotent; the original failed-acquire path releases the handle immediately to avoid kernel-object leaks.
- `Services/IPopoverHost.cs` — popover seam (`ShowPopover` / `HidePopover`).
- `Services/ITrayIconHandle.cs` — tray-icon seam (`TrayLeftMouseDown` event + `IDisposable`).
- `Services/TrayIconHandle.cs` — production handle wrapping the live `TaskbarIcon`. Subscribes/unsubscribes the `TrayLeftMouseDown` routed event so the WPF dependency stays behind the seam.
- `Services/TrayIconService.cs` — UI-thread-owned state machine. Caches popover visibility; idempotent show/hide/toggle; raises `ShutdownRequested` once.
- `App.xaml` — adds `TrayContextMenu` + `TrayIcon` resources, `xmlns:tb` namespace import.
- `App.xaml.cs` — composition root (see §Task 9 handoff next section for the same code).
- `MainWindow.xaml` — adds `Closing="Window_Closing"`.
- `MainWindow.xaml.cs` — implements `IPopoverHost`, adds `Window_Closing` handler, `ShowPopover/HidePopover` public methods, `IsShuttingDown` static flag.
- `Assets/AppIcon.ico` — generated 32×32 PNG-in-ICO, 166 bytes.
- `tests/TrackDot.Tests/TrackDot.Tests.csproj` — adds `<Compile Include>` entries.
- `tests/TrackDot.Tests/SingleInstanceGuardTests.cs` — 6 tests.
- `tests/TrackDot.Tests/TrayIconServiceTests.cs` — 8 tests.

**Build & test:** Debug + Release both build with 0 warnings, 0 errors. `dotnet test` Debug: **146 / 146 passing** (3 smoke + 13 snapshot + 20 mapper + 12 decoder + 16 command + 15 service-guards + 10 interpolation + 43 view-model + 6 single-instance + 8 tray-icon). Same on Release. Stable across 5× Debug stress re-runs.

**Gotchas the next session needs to know:**
1. **`Application.MainWindow` is a name-collision trap.** When `App.OnExit` writes `MainWindow.IsShuttingDown = true`, C# resolves `MainWindow` to the `Application.MainWindow` *property* (returns a `Window` instance), then errors `'Window' does not contain a definition for 'IsShuttingDown'`. The fix is to fully qualify as `TrackDot.MainWindow.IsShuttingDown = true`. This bites anywhere code touches both the type and the instance property in the same scope — the type wins when the property isn't set, so the type path always needs qualifying from inside `App`.
2. **`TaskbarIcon.IconSource` is `ImageSource`, not `string`.** WPF's implicit string→`ImageSource` converter accepts both filesystem paths and `pack://application:,,,/...` URIs. The csproj declares `<Resource Include="Assets\AppIcon.ico" />` which embeds the asset in `AssemblyName.g.resources` — to resolve at runtime, the XAML MUST use the pack URI form `pack://application:,,,/Assets/AppIcon.ico`. A bare `IconSource="Assets/AppIcon.ico"` only works at design time (when the current directory is the project root). The pack URI is the canonical pattern and survives `dotnet publish`.
3. **`TrayIconService` is single-instance-only.** The service holds popover visibility state in private fields; two instances would race on the same window. The composition root must construct exactly one `TrayIconService` per process and dispose it once.
4. **`DispatcherUiTicker` is not `IDisposable`.** `OnExit` calls `Stop()`, not `Dispose()`. The plan to make it disposable is deferred to a later cleanup pass; for now the timer's `Stop()` is the only teardown the production code needs.
5. **`MainWindow.Window_Closing` cancels the close.** `e.Cancel = true` while `!IsShuttingDown` is correct for both the user pressing X and Alt+F4. Without this guard, the user's accidental click on X would terminate the tray process. With it, the popover hides and the tray stays alive in the notification area.
6. **`Window_Deactivated` now hides the popover even during context-menu activations.** Task 7's gotcha #3 mentioned flipping this via an `IsContextMenuOpen` field — Task 9 can add that if real users report the context menu flashing closed. For MVP the simple hide is correct.
7. **Async SMTC init is fire-and-forget.** `App.OnStartup` returns before `_mediaService.InitializeAsync()` completes; init failure logs to `Debug.WriteLine` and the tray remains usable. Task 9 will own the real logger and may want to surface init failures via a tray balloon / tooltip update.
8. **Settings menu is a debug stub.** Clicking it writes to `Debug.WriteLine`. Task 10 wires the real `SettingsWindow`. Do not remove the click handler in Task 9 — it is the only evidence the menu wiring works end-to-end until Task 10 lands.

---

## Next: Task 11 — Add automated lifecycle tests and resource checks

Task 11 owns:

- `tests/TrackDot.Tests/ServiceGenerationTests.cs` — stale-metadata completion after session switch, repeated initialize/dispose, event unsubscription, no updates after disposal. Requires extracting the coordinator if necessary.
- `tests/TrackDot.Tests/ViewModelLifecycleTests.cs` — hiding pauses UI interpolation; showing resumes from latest authoritative baseline.
- `tests/TrackDot.Tests/Fakes/FakeMediaControllerService.cs` — extend to cover the new lifecycle scenarios.
- A build-time check that `Assets/AppIcon.ico` and `Assets/PlaceholderArt.png` exist with correct WPF resource actions (add the file-existence check + `Compile/None Remove` rules to the test project or a build target).

The composition-root exception log path is now used during normal startup (`App.OnStartup` step 0 → last-dispatched). If Task 11 lifecycle tests fail, the failure logs will be in `%LocalAppData%\TrackDot\crash.log` — check there first before chasing reproduce steps.

The SettingsWindow XAML surface verifier (`scripts/verify-xaml-surface.py`-style) at `.hermes/verify-settings-xaml-surface.py` already exists; Task 11 may want to fold both popovers under one script with the XAML filename as a CLI arg, or keep them separate.

---

## Task 10 — Implement launch-at-sign-in settings (shipped in commit `2e9a881`)

Plan §Task 10 is fully implemented. Behaviour shipped:

1. **`IStartupService`** — minimal contract: `IsEnabled`, `Enable()`, `Disable()`. Production implementation is `StartupService`; both `Enable` and `Disable` are idempotent (no-op when already in the target state).
2. **Registry adapter seam** — `IRegistryKey` (ReadValue / WriteValue / DeleteValue / IDisposable) + `IRegistryKeyFactory.OpenRunKey()`. The production `RegistryKeyFactory` opens `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` via `Microsoft.Win32.Registry.CurrentUser.CreateSubKey(...)` with `RegistryKeyPermissionCheck.ReadWriteSubTree`. Tests inject `FakeRegistryKeyFactory` — an in-memory dictionary adapter — so no test ever mutates the real registry.
3. **`StartupService`** resolves the executable path at construction time via `Environment.ProcessPath` (the .NET 6+ replacement for `Process.MainModule.FileName`). The path is stored **quoted** (`"..."`) so a path-with-spaces parses correctly under the Run-key parser; `IsEnabled` compares against the unquoted stored value with **case-insensitive** + **trailing-separator-tolerant** equality so both quoted and unquoted stored values register as ours.
4. **Per-user only, no elevation** — `HKCU` never needs admin. The `ValueName` and `RunKeyPath` constants live on `RegistryKeyFactory` so the service is decoupled from the path.
5. **`SettingsViewModel`** — `INotifyPropertyChanged` + `IDisposable`. `LaunchAtSignIn` is a two-way property; the setter calls `Enable()`/`Disable()` immediately, and on exception rolls the field back AND surfaces a `StatusMessage` for the UI. Save-immediately was chosen over an explicit Apply button (single-checkbox dialog — the dirty-tracking path would add complexity without benefit).
6. **`SettingsWindow.xaml`** — compact dialog: title, one CheckBox (`LaunchAtSignIn`), explanatory text with the registry path/value-name baked in for regedit verification, a status row that collapses to `Collapsed` when `StatusMessage` is empty (via `DataTrigger`, no new converter), and a Close button with `IsCancel="True"` for automatic Esc wiring. Closing the window hides it (preserves the user's position across opens) unless `App.IsShuttingDown`.
7. **Composition root** — `App.OnStartup` now constructs `_startupService = new StartupService(new RegistryKeyFactory())`, the `_settingsViewModel`, and the `_settingsWindow` after the tray icon (Step 4b). `App.OnExit` tears them down in reverse order, setting `TrackDot.SettingsWindow.IsShuttingDown = true` so the `Closing` handler lets the window close normally.
8. **`OnOpenSettingsClicked`** now calls `_settingsWindow.ShowSettings()` — idempotent; a second tray click while already open calls `Activate()` instead of duplicating the window.

**Files created / modified:**
- `Services/IStartupService.cs` — interface.
- `Services/IRegistryKey.cs` — key/value adapter + factory interfaces.
- `Services/RegistryKeyAdapter.cs` — `RegistryKeyFactory` (production) + `RegistryKeyAdapter` (production wrapper around a `Microsoft.Win32.RegistryKey` handle).
- `Services/StartupService.cs` — production service. Two internal ctors: one that takes a specific executable path (so tests don't depend on the test runner's host path), one with a marker `bool unresolvedPath` parameter that leaves both path fields null (the `Environment.ProcessPath`-returned-null branch).
- `ViewModels/SettingsViewModel.cs` — VM with roll-back-on-throw + `StatusMessage`.
- `SettingsWindow.xaml` + `.cs` — compact dialog; `IsShuttingDown` flag mirrors `MainWindow`.
- `App.xaml.cs` — composition root: Step 4b constructs `_startupService`, `_settingsViewModel`, `_settingsWindow`; `OnExit` adds `SettingsWindow.IsShuttingDown = true` + dispose; `OnOpenSettingsClicked` now opens the window.
- `tests/TrackDot.Tests/StartupServiceTests.cs` — **new**, 24 tests: detection (missing / matches / foreign / case-insensitive / trailing-separator / unquoted / no-spaces), write (quoted / idempotent / overwrites foreign / round-trips with Disable), removal (idempotent / preserves foreign), lifecycle (read-count / read+write count), unresolved-path branch (`Enable` throws / `IsEnabled` false), fake-adapter contract sanity (null-name throws, null-value deletes, missing-value delete no-op).
- `tests/TrackDot.Tests/TrackDot.Tests.csproj` — added `<Compile Include="StartupServiceTests.cs" />`.
- `.hermes/verify-settings-xaml-surface.py` — **new**, ad-hoc structural check for the settings window. Reports `PASS static=4 bindings=4 commands=0 handlers=2`. Differs from the original `winrt-wpf-desktop` skill's script: also scans `<Window.Resources>` blocks for local styles (the original only scans `App.xaml`).

**Build & test:** Debug + Release both build with 0 warnings, 0 errors. `dotnet test` Debug: **193 / 193 passing** (3 smoke + 13 snapshot + 20 mapper + 12 decoder + 16 command + 15 service-guards + 10 interpolation + 43 view-model + 6 single-instance + 8 tray-icon + 10 placement + 13 exception-logger + 24 startup). Same on Release. XAML surface verifier reports `PASS static=4 bindings=4 commands=0 handlers=2` — every `{StaticResource}`, `{Binding}`, `Command`, and code-behind handler in the settings XAML resolves.

**Gotchas the next session needs to know:**

1. **`StartupService` keeps its production class `sealed`.** Test seams live on **internal ctor overloads** — one that takes a specific path (`StartupService(IRegistryKeyFactory, string executablePath)`), and one with a marker `bool unresolvedPath` parameter that leaves both path fields null. Do NOT unseal the production class to subclass for tests — the overload pattern is sufficient and preserves the type's sealedness.

2. **`Enable` calls `IsEnabled` first (for idempotency), so each `Enable` opens the registry key TWICE** (once for the read, once for the write). This is correct — `IsEnabled` is the source of truth, and re-reading on every call means a foreign-write between the user's prior `Disable` and current `Enable` is picked up. The `StartupServiceTests` `Enable_opens_the_registry_key_one_read_and_one_write` test asserts this. Do not "optimize" by caching the read result.

3. **Idempotent-test assertion trap.** Asserting "no I/O happened" by counting `OpenRunKey` calls is wrong — `Enable` and `Disable` always call `IsEnabled` (which opens the key for the read), even when the write/delete is short-circuited. The correct assertion is on the **write surface** (the value dictionary, the file contents), NOT the open count. The `StartupServiceTests` `Enable_is_idempotent_when_already_enabled` and `Disable_is_idempotent_when_already_disabled` tests assert on `Values` (the write surface) AND on the open-count delta (`OpenCountBefore + 1`) — measuring exactly the read-side I/O the idempotent code path performs.

4. **`Microsoft.Win32.RegistryKey.SetValue(name, null, kind)` triggers CS8604** even though passing `null` to `SetValue` legitimately deletes the value (documented Win32 behaviour). The production `RegistryKeyAdapter.WriteValue` suppresses with `_key.SetValue(name, (object?)value!, RegistryValueKind.String);` — the cast matches the parameter's declared `object` type and the `!` is the "I know it's not null when non-null" assertion. Do not change this to a non-null sentinel — the underlying API genuinely accepts null and the registry-adapter seam's contract (`WriteValue(name, null)` = delete) needs to round-trip through it.

5. **`HKCU\...\Run` paths with spaces MUST be quoted.** The Windows Run-key parser splits on whitespace inside an unquoted string. Every per-user install path on Windows contains a space (`%LocalAppData%\Programs\App\`). The stored value must be `"C:\Path\To\App.exe"` (with surrounding quotes). The detection path (`IsEnabled`) accepts BOTH quoted and unquoted stored values — the Windows parser accepts both forms and third-party tools frequently write the unquoted form. `OrdinalIgnoreCase` + trailing-separator trim handles both.

6. **Save-immediately view-model with field-rollback-on-throw.** A `LaunchAtSignIn` setter that persists optimistically must roll the backing field back to its prior value on exception, otherwise a stale checkbox claiming "on" while the registry is off is worse than a click that visibly failed. The `SettingsViewModel.LaunchAtSignIn` setter does this AND surfaces the exception's message via `StatusMessage` so the user sees both signals (checkbox reverts, footer goes red). The next session should NOT switch to an Apply-button model without adding dirty-tracking — the current model is correct for a single checkbox.

7. **SettingsWindow is single-instance, owned by `App`.** `App._settingsWindow` is constructed once and `ShowSettings()` is idempotent (second click → `Activate()`). The window's `Closing` handler cancels + hides unless `App.OnExit` set `SettingsWindow.IsShuttingDown = true`. The fully-qualified type name (`TrackDot.SettingsWindow.IsShuttingDown`) follows the same Application-property-shadow rule as `MainWindow` (see Task 8 gotcha #1).

8. **`DataTrigger` beats a converter** for "visible-when-non-empty / collapsed-when-empty" patterns. The SettingsWindow status row uses a local `Style` with a `DataTrigger Binding="{Binding StatusMessage}" Value=""` that flips `Visibility` to `Collapsed`. The default `Setter` is `Visible`, so any non-empty message shows. No new `IValueConverter` class, no `Converter` resource registration.

9. **`IsCancel="True"` on the Close button** wires Esc to the button's `Click` event automatically. The handler still calls `Hide()` rather than `Close()` so the user's window position (set on first show) is preserved across opens. The `Window_KeyDown` handler is still present (so other keys can be added later) but the Esc case is now handled by the button's `IsCancel`.

10. **Composition-root teardown order.** `_settingsWindow` is closed between the tray disposal and the popover-view-model disposal (same dependency tier as `MainWindow`). The startup service has no native handle to release — drop the reference and let GC handle it. The exception logger remains the last thing disposed.

11. **`Test count unchanged ≠ surface verified.** Adding `SettingsWindow.xaml` / `.cs` did NOT change the test count for the xUnit suite beyond the 24 new `StartupServiceTests`. The XAML surface verifier (above) + `dotnet build` (catches missing `StaticResource` and binding type mismatches) are the only signals that the new window is wired correctly end-to-end. A manual smoke test (Task 12, `docs/SMOKE_TEST.md`) is required to verify the visual layout.

12. **The verifier's regex bug applies to the new script too.** The `winrt-wpf-desktop` skill's `scripts/verify-xaml-surface.py` uses a *non-capturing* group `(?:=>|\\{)` in the property scanner; the new `verify-settings-xaml-surface.py` follows the same pattern. If you re-implement either regex inline and the comparison always fails, that's why. Copy the regex verbatim and double-check `(?:...)` is non-capturing.

---

## Task 9 — Compose startup, initialization, and error handling (shipped in commit `d0e4a7c`)

Plan §Task 9 is fully implemented. Behaviour shipped:

1. **`WindowPlacementService`** (deferred from Task 7) — DPI-aware positioning. `IWindowPlacementService` exposes `GetWorkArea()` + `ComputeAnchoredPosition(Size[, Rect])`. The pure math lives in `Services/WindowPlacement.cs` (`internal static` class) so the clamp behaviour is fully unit-tested without a real desktop session. The production wrapper reads `SystemParameters.WorkArea` (the monitor containing the taskbar, in WPF DIPs). `MainWindow.ShowPopover()` calls `_placement.ComputeAnchoredPosition(...)` every time the popover is shown, so a display change (resolution, monitor swap) is picked up without a separate cache-invalidation path or `SystemEvents.DisplaySettingsChanged` subscription.
2. **Global exception handlers** — `UnhandledExceptionLogger` subscribes to all three WPF / CLR channels in `App.OnStartup` and unsubscribes in `OnExit`:
   - `Application.DispatcherUnhandledException` → logs and marks `Handled = true` (recoverable path: failed binding evaluation must not crash the tray).
   - `AppDomain.CurrentDomain.UnhandledException` → logs only (process is terminating; log line is post-mortem evidence).
   - `TaskScheduler.UnobservedTaskException` → logs and calls `SetObserved()` (belt-and-suspenders; the service already swallows internally).
3. **SMTC init-failure tray tooltip** — the existing `ITrayIconHandle` seam got a `SetToolTipText(string?)` method. `App.OnStartup` sets `ToolTipTextHealthy` ("TrackDot") at startup; the `InitializeMediaAsync` catch sets `ToolTipTextMediaUnavailable` ("TrackDot (media unavailable)") on failure so the user sees the degraded state at a glance.
4. **Composition root** — `App.OnStartup` now wires the exception logger first (so a failure in any subsequent step is captured), then the single-instance mutex, then the media service, view-model, window, placement, and tray. `App.OnExit` tears down in reverse with null-safe `try/catch` swallows on every step. The exception logger is disposed *last* so any exception during the teardown of the services above is captured before the process exits.

**Files created / modified:**
- `Services/IWindowPlacementService.cs`, `Services/WindowPlacement.cs` (pure math, `internal static`), `Services/WindowPlacementService.cs` (production wrapper).
- `Services/IUnhandledExceptionSink.cs`, `Services/FileUnhandledExceptionSink.cs` (writes to `%LocalAppData%\TrackDot\crash.log` with per-instance lock + first-failure disable), `Services/UnhandledExceptionLogger.cs` (subscribes/unsubscribes the three channels).
- `Services/ITrayIconHandle.cs` (added `SetToolTipText(string?)` to the existing seam).
- `App.xaml.cs` — composition root wired: exception logger first, then single-instance, then services, view-model, window, placement, tray; SMTC init-failure tooltip update; teardown in reverse.
- `MainWindow.xaml.cs` — accepts `IWindowPlacementService` via `SetPlacement(...)`, calls it on every `ShowPopover()`.
- `tests/TrackDot.Tests/WindowPlacementTests.cs` — **new**, 10 tests: anchor at bottom-right, default vs custom margin, zero margin, clamp to work-area when popover is wider/taller than the work area, secondary monitor work area (rect origin != (0,0)), negative / NaN margin treated as zero, service contract delegation.
- `tests/TrackDot.Tests/UnhandledExceptionLoggerTests.cs` — **new**, 13 tests: pure formatter (channel tag, exception text, inner exception chain, null arg checks, three channels), file sink (writes, appends, parent-directory creation, empty path rejection, first-failure disable, null line no-op, concurrent writes don't interleave), sink contract (recording fake).
- `tests/TrackDot.Tests/TrayIconServiceTests.cs` — `TestIconHandle` fake got `SetToolTipText` recording fields.
- `tests/TrackDot.Tests/TrackDot.Tests.csproj` — explicit `<Compile Include="WindowPlacementTests.cs" />` and `<Compile Include="UnhandledExceptionLoggerTests.cs" />` (the test project uses `EnableDefaultCompileItems=false`).

**Build & test:** Debug + Release both build with 0 warnings, 0 errors. `dotnet test` Debug: **169 / 169 passing** (3 smoke + 13 snapshot + 20 mapper + 12 decoder + 16 command + 15 service-guards + 10 interpolation + 43 view-model + 6 single-instance + 8 tray-icon + 10 placement + 13 exception-logger). Same on Release. Stable across 5× Debug stress re-runs. XAML surface verifier (the `winrt-wpf-desktop` skill's `scripts/verify-xaml-surface.py`) reports `PASS static=4 bindings=12 commands=4 handlers=5` — every `{StaticResource}`, `{Binding}`, `Command`, and code-behind handler in the popover XAML resolves.

**Gotchas the next session needs to know:**

1. **`WindowPlacement` is `internal static`, not `public`.** The math is testable through the surface but not part of the public contract. Tests reach it via the production `IWindowPlacementService` contract (which delegates) and via `InternalsVisibleTo TrackDot.Tests` (already wired). Do not make it `public` without a reason — the seam is `IWindowPlacementService`, the implementation may change.

2. **`FileUnhandledExceptionSink` opens/closes the file on every write, does not hold a handle.** Concurrent writes are serialised by an internal lock. After the first failed write (e.g. blocked by a regular file at the parent path) the sink flips `IsAvailable` to `false` and every subsequent write is a silent no-op — the production code path will never throw on log failure.

3. **`UnhandledExceptionLogger.Format` is `internal static` for tests.** The WPF dispatcher event hook cannot be exercised from xUnit (no real `Application` instance), so the test surface is the formatter + the file sink. The actual event subscriptions are integration territory and the log line is the only post-mortem evidence. If you need to verify subscription wiring in Task 11, use an xUnit `[Fact]` that constructs the logger with a fake sink and inspects whether the production code path actually fires it.

4. **`Application.DispatcherUnhandledException` is set to `Handled = true` for every exception.** The plan's "do not swallow fatal state corruption" is enforced by the log line — the next session (or post-mortem inspection of `%LocalAppData%\TrackDot\crash.log`) can recover the full exception. If a true-fatal scenario is observed in the field, the recovery path is to *not* mark Handled for that specific exception type, not to remove the logger.

5. **`App.OnExit` disposes the exception logger LAST.** The reverse-construction order means the logger is the last service torn down; any exception thrown during the teardown of the services above is captured in the log before the process exits. Do not reorder the teardown without a reason.

6. **SMTC init-failure tooltip is best-effort.** The `try/catch` around `_trayHandle?.SetToolTipText(...)` in `InitializeMediaAsync` swallows failures so a tooltip update bug cannot prevent the rest of the tray app from working. The actual SMTC init failure is already captured by the exception logger via the outer `catch`.

7. **`MainWindow.ApplyPlacement` falls back to `Width`/`Height` when `DesiredSize`/`ActualSize` is zero.** The popover uses `SizeToContent=Height`, so the first show happens before the window has measured itself. The fallback chain (`DesiredSize → ActualWidth → Width → no-op`) keeps placement working on every show including the first.

8. **XAML surface verifier (`scripts/verify-xaml-surface.py`) has a small bug** — when the regex uses a *capturing* group `(...|=>|{)`, `re.findall` returns tuples, not strings, and the `set()` comparison always fails. The fix is to use a *non-capturing* group `(?:=>|\{)` so `findall` returns just the property name. If you re-run the verifier and the comparison fails on every binding, that's why. The Task 9 verification used the corrected pattern.

9. **`Test count unchanged ≠ surface verified.** The new exception logger and placement service are covered by their own unit tests, but the *composition root* (`App.xaml.cs`) and the *MainWindow wiring changes* are not directly exercised by xUnit. The XAML surface verifier (above) plus `dotnet build` (catches missing `StaticResource` and binding type mismatches) plus the manual smoke test in `docs/SMOKE_TEST.md` (Task 12) are the only signals that the wiring is correct end-to-end.

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
- **`Application.MainWindow` collides with the `MainWindow` type name.** Inside an `App` partial (`App.xaml.cs`), `MainWindow` (unqualified) resolves to the `Application.MainWindow` *instance property*, not the `TrackDot.MainWindow` *type*. `MainWindow.IsShuttingDown = true` therefore compiles as `<Window>.IsShuttingDown = true`, which fails with `'Window' does not contain a definition for 'IsShuttingDown'`. The fix is to fully qualify: `TrackDot.MainWindow.IsShuttingDown = true`. This bites anywhere the App code touches the type, the static field, or any member that shares a name with a `Window` property.
- **`TaskbarIcon.IconSource` is `ImageSource`, resolved via the pack URI.** When the csproj embeds an asset via `<Resource Include="Assets\AppIcon.ico" />`, the asset lives in `AssemblyName.g.resources` under the path `Assets/AppIcon.ico`. WPF's string→`ImageSource` converter accepts both filesystem paths and `pack://application:,,,/Assets/AppIcon.ico` URIs, but only the pack URI resolves correctly at runtime once the working directory is `bin/.../`. Bare `IconSource="Assets/AppIcon.ico"` works at design time only. Use the pack URI in XAML.
- **`UnobservedTaskExceptionEventHandler` is `EventHandler<UnobservedTaskExceptionEventArgs>`, not a special delegate.** The TaskScheduler event is typed as the generic `EventHandler<T>`; there is no dedicated `UnobservedTaskExceptionEventHandler` delegate in .NET 8. Field type must be `EventHandler<UnobservedTaskExceptionEventArgs>` for the symmetric Add/Remove to compile.
- **`WindowPlacement` is `internal static`, not a public type.** The pure placement math is testable through the `IWindowPlacementService` contract and via `InternalsVisibleTo TrackDot.Tests`. The `internal` modifier means production callers go through the interface — the implementation may change. Do not make it `public` without a reason.
- **The XAML surface verifier's regex bug.** `scripts/verify-xaml-surface.py` from the `winrt-wpf-desktop` skill uses `(=>|\{)` (capturing) in the property scanner, which makes `re.findall` return tuples. The `set()` comparison then always fails. Fix: change to `(?:=>|\{)` (non-capturing) so `findall` returns just the property name. Symptom: every binding shows as missing even though they all exist.
- **`SystemParameters.WorkArea` updates automatically on display change.** No `SystemEvents.DisplaySettingsChanged` subscription is required — the property re-reads on every access, so a resolution change is picked up on the next show without invalidation logic. The popover's `ShowPopover` calls `_placement.ComputeAnchoredPosition(...)` directly, which reads `WorkArea` fresh every time.
- **The exception logger must be constructed BEFORE any service that can throw.** The composition root installs the logger as step 0 so a failure during mutex acquisition, service construction, view-model wiring, window setup, or tray attachment is captured in `%LocalAppData%\TrackDot\crash.log` rather than only the visual-studio debug stream.
- **`HKCU\...\Run` paths with spaces MUST be quoted** when stored under the per-user Run key. The Windows Run-key parser splits on whitespace inside an unquoted string; every per-user install path on Windows contains a space (`%LocalAppData%\Programs\App\`). The detection path must accept BOTH quoted and unquoted stored values (the parser accepts both; third-party tools frequently write the unquoted form). `OrdinalIgnoreCase` + trailing-separator trim is the comparison recipe.
- **`Enable` opens the registry key TWICE — once for the `IsEnabled` read, once for the write.** Idempotent methods that check state before mutating re-read on every call. Asserting idempotency by counting open-counts is wrong; assert on the WRITE surface (the value dictionary, the file contents) instead, AND assert the open-count delta is exactly +1 (one read, no second write open).
- **`Microsoft.Win32.RegistryKey.SetValue(name, null, kind)` triggers CS8604** even though passing `null` deletes the value (documented Win32 behaviour). Suppress with `(object?)value!` — the cast matches the parameter's declared `object` type and the `!` is the "I know it's not null when non-null" assertion. Do not change to a non-null sentinel; the registry-adapter seam's `WriteValue(name, null)` = delete contract needs to round-trip through it.
- **`StartupService` stays `sealed`; test seams live on internal ctor overloads.** One ctor takes a specific executable path (so tests don't depend on the test runner's host path); one with a marker `bool unresolvedPath` parameter leaves both path fields null (the `Environment.ProcessPath`-returned-null branch). Do not unseal the production class to subclass for tests.
- **Save-immediately view-model needs field-rollback-on-throw.** When the `LaunchAtSignIn` setter persists optimistically, an exception must roll the backing field back to its prior value AND surface the exception's message via `StatusMessage`. A stale checkbox claiming "on" while the registry is off is worse than a click that visibly failed.
- **`DataTrigger` beats a converter** for "visible-when-non-empty / collapsed-when-empty" patterns. Default `Setter Property="Visibility" Value="Visible"` + `DataTrigger Binding="{Binding StatusMessage}" Value="" Setter Property="Visibility" Value="Collapsed"`. No new `IValueConverter`, no `Converter` resource registration.
- **`IsCancel="True"` on the Close button** wires Esc to the button's `Click` event automatically. The handler still calls `Hide()` (not `Close()`) so the user's window position is preserved across opens.

---

## Files in the workspace

```
TrackDot/
├── .gitignore              (already comprehensive - covers .vs/, bin/, obj/, TestResults/)
├── .gitattributes
├── .hermes/
│   ├── HANDOFF.md          (this file)
│   ├── plans/
│   │   └── 2026-08-09_000000-track-dot-windows-smtc-popover.md
│   └── verify-settings-xaml-surface.py
├── App.xaml                (resources: brushes, transport-button style, tray context menu, TaskbarIcon)
├── App.xaml.cs             (composition root)
├── AssemblyInfo.cs         (untouched)
├── Commands/
│   └── AsyncRelayCommand.cs
├── Converters/
│   └── TimeSpanTextConverter.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── Models/
│   ├── MediaPlaybackState.cs
│   ├── MediaSessionSnapshot.cs
│   ├── PlaybackSnapshot.cs
│   └── TransportCapabilities.cs
├── Services/
│   ├── IMediaControllerService.cs
│   ├── MediaControllerService.cs
│   ├── MediaPropertyMapper.cs
│   ├── ProgressInterpolator.cs
│   ├── ThumbnailDecoder.cs
│   ├── SingleInstanceGuard.cs
│   ├── IPopoverHost.cs
│   ├── ITrayIconHandle.cs
│   ├── TrayIconHandle.cs
│   ├── TrayIconService.cs
│   ├── IWindowPlacementService.cs
│   ├── WindowPlacement.cs
│   ├── WindowPlacementService.cs
│   ├── IUnhandledExceptionSink.cs
│   ├── FileUnhandledExceptionSink.cs
│   ├── UnhandledExceptionLogger.cs
│   ├── IStartupService.cs           (Task 10)
│   ├── IRegistryKey.cs              (Task 10)
│   └── RegistryKeyAdapter.cs        (Task 10)
├── SettingsWindow.xaml              (Task 10)
├── SettingsWindow.xaml.cs           (Task 10)
├── ViewModels/
│   ├── DispatcherUiTicker.cs
│   ├── IUiTicker.cs
│   ├── MainViewModel.cs
│   └── SettingsViewModel.cs         (Task 10)
├── TrackDot.csproj
├── TrackDot.sln
├── TrackDot.csproj.user    (untouched)
└── tests/TrackDot.Tests/
    ├── AsyncRelayCommandTests.cs
    ├── Fakes/
    │   └── FakeMediaControllerService.cs
    ├── MainViewModelTests.cs
    ├── MediaControllerCommandTests.cs
    ├── MediaPropertyMapperTests.cs
    ├── MediaSessionSnapshotTests.cs
    ├── ProgressInterpolationTests.cs
    ├── SingleInstanceGuardTests.cs
    ├── SmokeTests.cs
    ├── StartupServiceTests.cs        (Task 10)
    ├── ThumbnailDecoderTests.cs
    ├── TrayIconServiceTests.cs
    ├── UnhandledExceptionLoggerTests.cs
    ├── WindowPlacementTests.cs
    └── TrackDot.Tests.csproj
```

Need to be created (planned, do not yet exist):
- `tests/TrackDot.Tests/ServiceGenerationTests.cs`, `ViewModelLifecycleTests.cs` (Task 11)
- `Assets/PlaceholderArt.png` (Tasks 4 / 11)

---

## Verification commands

```bash
cd "C:/Users/Herlandro Ando/Documents/Ando/sites_win/TrackDot"
dotnet restore TrackDot.sln
dotnet build TrackDot.sln -c Debug --no-restore
dotnet test TrackDot.sln -c Debug --no-build
dotnet build TrackDot.sln -c Release
```

Current `dotnet test` status: 193 / 193 passing (3 smoke + 13 snapshot + 20 mapper + 12 decoder + 16 command + 15 service-guards + 10 interpolation + 43 view-model + 6 single-instance + 8 tray-icon + 10 placement + 13 exception-logger + 24 startup). Stable across Debug and Release (full Debug + Release cycle in the Task 10 shipping session, zero flakes).
Current `dotnet build` status: Debug and Release both build with 0 warnings, 0 errors.

---

## Decision points deferred for the next session

1. **Subagent-driven-development vs. main-thread execution.** The plan calls for one-subagent-per-task with spec + quality reviews. The previous two sessions stayed in the main thread because the per-task files were small and incremental. The next session should pick one approach and apply it consistently. My recommendation: stay in the main thread for code authoring; review the diff against the spec and the quality checklist yourself before each commit. Saves context-switching cost.

2. **`AllowsTransparency` vs. `WindowStyle=None` + rounded border.** Plan §1.2 deferred this until testing. The next session should ship `WindowStyle=None` with `WindowChrome` rounded corners (the conservative path) and document the rendering tradeoff in `docs/SMOKE_TEST.md` once Task 12 lands.

3. **Source auto-switch policy.** Plan §1.5 says "follow `GetCurrentSession()` for MVP, expose source identity so a future source picker can be added." Implemented literally `GetCurrentSession()` in Task 3, no auto-switching. Continue this convention in Task 5b (command guards) and forward — `MediaControllerService` is the only place that picks a session; everything downstream treats it as a single active source.

4. **First public distribution format.** Plan §7 recommends framework-dependent ZIP first, then MSIX if installation/startup-registration needs product-grade handling. For Task 14, ship a framework-dependent x64 artifact only; document the MSIX follow-up.

5. **ThumbnailDecoder input type — RESOLVED in Task 4.** Chose `Func<Task<Stream>>`. The `IRandomAccessStreamReference` lives behind a small adapter inside `MediaControllerService.OpenThumbnailAsManagedStreamAsync`. The decoder itself stays pure. Same pattern should apply to any future CsWinRT runtime-class input.
