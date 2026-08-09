namespace TrackDot.Models;

/// <summary>
/// Coarse-grained playback state mapping.
/// SMTC's <c>GlobalSystemMediaTransportControlsSessionPlaybackStatus</c>
/// exposes Closed/Opened/Changing/Stopped/Playing/Paused. We add
/// <see cref="None"/> as the neutral state used when no session exists
/// or before the first state has been read.
/// </summary>
public enum MediaPlaybackState
{
    None,
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused,
}
