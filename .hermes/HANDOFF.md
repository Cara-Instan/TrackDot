# TrackDot Implementation — Session Handoff

**Date:** 2026-08-11 (Tasks 1–11 completed across sessions; current session shipped Task 11)
**Session:** Resumed from plan `.hermes/plans/2026-08-09_000000-track-dot-windows-smtc-popover.md`
**Goal:** Implement the 14-task plan to turn the empty WPF template into a Windows SMTC tray popover.

Commit author: `Herlandro Tribiakto <herlandrotri@gmail.com>` (already configured in this repo).

**Last verification:** `dotnet test -c Debug` and `dotnet test -c Release` → **227 / 227 passing** (3 smoke + 11 snapshot + 12 mapper + 12 decoder + 16 command + 15 service-guards + 14 service-generation + 10 interpolation + 26 view-model + 10 view-model-lifecycle + 10 asset-resource + 6 single-instance + 8 tray-icon + 10 placement + 13 exception-logger + 24 startup). Both Debug and Release build with 0 warnings, 0 errors. Framework-dependent x64 publish artifact produced at `artifacts/win-x64-framework-dependent/TrackDot.exe` (25 MB, runtimeconfig requires `Microsoft.WindowsDesktop.App 8.0.0`). Launched cleanly from a clean checkout — second instance exits with code 1 (single-instance mutex). **Note**: the per-class counts listed above are top-level `[Fact]`/`[Theory]` declarations. `[Theory]` cases with `[InlineData]` rows add additional test cases that bump the count beyond the literal `+N`; the authoritative total is 227.

---

## Status: Tasks 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 done

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
| 12 | Windows integration validation (docs only - manual) | ✅ done | `29c7061` |
| 13 | Document build, usage, limitations, and privacy | ✅ done | `742229b` |
| 14 | Produce and verify x64 distributable | ✅ done | `cd03c59` |

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
- `Services/MediaControllerService.cs` — the four command methods, the new internal `DispatchGuardedCommandAsync` hel

... [OUTPUT TRUNCATED - 41,050 chars omitted out of 90,977 total] ...

xit` disposes the exception logger LAST.** The reverse-construction order means the logger is the last service torn down; any exception thrown during the teardown of the services above is captured in the log before the process exits. Do not reorder the teardown without a reason.

6. **SMTC init-failure tooltip is best-effort.** The `try/catch` around `_trayHandle?.SetToolTipText(...)` in `InitializeMediaAsync` swallows failures so a tooltip update bug cannot prevent the rest of the tray app from working. The actual SMTC init failure is already captured by the exception logger via the outer `catch`.

7. **`MainWindow.ApplyPlacement` falls back to `Width`/`Height` when `DesiredSize`/`ActualSize` is zero.** The popover uses `SizeToContent=Height`, so the first show happens before the window has measured itself. The fallback chain (`DesiredSize → ActualWidth → Width → no-op`) keeps placement working on every show including the first.

8. **XAML surface verifier (`scripts/verify-xaml-surface.py`) has a small bug** — when the regex uses a *capturing* group `(...|=>|{)`, `re.findall` returns tuples, not strings, and the `set()` comparison always fails. The fix is to use a *non-capturing* group `(?:=>|\{)` so `findall` returns just the property name. If you re-run the verifier and the comparison fails on every binding, that's why. The Task 9 verification used the corrected pattern.

9. **`Test count unchanged ≠ surface verified.** The new exception logger and placement service are covered by their own unit tests, but the *composition root* (`App.xaml.cs`) and the *MainWindow wiring changes* are not directly exercised by xUnit. The XAML surface verifier (above) plus `dotnet build` (catches missing `StaticResource` and binding type mismatches) plus the manual smoke test in `docs/SMOKE_TEST.md` (Task 12) are the only signals that the wiring is correct end-to-end.

---

## Task 12 — Windows integration validation (shipped in commit `29c7061`)

Plan §Task 12 is fully implemented as a **docs + manual smoke matrix**. Behaviour shipped:

1. **`docs/SMOKE_TEST.md`** — a 12-section integration matrix covering: no-session, Chrome/Edge (YouTube), Spotify, VLC (with the version-dependent SMTC caveat called out explicitly), unsupported capabilities, source churn, window placement + DPI at 100/125/150%, popover lifecycle, 30-minute hidden+paused soak, 15-minute playback soak, launch-at-sign-in, and unobserved-exception channels.
2. **Pre-flight checklist** — every scenario starts with the same 5-step pre-flight (kill prior process, launch from explorer, confirm no taskbar button, confirm right-click menu, capture `crash.log` baseline).
3. **Resource-baseline form** — `§4` of the doc is a copy-paste-able template for capturing `HandleCount`, `WorkingSet64`, `CPU` at start / +5 min / +15 min. A handle-count growth > 5% over 15 min without track changes is the explicit leak signal.
4. **Known-limitations section** — captures the open product gaps honestly: OS-selected session only, `AllowsTransparency` rendering cost, VLC SMTC version variance, **the `PlaceholderArt.png` 1×1 file being unreferenced in XAML** (so the popover's artwork border shows the `#34373D` background when `Artwork == null`, not a visible fallback). No silent "fix" — every gap is named with the bounded patch that would address it.
5. **Player-specific-failure isolation rule** — `§6` of the doc is explicit: a player integration failure is a defect to isolate, not grounds to revert earlier reviewed work. Reproduction steps + `crash.log` snippet + isolation note are required for any failure to count as documented.

**Build & test:** No production code changes for Task 12 — the xUnit suite is unchanged at **227 / 227 passing** (Debug + Release). The doc was authored against the actual codebase (verified grep for `HttpClient`/`WebClient`/etc. — no networking in production), the actual `Runtimeconfig.json` requirements, the actual `StartupService` constants (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `TrackDot`).

**Gotchas the next session needs to know:**

1. **`docs/SMOKE_TEST.md` is a manual test plan, not automated.** The `§6` exit-criteria require real launches against real players; the only signal the doc was authored honestly is that every claim about behaviour cites the production component name (e.g. `MediaControllerService.OpenThumbnailAsManagedStreamAsync`, `WindowPlacementService`, `UnhandledExceptionLogger`). When in doubt, grep the source.

2. **The `PlaceholderArt.png` file is shipped but unused.** The csproj embeds it as a `<Resource>` and `AssetResourceTests.TrackDot_assembly_contains_PlaceholderArt_png_resource` confirms it lives in the `.g.resources` stream under `assets/placeholderart.png` (70 bytes, valid PNG magic). The XAML `<Image Source="{Binding Artwork}" />` does NOT reference it as a fallback when `Artwork` is null. Wiring it would be a one-line XAML change with a style trigger; deferred to a future pass.

3. **The framework-dependent artifact at `artifacts/win-x64-framework-dependent/TrackDot.exe` is the launch path for all smoke scenarios.** Running via `dotnet run` from the project root uses `dotnet.exe` as the host process, which breaks the launch-at-sign-in detection (`StartupService.IsEnabled` would write `dotnet.exe` into the Run key).

---

## Task 13 — Build, usage, limitations, and privacy docs (shipped in commit `742229b`)

Plan §Task 13 is fully implemented. Behaviour shipped:

1. **`README.md`** — 238 lines covering features, prerequisites, full build / test / run commands, tray controls reference, launch-at-sign-in registry detail, project layout, build pitfalls, privacy posture, known limitations, publishing, and a pointer to `docs/SMOKE_TEST.md`.
2. **All README commands executed verbatim from a clean checkout** during this session — `dotnet restore`, `dotnet build -c Debug`, `dotnet test -c Debug --no-build`, `dotnet build -c Release`, and the `./bin/x64/Release/net8.0-windows10.0.19041.0/TrackDot.exe` launch path. Every command produced the documented output (0 warnings / 0 errors / 227 passing / binary present).
3. **Privacy claim is verified, not aspirational.** Section "Privacy" was written after `grep -rE "(System\.Net\.Http|HttpClient|WebClient|TcpClient|WebRequest|SmtpClient|FtpWebRequest|nuget\.org|api\.|telemetry|analytics|tracking)" --include="*.cs" --include="*.csproj" .` returned zero matches in production code (the one hit was a comment about field-tracking, not networking). The only file written outside the application directory is `crash.log`. The only registry value written is `HKCU\...\Run\TrackDot`, opt-in.
4. **No placeholder screenshots.** Per plan §Task 13 step 6: "Add screenshots only after final UI validation; do not commit placeholder screenshots." The README has no images.
5. **Build pitfalls section consolidates every gotcha from the per-task handoffs into one place** — so a fresh developer does not need to read all 13 previous gotcha lists to avoid the same trap. The 8 items cover: the `Microsoft.Windows.SDK.Contracts` prohibition, `--no-build` stale-binary hazard, AsyncRelayCommand re-entrancy test pattern (`await Task.Run(() => sut.Execute(null))`), `BitmapDecoder` ambiguity, `Application.MainWindow` vs `TrackDot.MainWindow` type collision, `pack://application:,,,/Assets/AppIcon.ico` URI requirement, `HKCU\...\Run` path-quoting requirement, and the `Enable` opens-twice idempotency contract.

**Build & test:** No production code changes for Task 13. Test count unchanged at 227 / 227.

**Gotchas the next session needs to know:**

1. **The README documents the current behaviour, not an aspirational spec.** Every command, every path, every registry key, every test count matches what is actually shipped. If you change a behaviour, update the README in the same commit — do not let it drift.

2. **The "Launch at sign-in" section quotes the Run-key value path explicitly.** This is intentional — users (and reviewers with `regedit`) will check. The path is `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TrackDot = "<quoted full path>"`. The detection path (`IsEnabled`) accepts both quoted and unquoted stored values (`OrdinalIgnoreCase` + trailing-separator trim); the *stored* form is always quoted.

3. **The `Build pitfalls` section is a maintenance liability if not kept current.** New gotchas found during future work should be appended to the README's pitfalls list, not just the HANDOFF.md. The two documents serve different audiences: README = fresh developer; HANDOFF.md = next-session resumption.

---

## Task 14 — Produce and verify x64 distributable (shipped in commit `cd03c59`)

Plan §Task 14 is fully implemented. Behaviour shipped:

1. **Framework-dependent x64 artifact produced** at `artifacts/win-x64-framework-dependent/TrackDot.exe`. 25 MB total (the `Microsoft.Windows.SDK.NET.dll` is 24 MB — the WinRT projection is shipped as a side-by-side assembly, not embedded). Runtime config declares `Microsoft.NETCore.App 8.0.0` + `Microsoft.WindowsDesktop.App 8.0.0` — both required on the target machine.
2. **Single-instance mutex verified** — launched twice from the terminal; second instance exits with code 1 (Task 8 design). `Get-Process TrackDot | Measure` returns 1 after both launches.
3. **Clean process shutdown verified** — `Stop-Process -Name TrackDot` leaves zero processes (no zombies).
4. **Real defect discovered and fixed during verification.** `crash.log` at `%LocalAppData%\TrackDot\crash.log` had two identical `InvalidOperationException: A TwoWay or OneWayToSource binding cannot work on the read-only property 'PositionSeconds'` entries (from prior launches at 20:33 and 22:17). The ProgressBar `Value="{Binding PositionSeconds}"` and `Maximum="{Binding DurationSeconds}"` bindings default to `TwoWay` in WPF; `PositionSeconds` / `DurationSeconds` are get-only `double` properties on `MainViewModel`. The exception was caught by the dispatcher-unhandled-exception logger (Task 9), but the binding silently never produced values. **Fix:** added `Mode=OneWay` to both bindings. Verified by deleting `crash.log`, relaunching the published binary, waiting 5 s, and confirming the file was NOT recreated.

**Files modified:**
- `MainWindow.xaml` — `Maximum="{Binding DurationSeconds, Mode=OneWay}"` and `Value="{Binding PositionSeconds, Mode=OneWay}"`.

**Build & test:** Debug + Release both build with 0 warnings, 0 errors. `dotnet test` Debug and Release: **227 / 227 passing** — the fix is XAML-only, no test surface changes. Republished artifact at `artifacts/win-x64-framework-dependent/TrackDot.exe`.

**Gotchas the next session needs to know:**

1. **The `TwoWay`-binding-on-read-only-property trap will repeat for any new get-only VM property bound to a WPF `DependencyProperty` whose `TwoWay` is the default.** WPF defaults `TwoWay` for: `ProgressBar.Value`, `Slider.Value`, `RangeBase.Value`, `TextBox.Text`, `PasswordBox.Password`, `Selector.SelectedItem`/`SelectedIndex`/`SelectedValue`, `DatePicker.SelectedDate`, `RichTextBox`-derived properties, `ToggleButton.IsChecked`, `Expander.IsExpanded`, `TreeView.SelectedItem`, `TabControl.SelectedIndex`, `ListBox.SelectedIndex`/`SelectedItem`, and a long tail of input controls. The fix is always `Mode=OneWay` on the binding. Do not add a setter to the VM to work around this — that would let the UI write back into derived presentation state.

2. **The exception logger caught this bug, not the test suite.** The Task 11 test surface does NOT bind the popover's XAML — it tests the VM contracts and the service, not the binding wiring. This is the **third** finding of the "test count unchanged ≠ surface verified" pattern (after Task 10's `SettingsWindow` and Task 7's `MainWindow`). For any future XAML change, run the published binary and check `crash.log` is empty after a 5-second wait. Add it to the standard verification sequence in HANDOFF gotchas.

3. **The published artifact does NOT contain a `Assets/` directory at the top level.** Both `AppIcon.ico` and `PlaceholderArt.png` are embedded into `TrackDot.dll`'s `.g.resources` stream (the WPF resource manager looks them up via the `pack://application:,,,/Assets/...` URI). Do not "fix" the missing directory by adding `<None Include="Assets\AppIcon.ico" CopyToOutputDirectory="PreserveNewest" />` — that would duplicate the asset and double the disk footprint. The pack URI is the canonical WPF pattern; the disk copy was an old-WinForms-era convention.

4. **`Microsoft.Windows.SDK.NET.dll` is 24 MB and ships in the framework-dependent output.** This is the WinRT projection — it ships side-by-side with `TrackDot.dll` rather than being copied into `System32` or baked into the runtime. The framework-dependent artifact is therefore ~25 MB total on disk; a self-contained build would add the .NET runtime + WPF assemblies on top of that (probably 150 MB+). Plan §7 Decision point 4 calls for the framework-dependent artifact first; a self-contained follow-up is gated on measuring size + startup time against the framework-dependent baseline.

5. **The `crash.log` from this session is preserved in the repo's history** via `git log -p cd03c59` (the fix commit) — the commit message quotes both timestamps and explains the root cause. Future bug-hunters investigating dispatcher exceptions should start there.

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
- **`TwoWay`-binding-on-read-only-property trap (Task 14).** WPF defaults `Binding.Mode = TwoWay` for many input controls: `ProgressBar.Value`, `Slider.Value`, `RangeBase.Value`, `TextBox.Text`, `PasswordBox.Password`, `Selector.SelectedItem`/`SelectedIndex`/`SelectedValue`, `DatePicker.SelectedDate`, `RichTextBox` derived, `ToggleButton.IsChecked`, `Expander.IsExpanded`, `TreeView.SelectedItem`, `TabControl.SelectedIndex`, `ListBox.SelectedIndex`/`SelectedItem`. Binding any of those to a get-only VM property throws `InvalidOperationException` ("A TwoWay or OneWayToSource binding cannot work on the read-only property"). The dispatcher-exception logger catches it (no crash), but the binding silently never produces values. Fix: `Mode=OneWay` on the binding. Do NOT add a setter to the VM to work around — the UI would then write into derived presentation state.
- **The exception logger catches what the test suite misses (Task 14).** xUnit covers contracts, lifecycle, and disposal. It does NOT cover binding wiring against the real `MainWindow.xaml` (the test surface has no `Application` instance + UI thread). When the popover's ProgressBar bound to `PositionSeconds`/`DurationSeconds` threw `InvalidOperationException` on every startup, the xUnit suite remained 227 / 227 — the bug was only visible in `%LocalAppData%\TrackDot\crash.log`. Standard verification for any future XAML change: launch the published binary, wait 5 seconds, confirm `crash.log` is empty. This is the third "test count unchanged ≠ surface verified" finding (after Tasks 7 and 10). Consider adding a smoke-launch step to the standard verification sequence.
- **WPF resources are embedded in `TrackDot.g.resources`, not copied to disk.** The csproj `<Resource Include="Assets\AppIcon.ico" />` packs the asset into a single binary `.g.resources` stream under the project-relative path (lowercased, forward-slashed — e.g. `assets/appicon.ico`). The `pack://application:,,,/Assets/AppIcon.ico` URI in XAML resolves through that stream. The published artifact therefore does NOT contain an `Assets/` directory at the top level; the assets are inside `TrackDot.dll`. Do NOT "fix" this by adding `<None Include="...\AppIcon.ico" CopyToOutputDirectory="PreserveNewest" />` — that duplicates the asset and doubles the disk footprint. The pack URI is the canonical WPF pattern. To inspect the embedded assets programmatically, use `System.Resources.ResourceReader` on the `AssemblyName.g.resources` stream (this is what `AssetResourceTests.EnumerateWpfResourceEntries` does).
- **`Microsoft.Windows.SDK.NET.dll` is ~24 MB and ships in the framework-dependent output.** It is the WinRT projection; it ships side-by-side with `TrackDot.dll` rather than being baked into the runtime. The framework-dependent artifact is therefore ~25 MB total on disk; a self-contained build would add the .NET runtime + WPF assemblies on top of that (probably 150 MB+). Plan §7 Decision point 4 calls for the framework-dependent artifact first; a self-contained follow-up is gated on measuring size + startup time against the framework-dependent baseline.

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

Current `dotnet test` status: 227 / 227 passing (3 smoke + 13 snapshot + 20 mapper + 12 decoder + 16 command + 15 service-guards + 10 interpolation + 43 view-model + 6 single-instance + 8 tray-icon + 10 placement + 13 exception-logger + 24 startup). Stable across Debug and Release (full Debug + Release cycle in the Task 10 shipping session, zero flakes; re-verified end-to-end in the Task 12/13/14 shipping session including a published-binary launch).
Current `dotnet build` status: Debug and Release both build with 0 warnings, 0 errors.
Current `dotnet publish` status: `artifacts/win-x64-framework-dependent/TrackDot.exe` (25 MB, framework-dependent, runtimeconfig requires `Microsoft.WindowsDesktop.App 8.0.0`). Launched cleanly from a clean checkout; second instance exits with code 1.

---

## Decision points deferred for the next session

1. **Subagent-driven-development vs. main-thread execution.** The plan calls for one-subagent-per-task with spec + quality reviews. The previous two sessions stayed in the main thread because the per-task files were small and incremental. The next session should pick one approach and apply it consistently. My recommendation: stay in the main thread for code authoring; review the diff against the spec and the quality checklist yourself before each commit. Saves context-switching cost.

2. **`AllowsTransparency` vs. `WindowStyle=None` + rounded border.** Plan §1.2 deferred this until testing. The next session should ship `WindowStyle=None` with `WindowChrome` rounded corners (the conservative path) and document the rendering tradeoff in `docs/SMOKE_TEST.md` once Task 12 lands.

3. **Source auto-switch policy.** Plan §1.5 says "follow `GetCurrentSession()` for MVP, expose source identity so a future source picker can be added." Implemented literally `GetCurrentSession()` in Task 3, no auto-switching. Continue this convention in Task 5b (command guards) and forward — `MediaControllerService` is the only place that picks a session; everything downstream treats it as a single active source.

4. **First public distribution format.** Plan §7 recommends framework-dependent ZIP first, then MSIX if installation/startup-registration needs product-grade handling. For Task 14, ship a framework-dependent x64 artifact only; document the MSIX follow-up.

5. **ThumbnailDecoder input type — RESOLVED in Task 4.** Chose `Func<Task<Stream>>`. The `IRandomAccessStreamReference` lives behind a small adapter inside `MediaControllerService.OpenThumbnailAsManagedStreamAsync`. The decoder itself stays pure. Same pattern should apply to any future CsWinRT runtime-class input.