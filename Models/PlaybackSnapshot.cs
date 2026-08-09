using System;

namespace TrackDot.Models;

/// <summary>
/// Immutable snapshot of playback state at a specific moment in time.
/// <see cref="TimelineUpdatedAt"/> is the monotonic timestamp recorded
/// when this snapshot was produced; the view model uses it as the
/// baseline for progress interpolation while the next authoritative
/// event is pending.
/// </summary>
public sealed record PlaybackSnapshot(
    MediaPlaybackState State,
    TimeSpan Position,
    TimeSpan StartTime,
    TimeSpan EndTime,
    DateTimeOffset TimelineUpdatedAt,
    TransportCapabilities Capabilities)
{
    /// <summary>
    /// Neutral playback snapshot corresponding to "no active session".
    /// All times are zero, capabilities are disabled, and the state
    /// is <see cref="MediaPlaybackState.None"/>.
    /// </summary>
    public static readonly PlaybackSnapshot Empty = new(
        State: MediaPlaybackState.None,
        Position: TimeSpan.Zero,
        StartTime: TimeSpan.Zero,
        EndTime: TimeSpan.Zero,
        TimelineUpdatedAt: DateTimeOffset.MinValue,
        Capabilities: TransportCapabilities.None);
}
