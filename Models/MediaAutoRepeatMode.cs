namespace TrackDot.Models;

/// <summary>
/// Repeat mode of the media playback session.
/// </summary>
public enum MediaAutoRepeatMode
{
    /// <summary>No repeat.</summary>
    None = 0,

    /// <summary>Repeat current track/item.</summary>
    Track = 1,

    /// <summary>Repeat entire playlist or queue.</summary>
    List = 2,
}

