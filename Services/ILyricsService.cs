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
    Task<IReadOnlyList<LyricLine>> FetchLyricsAsync(
        string title,
        string artist,
        string album = "",
        TimeSpan duration = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for lyrics candidates matching a query string (e.g. track name + artist).
    /// </summary>
    Task<IReadOnlyList<LyricsSearchResult>> SearchCandidatesAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches and parses lyrics for a specific search result.
    /// </summary>
    Task<IReadOnlyList<LyricLine>> FetchLyricsByResultAsync(
        LyricsSearchResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses custom or local LRC / TTML / plain lyrics text into <see cref="LyricLine"/> entries with Furigana/Romaji.
    /// </summary>
    Task<IReadOnlyList<LyricLine>> ParseCustomLyricsAsync(
        string rawContent,
        string? format = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores lyrics directly in memory cache for a specified track.
    /// </summary>
    void SaveLyricsToCache(
        string title,
        string artist,
        string album,
        IReadOnlyList<LyricLine> lyrics);
}
