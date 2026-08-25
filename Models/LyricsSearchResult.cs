using System;

namespace TrackDot.Models;

/// <summary>
/// Immutable record representing a search candidate returned from online lyrics providers.
/// </summary>
public sealed record LyricsSearchResult(
    int? Id,
    string TrackName,
    string ArtistName,
    string? AlbumName,
    TimeSpan? Duration,
    bool HasSyncedLyrics,
    bool HasPlainLyrics,
    string Source,
    string? RawSyncedLyrics = null,
    string? RawPlainLyrics = null)
{
    public string FormattedDuration => Duration.HasValue
        ? $"{Duration.Value.Minutes}:{Duration.Value.Seconds:D2}"
        : "--:--";

    public string SyncBadge => HasSyncedLyrics ? "Synced" : "Plain";
}

