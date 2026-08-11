using System;
using TrackDot.Models;

namespace TrackDot.Services;

/// <summary>
/// Pure, stateless interpolation of a media session's playback
/// <see cref="PlaybackSnapshot.Position"/> between SMTC
/// <c>TimelinePropertiesChanged</c> events.
/// </summary>
/// <remarks>
/// <para>
/// The interpolator is a function of five inputs:
/// <list type="bullet">
///   <item><see cref="MediaPlaybackState"/> — only <c>Playing</c> advances.</item>
///   <item><c>baselinePosition</c> — the position at the time of the last authoritative event.</item>
///   <item><c>baselineTimestamp</c> — the monotonic clock reading when that event was recorded.</item>
///   <item><c>endTime</c> — the track's reported duration, used for upper clamping.</item>
///   <item><c>now</c> — the current monotonic clock reading.</item>
/// </list>
/// </para>
/// <para>
/// The function never reads the wall clock, never calls into SMTC,
/// and never touches the view-model. That makes it exhaustively
/// testable: the only moving part is the time delta between
/// <c>baselineTimestamp</c> and <c>now</c>.
/// </para>
/// <para>
/// <b>Backward seeks</b> are not handled here — the view-model
/// detects them by comparing new snapshots and resets the baseline.
/// This class has no "previous baseline" to compare against.
/// </para>
/// <para>
/// <b>Unknown duration</b> is signalled by <c>endTime == TimeSpan.Zero</c>.
/// The interpolator returns the baseline unchanged in that case,
/// because we cannot advance a position we have no upper bound for.
/// </para>
/// </remarks>
public static class ProgressInterpolator
{
    /// <summary>
    /// Returns the interpolated <see cref="TimeSpan"/> position for
    /// the given baseline and monotonic clock reading. See the
    /// class remarks for the full contract.
    /// </summary>
    /// <param name="state">Current playback state. Only <see cref="MediaPlaybackState.Playing"/> advances.</param>
    /// <param name="baselinePosition">Position at the time of the last authoritative timeline event.</param>
    /// <param name="baselineTimestamp">Monotonic clock reading recorded with the baseline. <see cref="DateTimeOffset.UnixEpoch"/> + monotonic is the recommended encoding.</param>
    /// <param name="endTime">Track duration. <see cref="TimeSpan.Zero"/> means "unknown" — the result stays at the baseline.</param>
    /// <param name="now">Current monotonic clock reading. Must be encoded with the same origin as <paramref name="baselineTimestamp"/>.</param>
    public static TimeSpan Evaluate(
        MediaPlaybackState state,
        TimeSpan baselinePosition,
        DateTimeOffset baselineTimestamp,
        TimeSpan endTime,
        DateTimeOffset now)
    {
        // Only Playing advances. For every other state the
        // interpolation result equals the baseline exactly. This
        // matches the plan §1.4: the UI timer only interpolates
        // while playing.
        if (state != MediaPlaybackState.Playing)
        {
            return ClampToRange(baselinePosition, endTime);
        }

        // Unknown duration: cannot advance without an upper bound.
        // Live streams and SMTC's "no end time" both land here.
        if (endTime <= TimeSpan.Zero)
        {
            return ClampToRange(baselinePosition, TimeSpan.Zero);
        }

        // Defensive: a clock that pre-dates the baseline (e.g. a
        // session switch overwrote TimelineUpdatedAt with an older
        // value) must not yield a negative result. Treat the
        // elapsed time as zero in that case.
        var elapsed = now - baselineTimestamp;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var advanced = baselinePosition + elapsed;
        return ClampToRange(advanced, endTime);
    }

    /// <summary>
    /// Clamp <paramref name="value"/> into <c>[TimeSpan.Zero, endTime]</c>.
    /// If the value is already past <paramref name="endTime"/> (SMTC
    /// race between timeline and controls), pin to the end. Negative
    /// values pin to zero.
    /// </summary>
    private static TimeSpan ClampToRange(TimeSpan value, TimeSpan endTime)
    {
        if (value < TimeSpan.Zero) return TimeSpan.Zero;
        if (endTime > TimeSpan.Zero && value > endTime) return endTime;
        return value;
    }
}
