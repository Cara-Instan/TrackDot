using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using TrackDot.Commands;
using TrackDot.Models;
using TrackDot.Services;
using TrackDot.Tests.Fakes;
using TrackDot.ViewModels;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Tests for <see cref="MainViewModel"/>.
///
/// <para>
/// The view-model is the presentation layer: it subscribes to
/// <see cref="IMediaControllerService.SnapshotChanged"/>, mirrors the
/// snapshot into bindable properties, owns four
/// <see cref="AsyncRelayCommand"/> instances, and drives
/// <see cref="ProgressInterpolator"/> while the popover is visible
/// and playback is <see cref="MediaPlaybackState.Playing"/>.
/// </para>
/// </summary>
public sealed class MainViewModelTests
{
    // -----------------------------------------------------------------------
    // No session — empty snapshot
    // -----------------------------------------------------------------------

    [Fact]
    public void No_session_renders_neutral_text_and_disables_commands()
    {
        var (vm, svc, _, _) = BuildViewModel();

        Assert.Equal("Nothing playing", vm.Title);
        Assert.Equal(string.Empty, vm.Artist);
        Assert.Equal(string.Empty, vm.AlbumTitle);
        Assert.Null(vm.Artwork);
        Assert.Null(vm.SourceAppUserModelId);
        Assert.False(vm.IsPlaying);
        Assert.False(vm.HasMedia);

        // With no session, capabilities default to None so every
        // command is disabled. The view-model's canExecute
        // delegates must mirror the service gate: the toggle uses
        // CanPlay || CanPause (per gotcha #4 of Task 5b), the rest
        // use their own flag.
        Assert.False(vm.TogglePlayPauseCommand.CanExecute(null));
        Assert.False(vm.PreviousCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));
        Assert.False(vm.NextCommand.CanExecute(null));

        // Position is zero when no session is active.
        Assert.Equal(0d, vm.PositionSeconds);
        Assert.Equal(0d, vm.DurationSeconds);
    }

    [Fact]
    public void No_session_uses_zero_time_strings()
    {
        var (vm, _, _, _) = BuildViewModel();
        Assert.Equal("0:00", vm.ElapsedTimeText);
        Assert.Equal("0:00", vm.DurationTimeText);
    }

    // -----------------------------------------------------------------------
    // Playing — full session metadata
    // -----------------------------------------------------------------------

    [Fact]
    public void Playing_snapshot_publishes_all_properties_and_enables_toggle()
    {
        var (vm, svc, _, _) = BuildViewModel();
        // A typical "now playing" snapshot: pause is supported
        // (because we're playing), stop is supported, but the
        // source has no previous-track history (e.g. a radio
        // stream) so CanGoPrevious is false. The toggle is
        // enabled because CanPause is true.
        var playing = MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(12),
            endTime: TimeSpan.FromMinutes(3),
            caps: new TransportCapabilities(
                CanPlay: false,  // not play-capable while playing
                CanPause: true,
                CanStop: true,
                CanGoPrevious: false,
                CanGoNext: true));

        svc.Publish(playing);

        Assert.Equal("Track Title", vm.Title);
        Assert.Equal("Artist Name", vm.Artist);
        Assert.Equal("Album Name", vm.AlbumTitle);
        Assert.True(vm.IsPlaying);
        Assert.True(vm.HasMedia);
        Assert.True(vm.TogglePlayPauseCommand.CanExecute(null));
        Assert.False(vm.PreviousCommand.CanExecute(null));   // not supported by the source
        Assert.True(vm.StopCommand.CanExecute(null));
        Assert.True(vm.NextCommand.CanExecute(null));
    }

    [Fact]
    public void Paused_snapshot_disables_toggle_when_neither_play_nor_pause_supported()
    {
        var (vm, svc, _, _) = BuildViewModel();
        var paused = MakeSnapshot(
            state: MediaPlaybackState.Paused,
            position: TimeSpan.FromSeconds(20),
            endTime: TimeSpan.FromMinutes(3),
            caps: TransportCapabilities.None);  // no transport at all

        svc.Publish(paused);

        Assert.False(vm.IsPlaying);
        Assert.True(vm.HasMedia);
        // CanPlay=false AND CanPause=false => toggle disabled.
        // The view-model must mirror the service gate exactly.
        Assert.False(vm.TogglePlayPauseCommand.CanExecute(null));
    }

    // -----------------------------------------------------------------------
    // TogglePlayPause enable uses CanPlay || CanPause
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(true, false, true)]   // CanPlay only  -> toggle enabled
    [InlineData(false, true, true)]   // CanPause only -> toggle enabled
    [InlineData(true, true, true)]    // both         -> enabled
    [InlineData(false, false, false)] // neither      -> disabled
    public void TogglePlayPause_enable_mirrors_CanPlay_or_CanPause(
        bool canPlay, bool canPause, bool expected)
    {
        var (vm, svc, _, _) = BuildViewModel();
        var caps = new TransportCapabilities(
            CanPlay: canPlay, CanPause: canPause,
            CanStop: false, CanGoPrevious: false, CanGoNext: false);
        var snap = MakeSnapshot(MediaPlaybackState.Playing, TimeSpan.Zero, TimeSpan.FromMinutes(1), caps);

        svc.Publish(snap);

        Assert.Equal(expected, vm.TogglePlayPauseCommand.CanExecute(null));
    }

    // -----------------------------------------------------------------------
    // Missing title / artist
    // -----------------------------------------------------------------------

    [Fact]
    public void Snapshot_with_empty_title_renders_neutral_text()
    {
        var (vm, svc, _, _) = BuildViewModel();
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(5),
            endTime: TimeSpan.FromMinutes(2),
            title: string.Empty,
            artist: string.Empty,
            caps: AllEnabled()));

        // The mapper already maps empty title/artist to the
        // neutral "Nothing playing" / "—" text, so the view-model
        // just surfaces what the snapshot says. Verify it does not
        // null-coalesce away to something else.
        Assert.Equal("Nothing playing", vm.Title);
    }

    // -----------------------------------------------------------------------
    // Zero / unknown duration
    // -----------------------------------------------------------------------

    [Fact]
    public void Zero_duration_renders_zero_text_without_exploding()
    {
        var (vm, svc, _, _) = BuildViewModel();
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.Zero,
            endTime: TimeSpan.Zero,
            caps: AllEnabled()));

        Assert.Equal(0d, vm.DurationSeconds);
        Assert.Equal(0d, vm.PositionSeconds);
        Assert.Equal("0:00", vm.DurationTimeText);
        Assert.Equal("0:00", vm.ElapsedTimeText);
    }

    [Fact]
    public void Position_exceeding_endTime_clamps_in_view_model()
    {
        // Even though ProgressInterpolator already clamps, the
        // view-model applies its own clamp on the snapshot path too
        // (defence in depth) so the bound slider never overflows.
        var (vm, svc, _, _) = BuildViewModel();
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Paused,
            position: TimeSpan.FromSeconds(120),  // past end
            endTime: TimeSpan.FromMinutes(1),
            caps: AllEnabled()));

        Assert.Equal(60d, vm.PositionSeconds);
        Assert.Equal(60d, vm.DurationSeconds);
    }

    // -----------------------------------------------------------------------
    // Time text formatting
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0,   "0:00")]
    [InlineData(5,   "0:05")]
    [InlineData(59,  "0:59")]
    [InlineData(60,  "1:00")]
    [InlineData(125, "2:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3661, "1:01:01")]
    public void Time_text_formatting(double seconds, string expected)
    {
        var (vm, _, _, _) = BuildViewModel();
        Assert.Equal(expected, MainViewModel.FormatTime(TimeSpan.FromSeconds(seconds)));
    }

    // -----------------------------------------------------------------------
    // PropertyChanged is raised for each updated property
    // -----------------------------------------------------------------------

    [Fact]
    public void Snapshot_publish_raises_PropertyChanged_for_changed_properties()
    {
        var (vm, svc, _, _) = BuildViewModel();

        var changes = new List<string>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? string.Empty);

        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(10),
            endTime: TimeSpan.FromMinutes(3),
            caps: AllEnabled()));

        // The view-model must notify for every property the
        // snapshot affected. The exact list is the contract; if
        // you add a property to the VM, add the corresponding
        // notification here too.
        Assert.Contains(nameof(MainViewModel.Title), changes);
        Assert.Contains(nameof(MainViewModel.Artist), changes);
        Assert.Contains(nameof(MainViewModel.AlbumTitle), changes);
        Assert.Contains(nameof(MainViewModel.IsPlaying), changes);
        Assert.Contains(nameof(MainViewModel.HasMedia), changes);
        Assert.Contains(nameof(MainViewModel.PositionSeconds), changes);
        Assert.Contains(nameof(MainViewModel.DurationSeconds), changes);
        Assert.Contains(nameof(MainViewModel.ElapsedTimeText), changes);
        Assert.Contains(nameof(MainViewModel.DurationTimeText), changes);
        Assert.Contains(nameof(MainViewModel.SourceAppUserModelId), changes);
    }

    [Fact]
    public void CanExecuteChanged_fires_for_each_command_on_snapshot_change()
    {
        var (vm, svc, _, _) = BuildViewModel();

        var toggleFires = 0;
        var nextFires = 0;
        vm.TogglePlayPauseCommand.CanExecuteChanged += (_, _) => toggleFires++;
        vm.NextCommand.CanExecuteChanged += (_, _) => nextFires++;

        // First publish: capabilities go None -> all enabled.
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.Zero,
            endTime: TimeSpan.FromMinutes(1),
            caps: AllEnabled()));

        Assert.True(toggleFires > 0, "TogglePlayPauseCommand must raise CanExecuteChanged when capabilities change.");
        Assert.True(nextFires > 0, "NextCommand must raise CanExecuteChanged when capabilities change.");
    }

    // -----------------------------------------------------------------------
    // Commands forward to the service
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Executing_TogglePlayPauseCommand_invokes_service_method()
    {
        var (vm, svc, _, _) = BuildViewModel();
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.Zero,
            endTime: TimeSpan.FromMinutes(1),
            caps: AllEnabled()));

        // Per the Task 5c gotcha, wrap the Execute call in
        // Task.Run to escape xUnit's captured sync context.
        await Task.Run(() => vm.TogglePlayPauseCommand.Execute(null));

        Assert.Equal(1, svc.TogglePlayPauseCallCount);
    }

    [Theory]
    [InlineData("Previous", "PreviousCallCount")]
    [InlineData("Stop", "StopCallCount")]
    [InlineData("Next", "NextCallCount")]
    public async Task Executing_transport_command_invokes_service_method(string commandName, string counterName)
    {
        var (vm, svc, _, _) = BuildViewModel();
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.Zero,
            endTime: TimeSpan.FromMinutes(1),
            caps: AllEnabled()));

        var command = commandName switch
        {
            "Previous" => (System.Windows.Input.ICommand)vm.PreviousCommand,
            "Stop"     => vm.StopCommand,
            "Next"     => vm.NextCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(commandName)),
        };

        await Task.Run(() => command.Execute(null));

        var count = counterName switch
        {
            "PreviousCallCount" => svc.PreviousCallCount,
            "StopCallCount"     => svc.StopCallCount,
            "NextCallCount"     => svc.NextCallCount,
            _ => throw new ArgumentOutOfRangeException(nameof(counterName)),
        };
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Command_exception_is_swallowed_and_does_not_propagate()
    {
        // The view-model layer is the third (and last) swallow
        // point: the service guards (Task 5b) and the command
        // itself (Task 5a) both swallow; the view-model just
        // forwards and trusts them. Verify the forwards still
        // complete normally even when the service throws.
        var (vm, svc, _, _) = BuildViewModel();
        svc.ThrowOnCommand = true;
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.Zero,
            endTime: TimeSpan.FromMinutes(1),
            caps: AllEnabled()));

        await Task.Run(() => vm.TogglePlayPauseCommand.Execute(null));
        // No exception observed by the test = contract held.
    }

    // -----------------------------------------------------------------------
    // Disposal — unsubscribes from the service
    // -----------------------------------------------------------------------

    [Fact]
    public void Dispose_unsubscribes_from_service()
    {
        var (vm, svc, _, _) = BuildViewModel();
        vm.Dispose();

        // After Dispose, publishing another snapshot must not raise
        // PropertyChanged on the view-model. (If the handler is
        // still wired, it would update and the test could detect
        // that by checking HasMedia.)
        var changes = 0;
        vm.PropertyChanged += (_, _) => changes++;

        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(5),
            endTime: TimeSpan.FromMinutes(1),
            caps: AllEnabled()));

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var (vm, _, _, _) = BuildViewModel();
        vm.Dispose();
        vm.Dispose();  // must not throw
    }

    // -----------------------------------------------------------------------
    // Timer behavior — driven by the IUiTicker seam
    // -----------------------------------------------------------------------

    [Fact]
    public void Timer_starts_when_visible_and_playing()
    {
        var (vm, svc, ticker, _) = BuildViewModel();
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.Zero,
            endTime: TimeSpan.FromMinutes(1),
            caps: AllEnabled()));

        // Popover is closed: timer must NOT have started yet
        // (the plan step 5: "only while the popover is visible
        // and playback is Playing").
        Assert.Equal(0, ticker.StartCallCount);

        vm.IsVisible = true;

        Assert.True(ticker.IsRunning,
            "Timer must start when the popover becomes visible and playback is Playing.");
        Assert.Equal(1, ticker.StartCallCount);
    }

    [Fact]
    public void Timer_does_not_start_when_visible_but_paused()
    {
        var (vm, svc, ticker, _) = BuildViewModel();
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Paused,
            position: TimeSpan.FromSeconds(10),
            endTime: TimeSpan.FromMinutes(2),
            caps: AllEnabled()));

        vm.IsVisible = true;

        Assert.False(ticker.IsRunning,
            "Timer must NOT start when paused, even if the popover is visible.");
    }

    [Fact]
    public void Timer_does_not_start_when_playing_but_hidden()
    {
        var (vm, svc, ticker, _) = BuildViewModel();
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.Zero,
            endTime: TimeSpan.FromMinutes(1),
            caps: AllEnabled()));

        // IsVisible defaults to false.
        Assert.False(ticker.IsRunning,
            "Timer must NOT start when the popover is hidden, even if playback is Playing.");
    }

    [Fact]
    public void Timer_stops_when_visibility_flips_to_false()
    {
        var (vm, svc, ticker, _) = BuildViewModel();
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.Zero,
            endTime: TimeSpan.FromMinutes(1),
            caps: AllEnabled()));
        vm.IsVisible = true;
        Assert.True(ticker.IsRunning);

        vm.IsVisible = false;

        Assert.False(ticker.IsRunning,
            "Timer must stop when the popover hides, even if playback is still Playing.");
    }

    [Fact]
    public void Timer_stops_when_a_paused_snapshot_arrives()
    {
        var (vm, svc, ticker, _) = BuildViewModel();
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.Zero,
            endTime: TimeSpan.FromMinutes(1),
            caps: AllEnabled()));
        vm.IsVisible = true;
        Assert.True(ticker.IsRunning);

        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Paused,
            position: TimeSpan.FromSeconds(10),
            endTime: TimeSpan.FromMinutes(2),
            caps: AllEnabled()));

        Assert.False(ticker.IsRunning,
            "Timer must stop when the source app pauses, even if the popover is still visible.");
    }

    [Fact]
    public void Timer_advances_positionSeconds_on_tick()
    {
        var (vm, svc, ticker, clock) = BuildViewModel();
        // BuildViewModel starts the clock at T=100s. Publish a
        // snapshot whose TimelineUpdatedAt matches the clock so
        // the baseline interpolation gives exactly the snapshot
        // position before any ticks fire.
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(5),
            endTime: TimeSpan.FromMinutes(2),
            caps: AllEnabled(),
            timelineUpdatedAt: clock.Now - DateTimeOffset.UnixEpoch));
        vm.IsVisible = true;
        Assert.True(ticker.IsRunning);

        // Before any tick: position equals the snapshot's 5s.
        Assert.Equal(5d, vm.PositionSeconds);

        // 3 seconds of real time pass (driven by the fake clock).
        clock.Advance(TimeSpan.FromSeconds(3));
        ticker.Fire();

        // The view-model uses the same clock we advanced, so the
        // interpolated position should be 5 + 3 = 8s.
        Assert.Equal(8d, vm.PositionSeconds);
    }

    [Fact]
    public void Timer_tick_does_not_advance_when_paused_midflight()
    {
        // Defensive: the view-model re-evaluates IsPlaying on
        // every tick, so a snapshot arriving between ticks that
        // pauses the source must stop the timer even if a tick
        // is already queued. The fake ticker's Fire() always
        // invokes the captured callback, so we model this by
        // publishing a Paused snapshot between two ticker
        // invocations: the second invocation must see the timer
        // stopped by the snapshot and not advance.
        var (vm, svc, ticker, clock) = BuildViewModel();
        clock.Advance(TimeSpan.Zero);
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(5),
            endTime: TimeSpan.FromMinutes(2),
            caps: AllEnabled(),
            timelineUpdatedAt: clock.Now - DateTimeOffset.UnixEpoch));
        vm.IsVisible = true;

        clock.Advance(TimeSpan.FromSeconds(2));
        ticker.Fire();

        // Source pauses mid-flight.
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Paused,
            position: TimeSpan.FromSeconds(5),  // unchanged
            endTime: TimeSpan.FromMinutes(2),
            caps: AllEnabled()));
        Assert.False(ticker.IsRunning,
            "Timer must stop when the source pauses.");

        // 10 more seconds of real time pass; if the timer were
        // still running the position would jump by 10s. It must
        // stay at the snapshot's 5s.
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(5d, vm.PositionSeconds);
    }

    [Fact]
    public void Timer_restarts_from_new_baseline_when_a_new_snapshot_arrives()
    {
        // Per plan §1.4: "Restart from each authoritative timeline
        // snapshot." A seek or track change emits a new snapshot
        // with a fresh Position and TimelineUpdatedAt. The timer
        // should pick up the new baseline immediately on the
        // next tick.
        var (vm, svc, ticker, clock) = BuildViewModel();
        clock.Advance(TimeSpan.FromSeconds(100));
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(50),
            endTime: TimeSpan.FromMinutes(5),
            caps: AllEnabled(),
            title: "Track 1",
            timelineUpdatedAt: clock.Now - DateTimeOffset.UnixEpoch));
        vm.IsVisible = true;

        clock.Advance(TimeSpan.FromSeconds(1));
        ticker.Fire();
        Assert.Equal(51d, vm.PositionSeconds);

        // User seeks back to 10s; new authoritative snapshot
        // arrives with a fresh TimelineUpdatedAt.
        clock.Advance(TimeSpan.FromSeconds(1));
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(10),
            endTime: TimeSpan.FromMinutes(5),
            caps: AllEnabled(),
            title: "Track 1",
            timelineUpdatedAt: clock.Now - DateTimeOffset.UnixEpoch));

        clock.Advance(TimeSpan.FromSeconds(2));
        ticker.Fire();
        Assert.Equal(12d, vm.PositionSeconds);
    }

    // -----------------------------------------------------------------------
    // Snapshot-to-property mapping coverage
    // -----------------------------------------------------------------------

    [Fact]
    public void SourceAppUserModelId_is_published_unchanged()
    {
        var (vm, svc, _, _) = BuildViewModel();
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.Zero,
            endTime: TimeSpan.FromMinutes(1),
            caps: AllEnabled(),
            sourceAumid: "Spotify.exe"));

        Assert.Equal("Spotify.exe", vm.SourceAppUserModelId);
    }

    [Fact]
    public void Null_aumid_remains_null()
    {
        var (vm, svc, _, _) = BuildViewModel();
        svc.Publish(MakeSnapshot(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.Zero,
            endTime: TimeSpan.FromMinutes(1),
            caps: AllEnabled(),
            sourceAumid: null));

        Assert.Null(vm.SourceAppUserModelId);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (
        MainViewModel Vm,
        FakeMediaControllerService Svc,
        FakeTicker Ticker,
        FakeClock Clock)
        BuildViewModel()
    {
        var svc = new FakeMediaControllerService();
        var ticker = new FakeTicker();
        var clock = new FakeClock(TimeSpan.FromSeconds(100));
        var vm = new MainViewModel(svc, ticker, () => clock.Now);
        // Visible=false by default — the popover isn't open.
        // Tests that need interpolation must flip it.
        return (vm, svc, ticker, clock);
    }

    private static TransportCapabilities AllEnabled() => new(
        CanPlay: true, CanPause: true, CanStop: true,
        CanGoPrevious: true, CanGoNext: true);

    private static MediaSessionSnapshot MakeSnapshot(
        MediaPlaybackState state,
        TimeSpan position,
        TimeSpan endTime,
        TransportCapabilities caps,
        string title = "Track Title",
        string artist = "Artist Name",
        string album = "Album Name",
        string? sourceAumid = "TestApp.exe",
        TimeSpan? timelineUpdatedAt = null)
    {
        // The view-model interpolates from
        // (position, TimelineUpdatedAt) to (clock.Now, ?). If the
        // caller does not supply a baseline timestamp, default it
        // to position (matches the production mapper, which
        // records TimelineUpdatedAt as the wall-clock time of the
        // snapshot read). Timer-driven tests override this so the
        // baseline matches the fake clock's reading.
        var at = timelineUpdatedAt ?? position;
        return new MediaSessionSnapshot(
            SourceAppUserModelId: sourceAumid,
            Title: title,
            Artist: artist,
            AlbumTitle: album,
            Artwork: null,
            Playback: new PlaybackSnapshot(
                State: state,
                Position: position,
                StartTime: TimeSpan.Zero,
                EndTime: endTime,
                TimelineUpdatedAt: DateTimeOffset.UnixEpoch + at,
                Capabilities: caps));
    }

    private sealed class FakeClock
    {
        private TimeSpan _now;
        public FakeClock(TimeSpan initial) { _now = initial; }
        public DateTimeOffset Now => DateTimeOffset.UnixEpoch + _now;
        public void Advance(TimeSpan delta) { _now += delta; }
    }

    /// <summary>
    /// Captures the tick callback instead of using a real
    /// <c>DispatcherTimer</c>. Tests call <see cref="Fire"/> to
    /// simulate a 250 ms elapsed tick.
    /// </summary>
    private sealed class FakeTicker : IUiTicker
    {
        public Action? Callback { get; private set; }
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public bool IsRunning => Callback is not null;

        public void Start(Action onTick)
        {
            Callback = onTick;
            StartCallCount++;
        }

        public void Stop()
        {
            Callback = null;
            StopCallCount++;
        }

        public void Fire()
        {
            // Take a snapshot in case the callback resets the ticker.
            var cb = Callback;
            cb?.Invoke();
        }
    }
}
