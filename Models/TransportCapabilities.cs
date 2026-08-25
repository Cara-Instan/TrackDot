namespace TrackDot.Models;

/// <summary>
/// Immutable record describing which transport commands the active
/// SMTC session currently supports. The UI binds to these booleans
/// to enable/disable Play/Pause/Stop/Previous/Next.
/// </summary>
public sealed record TransportCapabilities(
    bool CanPlay,
    bool CanPause,
    bool CanStop,
    bool CanGoPrevious,
    bool CanGoNext,
    bool CanSeek = false,
    bool CanChangeShuffle = false,
    bool CanChangeAutoRepeatMode = false)
{
    /// <summary>
    /// All controls disabled. Returned for "no session" so the UI
    /// can render disabled buttons without null checks.
    /// </summary>
    public static readonly TransportCapabilities None = new(
        CanPlay: false,
        CanPause: false,
        CanStop: false,
        CanGoPrevious: false,
        CanGoNext: false,
        CanSeek: false,
        CanChangeShuffle: false,
        CanChangeAutoRepeatMode: false);
}
