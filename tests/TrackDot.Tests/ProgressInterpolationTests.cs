using System;
using TrackDot.Models;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Table-driven tests for <see cref="ProgressInterpolator"/>.
///
/// <para>
/// The interpolator is a pure function over (<see cref="MediaPlaybackState"/>,
/// baseline <c>Position</c>, baseline <c>TimelineUpdatedAt</c>, <c>EndTime</c>,
/// current monotonic <c>now</c>). Tests inject a fake monotonic clock so they
/// run deterministically without sleeping.
/// </para>
/// </summary>
public sealed class ProgressInterpolationTests
{
    // -----------------------------------------------------------------------
    // Playing — advances linearly from the baseline
    // -----------------------------------------------------------------------

    [Fact]
    public void Playing_advances_by_elapsed_monotonic_time()
    {
        var t = new FakeClock(TimeSpan.Zero);
        // Advance the clock to T=10s before taking the baseline,
        // so the elapsed delta at query time is meaningful.
        t.Advance(TimeSpan.FromSeconds(10));
        var baseline = MakeBaseline(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(30),
            endTime: TimeSpan.FromMinutes(5),
            at: t.Now - DateTimeOffset.UnixEpoch);

        t.Advance(TimeSpan.FromSeconds(7));

        var result = ProgressInterpolator.Evaluate(
            state: MediaPlaybackState.Playing,
            baselinePosition: baseline.Position,
            baselineTimestamp: baseline.TimelineUpdatedAt,
            endTime: baseline.EndTime,
            now: t.Now);

        Assert.Equal(TimeSpan.FromSeconds(37), result);
    }

    [Fact]
    public void Playing_advances_from_zero_when_baseline_is_zero()
    {
        var t = new FakeClock(TimeSpan.FromSeconds(100));
        var baseline = MakeBaseline(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.Zero,
            endTime: TimeSpan.FromMinutes(3),
            at: TimeSpan.FromSeconds(100));

        t.Advance(TimeSpan.FromSeconds(12));

        var result = ProgressInterpolator.Evaluate(
            state: MediaPlaybackState.Playing,
            baselinePosition: baseline.Position,
            baselineTimestamp: baseline.TimelineUpdatedAt,
            endTime: baseline.EndTime,
            now: t.Now);

        Assert.Equal(TimeSpan.FromSeconds(12), result);
    }

    // -----------------------------------------------------------------------
    // Not playing — does not advance, returns the baseline unchanged
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(MediaPlaybackState.None)]
    [InlineData(MediaPlaybackState.Closed)]
    [InlineData(MediaPlaybackState.Opened)]
    [InlineData(MediaPlaybackState.Changing)]
    [InlineData(MediaPlaybackState.Stopped)]
    [InlineData(MediaPlaybackState.Paused)]
    public void Not_Playing_returns_baseline_unchanged_regardless_of_elapsed(
        MediaPlaybackState state)
    {
        var t = new FakeClock(TimeSpan.Zero);
        var baselinePosition = TimeSpan.FromSeconds(42);
        var baselineTimestamp = DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(5);
        var endTime = TimeSpan.FromMinutes(5);

        t.Advance(TimeSpan.FromMinutes(10));

        var result = ProgressInterpolator.Evaluate(
            state: state,
            baselinePosition: baselinePosition,
            baselineTimestamp: baselineTimestamp,
            endTime: endTime,
            now: t.Now);

        Assert.Equal(baselinePosition, result);
    }

    // -----------------------------------------------------------------------
    // Clamping
    // -----------------------------------------------------------------------

    [Fact]
    public void Playing_clamps_to_endTime_when_elapsed_would_exceed_duration()
    {
        var t = new FakeClock(TimeSpan.FromSeconds(50));
        var baseline = MakeBaseline(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(58),
            endTime: TimeSpan.FromMinutes(1),  // 60s
            at: TimeSpan.FromSeconds(50));

        // 5 hours later, the snapshot still says Playing. The
        // interpolator must clamp to EndTime, not let the position
        // run into the next track.
        t.Advance(TimeSpan.FromHours(5));

        var result = ProgressInterpolator.Evaluate(
            state: MediaPlaybackState.Playing,
            baselinePosition: baseline.Position,
            baselineTimestamp: baseline.TimelineUpdatedAt,
            endTime: baseline.EndTime,
            now: t.Now);

        Assert.Equal(baseline.EndTime, result);
    }

    [Fact]
    public void Playing_never_returns_negative_when_clock_is_before_baseline()
    {
        // Defensive: if the clock somehow pre-dates the baseline
        // (e.g. a session switch overwrote TimelineUpdatedAt with an
        // older value), the result must not go negative.
        var baseline = MakeBaseline(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(10),
            endTime: TimeSpan.FromMinutes(5),
            at: TimeSpan.FromSeconds(20));

        var result = ProgressInterpolator.Evaluate(
            state: MediaPlaybackState.Playing,
            baselinePosition: baseline.Position,
            baselineTimestamp: baseline.TimelineUpdatedAt,
            endTime: baseline.EndTime,
            now: DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(15));  // 5s BEFORE the baseline

        Assert.True(result >= TimeSpan.Zero,
            $"Position must never be negative; got {result}.");
        Assert.Equal(baseline.Position, result);
    }

    [Fact]
    public void Playing_with_unknown_duration_clamps_to_baseline()
    {
        // When the source hasn't reported EndTime (live streams,
        // ad-hoc media, very first event before timeline is read),
        // EndTime is TimeSpan.Zero. The interpolator must not
        // divide by zero or jump to zero — it should return the
        // baseline and not advance.
        var t = new FakeClock(TimeSpan.Zero);
        var baseline = MakeBaseline(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(7),
            endTime: TimeSpan.Zero,  // unknown
            at: TimeSpan.Zero);

        t.Advance(TimeSpan.FromSeconds(30));

        var result = ProgressInterpolator.Evaluate(
            state: MediaPlaybackState.Playing,
            baselinePosition: baseline.Position,
            baselineTimestamp: baseline.TimelineUpdatedAt,
            endTime: baseline.EndTime,
            now: t.Now);

        Assert.Equal(baseline.Position, result);
    }

    [Fact]
    public void Playing_clamps_position_that_already_exceeds_endTime()
    {
        // SMTC can hand us a position greater than EndTime during a
        // race between the timeline and the controls event. The
        // interpolator must clamp, not propagate the garbage.
        var t = new FakeClock(TimeSpan.FromSeconds(100));
        var baseline = MakeBaseline(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(120),  // already past 60s
            endTime: TimeSpan.FromSeconds(60),
            at: TimeSpan.FromSeconds(100));

        t.Advance(TimeSpan.FromSeconds(1));

        var result = ProgressInterpolator.Evaluate(
            state: MediaPlaybackState.Playing,
            baselinePosition: baseline.Position,
            baselineTimestamp: baseline.TimelineUpdatedAt,
            endTime: baseline.EndTime,
            now: t.Now);

        Assert.Equal(baseline.EndTime, result);
    }

    [Fact]
    public void Not_Playing_with_position_exceeding_endTime_still_clamps()
    {
        // Same defensive clamp applies when paused but SMTC has
        // reported a stale end-time race.
        var baseline = MakeBaseline(
            state: MediaPlaybackState.Paused,
            position: TimeSpan.FromSeconds(999),
            endTime: TimeSpan.FromSeconds(60),
            at: TimeSpan.FromSeconds(5));

        var result = ProgressInterpolator.Evaluate(
            state: MediaPlaybackState.Paused,
            baselinePosition: baseline.Position,
            baselineTimestamp: baseline.TimelineUpdatedAt,
            endTime: baseline.EndTime,
            now: DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(60));

        Assert.Equal(baseline.EndTime, result);
    }

    // -----------------------------------------------------------------------
    // Backward seek — handled by the view-model passing a new baseline
    // -----------------------------------------------------------------------

    [Fact]
    public void New_baseline_with_smaller_position_treated_as_new_origin()
    {
        // The interpolator is pure; "backward seek" is just the
        // view-model passing a fresh baseline. Verify the pure
        // function honours the new baseline + new timestamp without
        // any state of its own.
        var t = new FakeClock(TimeSpan.FromSeconds(50));
        var oldBaseline = MakeBaseline(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(40),
            endTime: TimeSpan.FromMinutes(2),
            at: TimeSpan.FromSeconds(50));

        t.Advance(TimeSpan.FromSeconds(3));
        var seekedTo = TimeSpan.FromSeconds(15);
        var newBaseline = MakeBaseline(
            state: MediaPlaybackState.Playing,
            position: seekedTo,
            endTime: TimeSpan.FromMinutes(2),
            at: t.Now - DateTimeOffset.UnixEpoch);

        t.Advance(TimeSpan.FromSeconds(2));

        var result = ProgressInterpolator.Evaluate(
            state: MediaPlaybackState.Playing,
            baselinePosition: newBaseline.Position,
            baselineTimestamp: newBaseline.TimelineUpdatedAt,
            endTime: newBaseline.EndTime,
            now: t.Now);

        Assert.Equal(seekedTo + TimeSpan.FromSeconds(2), result);

        // The old baseline, evaluated at the same clock, would have
        // returned 40+5 = 45s. Confirms the function truly is
        // stateless and only looks at the inputs it was given.
        var oldResult = ProgressInterpolator.Evaluate(
            state: MediaPlaybackState.Playing,
            baselinePosition: oldBaseline.Position,
            baselineTimestamp: oldBaseline.TimelineUpdatedAt,
            endTime: oldBaseline.EndTime,
            now: t.Now);
        Assert.Equal(TimeSpan.FromSeconds(45), oldResult);
    }

    // -----------------------------------------------------------------------
    // Determinism
    // -----------------------------------------------------------------------

    [Fact]
    public void Same_inputs_return_same_result()
    {
        var t = new FakeClock(TimeSpan.FromSeconds(12));
        var baseline = MakeBaseline(
            state: MediaPlaybackState.Playing,
            position: TimeSpan.FromSeconds(8),
            endTime: TimeSpan.FromMinutes(2),
            at: TimeSpan.FromSeconds(10));

        t.Advance(TimeSpan.FromSeconds(3));

        var first = ProgressInterpolator.Evaluate(
            MediaPlaybackState.Playing,
            baseline.Position, baseline.TimelineUpdatedAt, baseline.EndTime, t.Now);
        var second = ProgressInterpolator.Evaluate(
            MediaPlaybackState.Playing,
            baseline.Position, baseline.TimelineUpdatedAt, baseline.EndTime, t.Now);

        Assert.Equal(first, second);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static PlaybackSnapshot MakeBaseline(
        MediaPlaybackState state,
        TimeSpan position,
        TimeSpan endTime,
        TimeSpan at)
    {
        return new PlaybackSnapshot(
            State: state,
            Position: position,
            StartTime: TimeSpan.Zero,
            EndTime: endTime,
            TimelineUpdatedAt: ToDateTimeOffset(at),
            Capabilities: TransportCapabilities.None);
    }

    /// <summary>
    /// Convert a monotonic TimeSpan to a <see cref="DateTimeOffset"/>
    /// for compatibility with the snapshot's <c>TimelineUpdatedAt</c>
    /// field. The mapping is <c>DateTimeOffset.UnixEpoch + at</c> —
    /// the absolute date is irrelevant; the interpolator only uses
    /// the difference between <c>now</c> and <c>TimelineUpdatedAt</c>.
    /// </summary>
    private static DateTimeOffset ToDateTimeOffset(TimeSpan sinceEpoch)
        => DateTimeOffset.UnixEpoch + sinceEpoch;

    /// <summary>
    /// In-process monotonic clock used by the interpolator tests.
    /// The view-model will use a <c>Stopwatch</c>-backed version of
    /// the same contract in production.
    /// </summary>
    private sealed class FakeClock
    {
        private TimeSpan _now;
        public FakeClock(TimeSpan initial) { _now = initial; }
        public DateTimeOffset Now => ToDateTimeOffset(_now);
        public void Advance(TimeSpan delta) { _now += delta; }
    }
}
