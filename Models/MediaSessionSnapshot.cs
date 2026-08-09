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
/// </remarks>
public sealed record MediaSessionSnapshot(
    string? SourceAppUserModelId,
    string Title,
    string Artist,
    string AlbumTitle,
    ImageSource? Artwork,
    PlaybackSnapshot Playback)
{
    /// <summary>
    /// Neutral snapshot for "no active session". Title/artist/album
    /// are empty strings, artwork is null, and playback is
    /// <see cref="PlaybackSnapshot.Empty"/>. View-model code can
    /// treat <see cref="Empty"/> as a single safe default and apply
    /// user-facing fallbacks (e.g. "Nothing playing") at the view layer.
    /// </summary>
    public static readonly MediaSessionSnapshot Empty = new(
        SourceAppUserModelId: null,
        Title: string.Empty,
        Artist: string.Empty,
        AlbumTitle: string.Empty,
        Artwork: null,
        Playback: PlaybackSnapshot.Empty);
}
