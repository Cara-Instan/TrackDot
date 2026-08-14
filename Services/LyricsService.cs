using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kawazu;
using TrackDot.Models;

namespace TrackDot.Services;

/// <summary>
/// Fetches lyrics from lrclib.net, parses LRC synchronized timestamps,
/// and uses Kawazu for Japanese Kanji/Kana to Romaji / Furigana conversion.
/// </summary>
public class LyricsService : ILyricsService
{
    private static readonly Regex LrcTimestampRegex = new(
        @"\[(?<min>\d+):(?<sec>\d{2})(?:[\.:](?<ms>\d{2,3}))?\]",
        RegexOptions.Compiled);

    private static readonly Regex JapaneseCharRegex = new(
        @"[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF]",
        RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, IReadOnlyList<LyricLine>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private KawazuConverter? _kawazuConverter;
    private bool _kawazuInitAttempted;
    private readonly object _kawazuLock = new();

    public LyricsService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TrackDot/0.1.0 (https://github.com/herlandroando/TrackDot)");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LyricLine>> FetchLyricsAsync(
        string title,
        string artist,
        string album = "",
        TimeSpan duration = default,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title)) return Array.Empty<LyricLine>();

        string cacheKey = $"{artist.Trim()} - {title.Trim()} - {album.Trim()}";
        if (_cache.TryGetValue(cacheKey, out var cachedLyrics))
        {
            return cachedLyrics;
        }

        try
        {
            var dto = await FetchFromLrclibAsync(title, artist, album, duration, cancellationToken).ConfigureAwait(false);
            if (dto is null)
            {
                _cache[cacheKey] = Array.Empty<LyricLine>();
                return Array.Empty<LyricLine>();
            }

            var lines = await ParseAndConvertLyricsAsync(dto, cancellationToken).ConfigureAwait(false);
            _cache[cacheKey] = lines;
            return lines;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsService] Error fetching lyrics: {ex.Message}");
            return Array.Empty<LyricLine>();
        }
    }

    private async Task<LrclibResponseDto?> FetchFromLrclibAsync(
        string title, string artist, string album, TimeSpan duration, CancellationToken ct)
    {
        // 1. Try exact match /api/get
        string getUrl = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        if (!string.IsNullOrWhiteSpace(album))
        {
            getUrl += $"&album_name={Uri.EscapeDataString(album)}";
        }
        if (duration > TimeSpan.Zero)
        {
            getUrl += $"&duration={(int)Math.Round(duration.TotalSeconds)}";
        }

        System.Diagnostics.Debug.WriteLine($"[LyricsService] GET {getUrl}");
        try
        {
            using var response = await _httpClient.GetAsync(getUrl, ct).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[LyricsService] /api/get responded {(int)response.StatusCode} {response.StatusCode}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"[LyricsService] /api/get body length={content.Length} bytes");
                var result = JsonSerializer.Deserialize<LrclibResponseDto>(content, JsonOptions);
                if (result != null && (!string.IsNullOrWhiteSpace(result.SyncedLyrics) || !string.IsNullOrWhiteSpace(result.PlainLyrics)))
                {
                    LogResponseCandidate("get", result);
                    bool queryHasJapanese = JapaneseCharRegex.IsMatch(title) || JapaneseCharRegex.IsMatch(artist);
                    string combined = (result.SyncedLyrics ?? "") + " " + (result.PlainLyrics ?? "");
                    bool resultHasJapanese = JapaneseCharRegex.IsMatch(combined);

                    if (!queryHasJapanese || resultHasJapanese || !string.IsNullOrWhiteSpace(result.SyncedLyrics))
                    {
                        System.Diagnostics.Debug.WriteLine($"[LyricsService] /api/get match accepted (track='{result.TrackName}', artist='{result.ArtistName}')");
                        return result;
                    }

                    System.Diagnostics.Debug.WriteLine("[LyricsService] /api/get match rejected: query has Japanese but result had neither Japanese lyrics nor synced timestamps");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[LyricsService] /api/get response had no usable lyrics");
                }
            }
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsService] /api/get HttpRequestException: {ex.Message}");
        }

        // 2. Search fallback /api/search
        string searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString($"{title} {artist}")}";
        System.Diagnostics.Debug.WriteLine($"[LyricsService] GET {searchUrl}");
        try
        {
            using var response = await _httpClient.GetAsync(searchUrl, ct).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[LyricsService] /api/search responded {(int)response.StatusCode} {response.StatusCode}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"[LyricsService] /api/search body length={content.Length} bytes");
                var searchResults = JsonSerializer.Deserialize<List<LrclibResponseDto>>(content, JsonOptions);
                if (searchResults != null && searchResults.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[LyricsService] /api/search returned {searchResults.Count} candidate(s):");
                    for (int i = 0; i < searchResults.Count; i++)
                    {
                        LogResponseCandidate($"search[{i}]", searchResults[i]);
                    }

                    var bestMatch = SelectBestLyricsMatch(searchResults, title, artist, duration);
                    if (bestMatch != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LyricsService] /api/search selected match (track='{bestMatch.TrackName}', artist='{bestMatch.ArtistName}')");
                        return bestMatch;
                    }

                    System.Diagnostics.Debug.WriteLine("[LyricsService] /api/search candidates were all rejected by SelectBestLyricsMatch");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[LyricsService] /api/search returned an empty result list");
                }
            }
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsService] /api/search HttpRequestException: {ex.Message}");
        }

        System.Diagnostics.Debug.WriteLine("[LyricsService] No lyrics found from lrclib");
        return null;
    }

    private static void LogResponseCandidate(string label, LrclibResponseDto dto)
    {
        bool hasSynced = !string.IsNullOrWhiteSpace(dto.SyncedLyrics);
        bool hasPlain = !string.IsNullOrWhiteSpace(dto.PlainLyrics);
        int syncedLineCount = hasSynced ? dto.SyncedLyrics!.Split('\n').Length : 0;
        int plainLineCount = hasPlain ? dto.PlainLyrics!.Split('\n').Length : 0;
        System.Diagnostics.Debug.WriteLine(
            $"[LyricsService]   {label} track='{dto.TrackName}' artist='{dto.ArtistName}' duration={dto.Duration}s synced={(hasSynced ? $"yes({syncedLineCount} lines)" : "no")} plain={(hasPlain ? $"yes({plainLineCount} lines)" : "no")}");
    }

    private async Task<IReadOnlyList<LyricLine>> ParseAndConvertLyricsAsync(
        LrclibResponseDto dto, CancellationToken ct)
    {
        var rawLines = new List<(TimeSpan timestamp, string text)>();

        if (!string.IsNullOrWhiteSpace(dto.SyncedLyrics))
        {
            rawLines = ParseLrc(dto.SyncedLyrics);
        }
        else if (!string.IsNullOrWhiteSpace(dto.PlainLyrics))
        {
            var plain = dto.PlainLyrics.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < plain.Length; i++)
            {
                var lineText = plain[i].Trim();
                if (!string.IsNullOrEmpty(lineText))
                {
                    rawLines.Add((TimeSpan.Zero, lineText));
                }
            }
        }

        var result = new List<LyricLine>(rawLines.Count);
        int index = 0;

        foreach (var (timestamp, text) in rawLines)
        {
            ct.ThrowIfCancellationRequested();
            string romaji = text;
            var segments = new List<FuriganaSegment>();

            if (JapaneseCharRegex.IsMatch(text))
            {
                try
                {
                    romaji = await ConvertToRomajiAsync(text).ConfigureAwait(false);
                    segments = await BuildFuriganaSegmentsAsync(text).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LyricsService] Kawazu conversion failed for '{text}': {ex.Message}");
                    segments.Add(new FuriganaSegment(text, string.Empty));
                }
            }
            else
            {
                segments.Add(new FuriganaSegment(text, string.Empty));
            }

            result.Add(new LyricLine(
                Index: index++,
                Timestamp: timestamp,
                Text: text,
                RomajiText: romaji,
                Segments: segments));
        }

        return result;
    }

    private static List<(TimeSpan timestamp, string text)> ParseLrc(string lrcContent)
    {
        var result = new List<(TimeSpan timestamp, string text)>();
        var lines = lrcContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var matches = LrcTimestampRegex.Matches(rawLine);
            if (matches.Count == 0) continue;

            string lineText = LrcTimestampRegex.Replace(rawLine, string.Empty).Trim();
            if (string.IsNullOrEmpty(lineText)) continue;

            foreach (Match m in matches)
            {
                int min = int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture);
                int sec = int.Parse(m.Groups["sec"].Value, CultureInfo.InvariantCulture);
                int ms = 0;

                if (m.Groups["ms"].Success)
                {
                    string msStr = m.Groups["ms"].Value;
                    if (msStr.Length == 2) ms = int.Parse(msStr, CultureInfo.InvariantCulture) * 10;
                    else if (msStr.Length == 3) ms = int.Parse(msStr, CultureInfo.InvariantCulture);
                }

                var timestamp = new TimeSpan(0, 0, min, sec, ms);
                result.Add((timestamp, lineText));
            }
        }

        result.Sort((a, b) => a.timestamp.CompareTo(b.timestamp));
        return result;
    }

    private KawazuConverter? GetKawazuConverter()
    {
        if (_kawazuInitAttempted) return _kawazuConverter;
        lock (_kawazuLock)
        {
            if (!_kawazuInitAttempted)
            {
                _kawazuInitAttempted = true;
                try
                {
                    _kawazuConverter = new KawazuConverter();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LyricsService] Failed to initialize KawazuConverter: {ex.Message}");
                    _kawazuConverter = null;
                }
            }
        }
        return _kawazuConverter;
    }

    private async Task<string> ConvertToRomajiAsync(string text)
    {
        var converter = GetKawazuConverter();
        if (converter is null) return text;
        return await converter.Convert(text, To.Romaji, Mode.Spaced).ConfigureAwait(false);
    }

    private async Task<List<FuriganaSegment>> BuildFuriganaSegmentsAsync(string text)
    {
        var segments = new List<FuriganaSegment>();
        var converter = GetKawazuConverter();

        // Kawazu converts mixed text to spaced romaji or hiragana readings
        // For segments, we can split text into words / kanji runs and convert each
        var words = text.Split(' ');
        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word)) continue;

            if (JapaneseCharRegex.IsMatch(word) && converter is not null)
            {
                string reading = await converter.Convert(word, To.Romaji, Mode.Spaced).ConfigureAwait(false);
                segments.Add(new FuriganaSegment(word, reading));
            }
            else
            {
                segments.Add(new FuriganaSegment(word, string.Empty));
            }
        }

        return segments;
    }

    internal static LrclibResponseDto? SelectBestLyricsMatch(
        IEnumerable<LrclibResponseDto> candidates,
        string queryTitle,
        string queryArtist,
        TimeSpan targetDuration)
    {
        LrclibResponseDto? bestItem = null;
        int maxScore = int.MinValue;

        bool queryHasJapanese = JapaneseCharRegex.IsMatch(queryTitle) || JapaneseCharRegex.IsMatch(queryArtist);

        foreach (var item in candidates)
        {
            if (string.IsNullOrWhiteSpace(item.SyncedLyrics) && string.IsNullOrWhiteSpace(item.PlainLyrics))
                continue;

            int score = 0;

            // 1. Synced lyrics preference (+100)
            if (!string.IsNullOrWhiteSpace(item.SyncedLyrics))
            {
                score += 100;
            }

            // 2. Japanese character presence
            string combinedLyrics = (item.SyncedLyrics ?? string.Empty) + " " + (item.PlainLyrics ?? string.Empty);
            bool lyricsHasJapanese = JapaneseCharRegex.IsMatch(combinedLyrics);
            bool metadataHasJapanese = (item.TrackName != null && JapaneseCharRegex.IsMatch(item.TrackName)) ||
                                       (item.ArtistName != null && JapaneseCharRegex.IsMatch(item.ArtistName));

            if (queryHasJapanese)
            {
                // Query has Japanese -> strongly prefer Japanese lyrics over English translation
                if (lyricsHasJapanese) score += 300;
                else score -= 100;

                if (metadataHasJapanese) score += 50;
            }
            else
            {
                // Query might be Romaji/English title for Japanese track -> give bonus if candidate lyrics have Japanese
                if (lyricsHasJapanese) score += 80;
            }

            // 3. Duration match
            if (targetDuration > TimeSpan.Zero && item.Duration.HasValue && item.Duration.Value > 0)
            {
                double diff = Math.Abs(item.Duration.Value - targetDuration.TotalSeconds);
                if (diff <= 2.0) score += 60;
                else if (diff <= 5.0) score += 40;
                else if (diff <= 10.0) score += 20;
                else if (diff > 20.0) score -= 50;
            }

            if (score > maxScore)
            {
                maxScore = score;
                bestItem = item;
            }
        }

        return bestItem;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal sealed record LrclibResponseDto(
        [property: JsonPropertyName("syncedLyrics")] string? SyncedLyrics,
        [property: JsonPropertyName("plainLyrics")] string? PlainLyrics,
        [property: JsonPropertyName("trackName")] string? TrackName,
        [property: JsonPropertyName("artistName")] string? ArtistName,
        [property: JsonPropertyName("duration")] double? Duration);
}
