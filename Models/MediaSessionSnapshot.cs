using System.Windows.Media;

namespace TrackDot.Models;

/// <summary>
/// Immutable snapshot of a media session's metadata, plus its current
/// playback snapshot. The view model and any view layer consume this
/// type — they never see WinRT session objects directly.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SourceAppUserModelId"/> is the AUMID of the source app
/// (Spotify, Chrome, etc.). It is exposed so a future source picker
/// can identify or list source applications without changing the
/// contract.
/// </para>
/// <para>
/// <see cref="Artwork"/> is already a frozen <see cref="ImageSource"/>
/// safe to read on the WPF UI thread. Decode happens in the
/// controller service before publishing.
/// </para>
/// <para>
/// <see cref="Volume"/> and <see cref="IsMuted"/> are sourced from
/// the CoreAudio <c>ISimpleAudioVolume</c> API (not SMTC) and are
/// updated whenever the active session changes or a volume/mute
/// command completes. They default to <c>1.0 / false</c> when no
/// CoreAudio session can be matched to the current SMTC source.
/// </para>
/// </remarks>
public sealed record MediaSessionSnapshot(
    string? SourceAppUserModelId,
    string Title,
    string Artist,
    string AlbumTitle,
    ImageSource? Artwork,
    PlaybackSnapshot Playback,
    double Volume = 1.0,
    bool IsMuted = false)
{
    /// <summary>
    /// Neutral snapshot for "no active session". Title/artist/album
    /// are empty strings, artwork is null, playback is
    /// <see cref="PlaybackSnapshot.Empty"/>, volume is 1.0, and
    /// muted is false. View-model code can treat <see cref="Empty"/>
    /// as a single safe default and apply user-facing fallbacks (e.g.
    /// "Nothing playing") at the view layer.
    /// </summary>
    public static readonly MediaSessionSnapshot Empty = new(
        SourceAppUserModelId: null,
        Title: string.Empty,
        Artist: string.Empty,
        AlbumTitle: string.Empty,
        Artwork: null,
        Playback: PlaybackSnapshot.Empty,
        Volume: 1.0,
        IsMuted: false);
}
