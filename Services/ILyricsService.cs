using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrackDot.Models;

namespace TrackDot.Services;

/// <summary>
/// Service interface for fetching and parsing synced or plain lyrics,
/// including Japanese Kanji/Kana to Romaji / Furigana conversion.
/// </summary>
public interface ILyricsService
{
    /// <summary>
    /// Fetches lyrics for the specified track metadata, parsing LRC synced timestamps
    /// and performing Romaji / Furigana conversion when Japanese text is detected.
    /// </summary>
    /// <param name="title">Track title.</param>
    /// <param name="artist">Track artist.</param>
    /// <param name="album">Track album (optional).</param>
    /// <param name="duration">Track duration (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of parsed <see cref="LyricLine"/> entries, or empty list if not found.</returns>
    Task<IReadOnlyList<LyricLine>> FetchLyricsAsync(
        string title,
        string artist,
        string album = "",
        TimeSpan duration = default,
        CancellationToken cancellationToken = default);
}
