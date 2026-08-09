# TrackDot Implementation — Session Handoff

**Date:** 2026-08-09
**Session:** Resumed from plan `.hermes/plans/2026-08-09_000000-track-dot-windows-smtc-popover.md`
**Goal:** Implement the 14-task plan to turn the empty WPF template into a Windows SMTC tray popover.

Commit author: `Herlandro Tribiakto <herlandrotri@gmail.com>` (already configured in this repo).

---

## Status: Tasks 1, 2 & 3 complete, Tasks 4-14 pending

| # | Task | Status | Commit |
|---|------|--------|--------|
| 1 | Establish clean solution baseline (TFM, .gitignore, test project) | ✅ done | `13f85c1` |
| 2 | Define media state and transport contracts (Models, IMediaControllerService) | ✅ done | `f3f96aa` |
| 3 | Implement SMTC session discovery and event lifecycle | ✅ done | `9869f15` |
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

### Task 3 — SMTC session discovery and event lifecycle (commit `9869f15`)

**Files created:**
- `Services/MediaPropertyMapper.cs` — pure static class mapping SMTC enums and shape records into `MediaSessionSnapshot` / `PlaybackSnapshot` / `TransportCapabilities`. Consumes small data shapes (`SessionShape`, `MediaPropertiesShape`, `PlaybackInfoShape`, `ControlsShape`, `TimelineShape`) instead of WinRT runtime classes — the SMTC playback-controls class has no public constructor and read-only properties, so it cannot be substituted in tests.
- `Services/MediaControllerService.cs` — `IMediaControllerService` implementation. Owns the `GlobalSystemMediaTransportControlsSessionManager`, wires the three property-grouped event subscriptions on each session, centralises session replacement behind a generation counter, marshals every WinRT callback through the captured `SynchronizationContext` before publishing, and exposes command methods (`TogglePlayPauseAsync` / `PreviousAsync` / `StopAsync` / `NextAsync`) that forward to the active session.
- `tests/TrackDot.Tests/MediaPropertyMapperTests.cs` — 20 tests covering all six SMTC playback statuses, capability flag combinations, every mapper input null-case, and the timeline-baseline fallback rules.

**Total tests:** 36 (3 smoke + 13 snapshot + 20 mapper). All pass.

**Gotchas the next session needs to know:**

1. **WinRT runtime classes cannot be `new`'d in tests.** `GlobalSystemMediaTransportControlsSessionPlaybackControls` has no public constructor and read-only properties; the same applies to media-properties and timeline-properties classes. The mapper therefore consumes record shapes, and the service projects SMTC objects into those shapes. **Do not change the mapper's input types to the runtime classes** — the test project cannot supply substitutes.

2. **SMTC type names are exact.** The timeline class is `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionTimelineProperties` (with `Session` in the middle). The last-updated field on the timeline is `LastUpdatedTime`, not `LastUpdated`. The control methods `TryPlayAsync` / `TryPauseAsync` / `TrySkipPreviousAsync` / `TryStopAsync` / `TrySkipNextAsync` return `Windows.Foundation.IAsyncOperation<bool>`, not `<int>`. If a compile error names any of these, check the spelling before assuming a missing API.

3. **`IAsyncOperation<T>` requires `using Windows.Foundation;`** in any file that returns or awaits one. The CS052 error otherwise is "`IAsyncOperation<T>` not found" — easy to misread as a missing package.

4. **`TryGetMediaPropertiesAsync()` returns `IAsyncOperation<MediaProperties?>`** — the result may be null (the source app has not populated metadata yet). Always null-check before reading `Title` / `Artist` / `AlbumTitle` / `Thumbnail`.

5. **Synchronous SMTC reads (`GetPlaybackInfo()`, `GetTimelineProperties()`)** do not need to be `async Task` — they execute inline on the marshaled UI thread. Marking them `async Task` without an `await` triggers CS1998.

6. **The service uses `Volatile.Read` / `Volatile.Write` on `_currentSnapshot` and `_generation`** so the dispatcher-thread publish path and the worker-thread generation check stay coherent. **Do not remove these** — the handoff's "stale async result" hazard is real and the generation check only works if the reads are volatile.

7. **The artwork decode in `DecodeArtworkAsync` is currently a stub** returning `Task<ImageSource?>(null)`. Task 4 will replace it with the real `ThumbnailDecoder` pipeline. The signature is already correct (`Task<ImageSource?>`) so Task 4 can plug in directly.

8. **Lifecycle tests for the service itself are deferred to Task 11** (per the handoff plan). `InternalsVisibleTo TrackDot.Tests` is already wired in `TrackDot.csproj`, so the service can be exercised directly from tests when Task 11 lands.

---

## Next: Task 4 — Decode album artwork safely

**Plan said (verbatim):**
> Implement `ThumbnailDecoder` using `RandomAccessStreamReference.OpenReadAsync()` → `BitmapDecoder` → `SoftwareBitmap` → `BitmapSource` (`WriteableBitmap` if `SoftwareBitmap` direct conversion is blocked). Decode off the UI thread, clamp to a max pixel size (256x256), freeze the result, dispose intermediate buffers, and return `null` (not throw) on missing/unsupported input.

**Files to create:**
- `Services/ThumbnailDecoder.cs` — the artwork decode pipeline.
- `tests/TrackDot.Tests/ThumbnailDecoderTests.cs` — tests for the decoder.

**Concrete steps the next session should follow:**

1. **Verify the artwork pipeline API surface against the installed SDK ref.** The relevant types are:
   - `Windows.Storage.Streams.IRandomAccessStreamReference` (interface, no constructor).
   - `Windows.Storage.Streams.RandomAccessStreamReference` (factory class — `CreateFromFile`, `CreateFromUri`, etc.).
   - `Windows.Graphics.Imaging.BitmapDecoder` (static `CreateAsync` overloads).
   - `Windows.Graphics.Imaging.SoftwareBitmap` (has `BitmapPixelFormat`, `BitmapAlphaMode`, `SoftwareBitmap.CopyTo(WriteableBitmap)` etc.).
   - `Windows.UI.Xaml.Media.Imaging.WriteableBitmap` — note this is the UWP type, not `System.Windows.Media.Imaging.WriteableBitmap`. WPF binding works because both are `ImageSource`-compatible at the WPF layer, but the type lives in `Windows.UI.Xaml.Media.Imaging`.

   Verify exact member names by grepping `C:/Users/Herlandro Ando/.nuget/packages/microsoft.windows.sdk.net.ref/10.0.19041.31/lib/net6.0/Microsoft.Windows.SDK.NET.xml` for `BitmapDecoder`, `SoftwareBitmap`, `RandomAccessStreamReference`. The same naming pitfall as Task 3 (typos, runtime-class vs record) applies.

2. **Draft tests first (RED).** The plan called for "ThumbnailDecoder tests for null input → null output, oversized input → clamped output, valid input → frozen `ImageSource`." The decoder should be a pure method so the tests run without a live SMTC session.

   Note: the input to the decoder is an `IRandomAccessStreamReference` runtime class — **same testability problem as Task 3's playback controls**. Either:
   - (a) Take `IRandomAccessStreamReference` directly and have tests supply a fake via reflection on `IPropertyValue`-backed streams (heavyweight).
   - (b) Take a `Func<Task<Stream>>` that opens the thumbnail, with tests providing a `MemoryStream` lambda. Cleaner — pick this unless the plan explicitly requires the runtime class.

3. **Implement the decoder.**
   - Signature: `public static async Task<ImageSource?> DecodeAsync(Func<Task<Stream>> openStream, CancellationToken ct = default)`.
   - Open the stream, create `BitmapDecoder.CreateAsync(stream.AsRandomAccessStream())`.
   - Get `SoftwareBitmap` via `decoder.GetSoftwareBitmapAsync()` or `decoder.GetPixelDataAsync()` + `SoftwareBitmap.CreateCopyFromBuffer(...)`.
   - If width or height exceeds 256, scale by setting `decoder.Scale` or applying `BitmapTransform.ScaledWidth/ScaledHeight`.
   - Convert `SoftwareBitmap` to a WPF `BitmapSource`: `SoftwareBitmap.CopyTo(WriteableBitmap)` works on Windows 10+, OR use `new WriteableBitmap(softwareBitmap.PixelWidth, softwareBitmap.PixelHeight, 96, 96, PixelFormats.Bgra32, null)` + `softwareBitmap.CopyTo(writeableBitmap)`. Verify the working form during build.
   - **Freeze the result** (`bitmap.Freeze()`) before returning — the UI thread owns all `ImageSource` instances and a frozen one is thread-safe to assign from any thread.
   - **Wrap every step in try/catch** — malformed thumbnails should produce `null`, never throw. This is the contract the mapper already assumes.

4. **Wire into `MediaControllerService`.** Replace the stub:
   ```csharp
   private static Task<ImageSource?> DecodeArtworkAsync(object? thumbnail)
       => Task.FromResult<ImageSource?>(null);
   ```
   with a real call. The `IRandomAccessStreamReference` parameter from `MediaProperties.Thumbnail` becomes:
   ```csharp
   var artwork = thumbnail is null
       ? null
       : await ThumbnailDecoder.DecodeAsync(
           () => ((IRandomAccessStreamReference)thumbnail).OpenReadAsync().AsTask().ContinueWith(t => t.Result.AsStreamForRead()),
           ct).ConfigureAwait(true);
   ```
   Wrap in `try/catch` and return `null` on any failure (the service already swallows decoder errors).

5. **Build + test:** `dotnet build TrackDot.sln -c Debug --no-restore` then `dotnet test TrackDot.sln -c Debug --no-build --filter ThumbnailDecoderTests`. Both must succeed. Then run the full suite — should still be 36 + new decoder tests green.

**Commit message:** `feat: decode album artwork safely`

---

## Pitfalls to remember

- **WinRT callbacks arrive on arbitrary threads.** The WPF UI thread binding will throw if you update `INotifyPropertyChanged` properties from a non-dispatcher thread. Always marshal through `SynchronizationContext` or `Dispatcher`.
- **Async work crossing session switches.** If a user starts playing Track A, the manager briefly switches to Track B mid-`TryGetMediaPropertiesAsync`, the old completion arrives with stale data. The generation counter is the only thing standing between you and the wrong track displayed. Check it before every publish.
- **Empty state on no session.** When `GetCurrentSession()` returns null, publish `MediaSessionSnapshot.Empty` immediately (don't just leave `Current` as the default initialization value forever).
- **Don't catch all exceptions.** SMTC may throw `COMException` with `HResult 0x800704C7` (no session) on the first read. That is normal and should be treated as "publish Empty", not log spam. Genuine exceptions should still be logged in debug builds.
- **Marshalling `ImageSource` is dangerous.** The WPF UI thread owns all `ImageSource` instances. Decode happens in Task 4 (`ThumbnailDecoder`) and the result is `Freeze()`'d before publishing — frozen `BitmapSource` is thread-safe.
- **WinRT runtime classes have no public constructors.** Every mapper/decoder that wants to be testable must accept a record / delegate / stream rather than the runtime class. This pattern is established in Task 3 and applies again in Task 4.
- **The `_context.Post` callback may be dropped** if the dispatcher is shutting down. Treat dropped callbacks as "silently no-op" rather than retrying — the service is being torn down anyway.

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
│   └── MediaPropertyMapper.cs
├── TrackDot.csproj
├── TrackDot.sln
├── TrackDot.csproj.user    (untouched)
└── tests/TrackDot.Tests/
    ├── MediaPropertyMapperTests.cs
    ├── MediaSessionSnapshotTests.cs
    ├── SmokeTests.cs
    └── TrackDot.Tests.csproj
```

Need to be created (planned, do not yet exist):
- `Models/` complete (no more needed)
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

Current `dotnet test` status: 36 / 36 passing (3 smoke + 13 snapshot + 20 mapper).
Current `dotnet build` status: Debug and Release both build with 0 warnings, 0 errors.

---

## Decision points deferred for the next session

1. **Subagent-driven-development vs. main-thread execution.** The plan calls for one-subagent-per-task with spec + quality reviews. The previous two sessions stayed in the main thread because the per-task files were small and incremental. The next session should pick one approach and apply it consistently. My recommendation: stay in the main thread for code authoring; review the diff against the spec and the quality checklist yourself before each commit. Saves context-switching cost.

2. **`AllowsTransparency` vs. `WindowStyle=None` + rounded border.** Plan §1.2 deferred this until testing. The next session should ship `WindowStyle=None` with `WindowChrome` rounded corners (the conservative path) and document the rendering tradeoff in `docs/SMOKE_TEST.md` once Task 12 lands.

3. **Source auto-switch policy.** Plan §1.5 says "follow `GetCurrentSession()` for MVP, expose source identity so a future source picker can be added." Implemented literally `GetCurrentSession()` in Task 3, no auto-switching. Continue this convention in Task 4 (decoder) and Task 5 (commands).

4. **First public distribution format.** Plan §7 recommends framework-dependent ZIP first, then MSIX if installation/startup-registration needs product-grade handling. For Task 14, ship a framework-dependent x64 artifact only; document the MSIX follow-up.

5. **ThumbnailDecoder input type.** `IRandomAccessStreamReference` (runtime class, untestable) vs. `Func<Task<Stream>>` (testable, requires a small adapter in the service). Recommendation: `Func<Task<Stream>>` — matches the testability convention established in Task 3 and keeps the decoder itself pure.
