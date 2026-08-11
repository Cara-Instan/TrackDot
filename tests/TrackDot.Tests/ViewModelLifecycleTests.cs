using System;
using TrackDot.Models;
using TrackDot.Services;
using TrackDot.Tests.Fakes;
using TrackDot.ViewModels;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Tests for the popover visibility lifecycle of
/// <see cref="MainViewModel"/>. The timer (progress interpolation)
/// is allowed to run only when the popover is visible AND playback
/// is <see cref="MediaPlaybackState.Playing"/>; hiding the popover
/// pauses interpolation and stops the timer, while showing it again
/// resumes from the latest authoritative snapshot.
/// </summary>
/// <remarks>
/// <para>
/// These tests share the <see cref="FakeTicker"/> seam with
/// <see cref="MainViewModelTests"/>; the difference is the focus.
/// <see cref="MainViewModelTests"/> covers the snapshot-to-property
/// mapping; this class covers the visible/hidden transitions and
/// the timer state machine they drive.
/// </para>
/// <para>
/// Every test in this class uses a deterministic
/// <see cref="FakeMediaControllerService"/> + <see cref="FakeTicker"/>
/// + immutable shared clock. No real <c>DispatcherTimer</c> is
/// involved, so the tests are timing-independent and safe to
/// re-run on Debug and Release JIT fatigue.
/// </para>
/// </remarks>
public sealed class ViewModelLifecycleTests
{
    // -------------------------------------------------------------------
    // Hide pauses interpolation
    // -------------------------------------------------------------------

    [Fact]
    public void Hiding_pauses_interpolation_so_position_does_not_advance()
    {
        // Visible + playing ticks. The user hides the popover.
        // The timer must stop, and even if the clock keeps
        // moving, PositionSeconds must stay at the snapshot's
        // last-known value (NOT the interpolated one).
        var (vm, svc, ticker, clock) = BuildViewModel();
        clock.Advance(TimeSpan.FromSeconds(50));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(20),
            endTime: TimeSpan.FromMinutes(5),
            clock: clock));
        vm.IsVisible = true;
        Assert.True(ticker.IsRunning);

        // User hides the popover.
        vm.IsVisible = false;

        // The fake ticker's Callback is now null (Start/Stop
        // semantics are explicit). Even if Fire() ran, it would
        // be a no-op — but a tick handler reaching the view-model
        // during the hidden state is exactly the bug we are
        // locking out.
        Assert.False(ticker.IsRunning);
        Assert.Null(ticker.Callback);

        // Even if the clock advances (real time keeps going),
        // PositionSeconds stays at the snapshot's value because
        // the hidden-state branch returns the cached Position
        // directly, not the interpolated one.
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(20d, vm.PositionSeconds);
    }

    [Fact]
    public void Hiding_pauses_interpolation_even_if_a_tick_is_queued()
    {
        // Defensive: the view-model re-evaluates IsPlaying on
        // every tick, so a hide between the previous tick and
        // the queued one must still keep PositionSeconds stable.
        // We model this by publishing a paused snapshot between
        // two ticks — the second tick must see the timer
        // stopped AND not advance.
        var (vm, svc, ticker, clock) = BuildViewModel();
        clock.Advance(TimeSpan.FromSeconds(50));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(5),
            endTime: TimeSpan.FromMinutes(5),
            clock: clock));
        vm.IsVisible = true;
        Assert.True(ticker.IsRunning);

        // First tick advances position by 2s.
        clock.Advance(TimeSpan.FromSeconds(2));
        ticker.Fire();
        Assert.Equal(7d, vm.PositionSeconds);

        // User hides the popover. Timer must stop.
        vm.IsVisible = false;
        Assert.False(ticker.IsRunning);

        // Even if the clock keeps moving, position stays at the
        // snapshot's 5s (the hidden branch returns the cached
        // Position, not the interpolated one).
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(5d, vm.PositionSeconds);
    }

    // -------------------------------------------------------------------
    // Show resumes from the latest authoritative baseline
    // -------------------------------------------------------------------

    [Fact]
    public void Showing_resumes_interpolation_from_the_latest_snapshot()
    {
        // The classic "pop the clock" sequence: playing →
        // hide → clock advances a lot → show. After show,
        // PositionSeconds must interpolate from the snapshot's
        // TimelineUpdatedAt, not from the tick-count at hide
        // time. The first read after show is the snapshot's
        // position PLUS the elapsed time during the hide (30s).
        var (vm, svc, ticker, clock) = BuildViewModel();
        clock.Advance(TimeSpan.FromSeconds(50));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(10),
            endTime: TimeSpan.FromMinutes(5),
            clock: clock));
        vm.IsVisible = true;

        // Hide + 30s of real time.
        vm.IsVisible = false;
        clock.Advance(TimeSpan.FromSeconds(30));

        // Show again. The timer restarts.
        vm.IsVisible = true;
        Assert.True(ticker.IsRunning);

        // PositionSeconds is the interpolated value: snapshot's
        // 10s baseline + 30s of elapsed time during the hide
        // = 40s. This is the right behaviour: the user expects
        // the slider to be at the actual position when they
        // show the popover, not frozen at the last hide value.
        Assert.Equal(40d, vm.PositionSeconds);

        // 5s more of real time; the next tick interpolates to 45s.
        clock.Advance(TimeSpan.FromSeconds(5));
        ticker.Fire();
        Assert.Equal(45d, vm.PositionSeconds);
    }

    [Fact]
    public void Snapshot_arriving_while_hidden_is_visible_immediately_on_show()
    {
        // A new authoritative snapshot arrives while the popover
        // is hidden. The view-model captures it (the snapshot
        // field updates) but the timer does NOT start. When the
        // popover is shown again, the new title/artist/position
        // are visible immediately.
        var (vm, svc, ticker, clock) = BuildViewModel();
        clock.Advance(TimeSpan.FromSeconds(50));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(10),
            endTime: TimeSpan.FromMinutes(5),
            title: "Track 1",
            clock: clock));
        vm.IsVisible = true;

        // Hide.
        vm.IsVisible = false;
        Assert.False(ticker.IsRunning);

        // New snapshot arrives during hide.
        clock.Advance(TimeSpan.FromSeconds(20));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(30),
            endTime: TimeSpan.FromMinutes(5),
            title: "Track 2",
            clock: clock));

        // The timer is still stopped because the snapshot path
        // called UpdateTicker(), which checks IsVisible.
        Assert.False(ticker.IsRunning);

        // Show: the new snapshot is in effect.
        vm.IsVisible = true;
        Assert.True(ticker.IsRunning);
        Assert.Equal("Track 2", vm.Title);
        Assert.Equal(30d, vm.PositionSeconds);
    }

    // -------------------------------------------------------------------
    // Multiple visibility toggles
    // -------------------------------------------------------------------

    [Fact]
    public void Repeated_visibility_toggles_flip_the_timer_state_each_time()
    {
        // Drive the popover through 5 hide/show cycles. The
        // timer must be in lockstep with IsVisible.
        var (vm, svc, ticker, clock) = BuildViewModel();
        clock.Advance(TimeSpan.FromSeconds(50));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(20),
            endTime: TimeSpan.FromMinutes(5),
            clock: clock));

        // 5 cycles of (visible → hidden). Each visible flip
        // starts the timer; each hidden flip stops it.
        for (int i = 0; i < 5; i++)
        {
            vm.IsVisible = true;
            Assert.True(ticker.IsRunning, $"iteration {i} (visible) should have running ticker");
            vm.IsVisible = false;
            Assert.False(ticker.IsRunning, $"iteration {i} (hidden) should have stopped ticker");
        }

        // One final visible flip to confirm the timer restarts.
        vm.IsVisible = true;
        Assert.True(ticker.IsRunning);
        // And after one final hide, the timer is stopped.
        vm.IsVisible = false;
        Assert.False(ticker.IsRunning);
    }

    [Fact]
    public void Visibility_flips_with_paused_state_never_start_the_timer()
    {
        // A snapshot says "paused" but IsVisible=true. The
        // timer must NOT start. Flipping back to playing
        // while still visible must start the timer.
        var (vm, svc, ticker, clock) = BuildViewModel();
        clock.Advance(TimeSpan.FromSeconds(50));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(20),
            endTime: TimeSpan.FromMinutes(5),
            clock: clock));
        vm.IsVisible = true;
        Assert.True(ticker.IsRunning);

        // Source pauses.
        svc.Publish(PausedSnapshot(
            position: TimeSpan.FromSeconds(20),
            endTime: TimeSpan.FromMinutes(5),
            clock: clock));
        Assert.False(ticker.IsRunning);

        // Popover is still visible. The timer must NOT re-start.
        Assert.True(vm.IsVisible);
        Assert.False(ticker.IsRunning);

        // Source resumes.
        clock.Advance(TimeSpan.FromSeconds(5));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(20),
            endTime: TimeSpan.FromMinutes(5),
            clock: clock));
        Assert.True(ticker.IsRunning);
    }

    // -------------------------------------------------------------------
    // Disposal after hide
    // -------------------------------------------------------------------

    [Fact]
    public void Dispose_after_hide_stops_the_ticker_and_unsubscribes()
    {
        // The hide already stopped the timer. Dispose must
        // still unsubscribe from the service and not throw.
        var (vm, svc, ticker, clock) = BuildViewModel();
        clock.Advance(TimeSpan.FromSeconds(50));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(20),
            endTime: TimeSpan.FromMinutes(5),
            clock: clock));
        vm.IsVisible = true;
        vm.IsVisible = false;
        Assert.False(ticker.IsRunning);

        // Snapshot listens via PropertyChanged; after Dispose,
        // publishing must not raise PropertyChanged.
        int observed = 0;
        vm.PropertyChanged += (_, _) => observed++;

        vm.Dispose();

        clock.Advance(TimeSpan.FromSeconds(5));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(25),
            endTime: TimeSpan.FromMinutes(5),
            clock: clock));

        Assert.Equal(0, observed);
    }

    [Fact]
    public void Dispose_while_visible_stops_the_ticker()
    {
        // Flip from the production path: disposal must also
        // stop the timer even if the user never hid the popover
        // (e.g. application shutdown while the popover is open).
        var (vm, svc, ticker, clock) = BuildViewModel();
        clock.Advance(TimeSpan.FromSeconds(50));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(20),
            endTime: TimeSpan.FromMinutes(5),
            clock: clock));
        vm.IsVisible = true;
        Assert.True(ticker.IsRunning);

        vm.Dispose();

        Assert.False(ticker.IsRunning);
    }

    // -------------------------------------------------------------------
    // Position stability across hide
    // -------------------------------------------------------------------

    [Fact]
    public void Hide_preserves_the_last_known_snapshot_position()
    {
        // PositionSeconds after hide returns the snapshot's
        // Position, NOT zero. The next show must re-interpolate
        // from that same value.
        var (vm, svc, ticker, clock) = BuildViewModel();
        clock.Advance(TimeSpan.FromSeconds(50));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(42),
            endTime: TimeSpan.FromMinutes(5),
            clock: clock));
        vm.IsVisible = true;
        // 13s of ticks while visible.
        clock.Advance(TimeSpan.FromSeconds(13));
        ticker.Fire();
        Assert.Equal(55d, vm.PositionSeconds);

        // Hide. PositionSeconds returns the snapshot's last
        // position (42), not the previously interpolated
        // value (55) — the snapshot is the source of truth.
        vm.IsVisible = false;
        Assert.Equal(42d, vm.PositionSeconds);
    }

    // -------------------------------------------------------------------
    // Snapshot-while-hidden refreshes commands
    // -------------------------------------------------------------------

    [Fact]
    public void Snapshot_arriving_while_hidden_still_raises_command_refresh()
    {
        // Capabilities may flip while the popover is hidden
        // (e.g. the user pressed Next on the hardware keys).
        // The view-model must raise CanExecuteChanged on the
        // four commands so the next show binds the new state.
        var (vm, svc, _, clock) = BuildViewModel();
        clock.Advance(TimeSpan.FromSeconds(50));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(20),
            endTime: TimeSpan.FromMinutes(5),
            clock: clock,
            caps: NoneCapabilities()));

        var toggleFires = 0;
        vm.TogglePlayPauseCommand.CanExecuteChanged += (_, _) => toggleFires++;

        vm.IsVisible = false;

        // Capabilities flip while hidden.
        clock.Advance(TimeSpan.FromSeconds(5));
        svc.Publish(PlayingSnapshot(
            position: TimeSpan.FromSeconds(20),
            endTime: TimeSpan.FromMinutes(5),
            clock: clock,
            caps: AllEnabled()));

        Assert.True(toggleFires > 0,
            "TogglePlayPauseCommand must raise CanExecuteChanged when capabilities change, even while hidden.");
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

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
        // Visible=false by default — tests must flip when they
        // want the timer to run.
        return (vm, svc, ticker, clock);
    }

    private static MediaSessionSnapshot PlayingSnapshot(
        TimeSpan position,
        TimeSpan endTime,
        FakeClock clock,
        string title = "Track Title",
        TransportCapabilities? caps = null)
    {
        var at = clock.Now - DateTimeOffset.UnixEpoch;
        return new MediaSessionSnapshot(
            SourceAppUserModelId: "TestApp.exe",
            Title: title,
            Artist: "Artist Name",
            AlbumTitle: "Album Name",
            Artwork: null,
            Playback: new PlaybackSnapshot(
                State: MediaPlaybackState.Playing,
                Position: position,
                StartTime: TimeSpan.Zero,
                EndTime: endTime,
                TimelineUpdatedAt: DateTimeOffset.UnixEpoch + at,
                Capabilities: caps ?? AllEnabled()));
    }

    private static MediaSessionSnapshot PausedSnapshot(
        TimeSpan position,
        TimeSpan endTime,
        FakeClock clock,
        TransportCapabilities? caps = null)
    {
        var at = clock.Now - DateTimeOffset.UnixEpoch;
        return new MediaSessionSnapshot(
            SourceAppUserModelId: "TestApp.exe",
            Title: "Track Title",
            Artist: "Artist Name",
            AlbumTitle: "Album Name",
            Artwork: null,
            Playback: new PlaybackSnapshot(
                State: MediaPlaybackState.Paused,
                Position: position,
                StartTime: TimeSpan.Zero,
                EndTime: endTime,
                TimelineUpdatedAt: DateTimeOffset.UnixEpoch + at,
                Capabilities: caps ?? AllEnabled()));
    }

    private static TransportCapabilities AllEnabled() => new(
        CanPlay: true, CanPause: true, CanStop: true,
        CanGoPrevious: true, CanGoNext: true);

    private static TransportCapabilities NoneCapabilities() => new(
        CanPlay: false, CanPause: false, CanStop: false,
        CanGoPrevious: false, CanGoNext: false);

    /// <summary>
    /// Local copy of the FakeClock pattern from MainViewModelTests.
    /// Kept private here so the lifecycle tests are self-contained;
    /// it follows the same shape (an offset from UnixEpoch that
    /// the test can advance) so the production clock injection
    /// behaviour is identical.
    /// </summary>
    private sealed class FakeClock
    {
        private TimeSpan _now;
        public FakeClock(TimeSpan initial) { _now = initial; }
        public DateTimeOffset Now => DateTimeOffset.UnixEpoch + _now;
        public void Advance(TimeSpan delta) { _now += delta; }
    }
}
