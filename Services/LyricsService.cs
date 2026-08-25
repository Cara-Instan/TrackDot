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

    private static readonly Regex TtmlParagraphRegex = new(
        @"<p\b[^>]*\bbegin=""(?<begin>[^""]+)""[^>]*>(?<content>.*?)</p>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex TtmlTimestampRegex = new(
        @"^(?:(?:(?<hours>\d+):)?(?<min>\d+):(?<sec>\d{1,2})(?:\.(?<ms>\d+))?|(?<rawSec>\d+(?:\.\d+)?)(?:s)?|(?<rawMs>\d+)ms)$",
        RegexOptions.Compiled);

    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, IReadOnlyList<LyricLine>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private KawazuConverter? _kawazuConverter;
    private bool _kawazuInitAttempted;
    private readonly object _kawazuLock = new();

    public LyricsService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TrackDot/0.3.0 (https://github.com/herlandroando/TrackDot)");
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
            // 1. Try Unison (primary source)
            var rawLines = await FetchFromUnisonAsync(title, artist, album, duration, cancellationToken).ConfigureAwait(false);
            if (rawLines != null && rawLines.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[LyricsService] Using lyrics from Unison ({rawLines.Count} lines)");
                var lines = await ConvertRawLinesToLyricLinesAsync(rawLines, cancellationToken).ConfigureAwait(false);
                _cache[cacheKey] = lines;
                return lines;
            }

            // 2. Fallback to LRCLIB
            System.Diagnostics.Debug.WriteLine("[LyricsService] Unison returned no lyrics; falling back to LRCLIB");
            var lrclibDto = await FetchFromLrclibAsync(title, artist, album, duration, cancellationToken).ConfigureAwait(false);
            if (lrclibDto != null)
            {
                var lrclibRaw = ExtractLrclibRawLines(lrclibDto);
                if (lrclibRaw.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[LyricsService] Using lyrics from LRCLIB ({lrclibRaw.Count} lines)");
                    var lines = await ConvertRawLinesToLyricLinesAsync(lrclibRaw, cancellationToken).ConfigureAwait(false);
                    _cache[cacheKey] = lines;
                    return lines;
                }
            }

            _cache[cacheKey] = Array.Empty<LyricLine>();
            return Array.Empty<LyricLine>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsService] Error fetching lyrics: {ex.Message}");
            return Array.Empty<LyricLine>();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LyricsSearchResult>> SearchCandidatesAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<LyricsSearchResult>();

        string searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(query.Trim())}";
        try
        {
            using var response = await _httpClient.GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var searchResults = JsonSerializer.Deserialize<List<LrclibResponseDto>>(content, JsonOptions);
                if (searchResults != null)
                {
                    var results = new List<LyricsSearchResult>(searchResults.Count);
                    foreach (var item in searchResults)
                    {
                        bool hasSynced = !string.IsNullOrWhiteSpace(item.SyncedLyrics);
                        bool hasPlain = !string.IsNullOrWhiteSpace(item.PlainLyrics);
                        if (!hasSynced && !hasPlain) continue;

                        TimeSpan? dur = item.Duration.HasValue && item.Duration.Value > 0
                            ? TimeSpan.FromSeconds(item.Duration.Value)
                            : null;

                        results.Add(new LyricsSearchResult(
                            Id: item.Id,
                            TrackName: item.TrackName ?? "Unknown Track",
                            ArtistName: item.ArtistName ?? "Unknown Artist",
                            AlbumName: item.AlbumName,
                            Duration: dur,
                            HasSyncedLyrics: hasSynced,
                            HasPlainLyrics: hasPlain,
                            Source: "LRCLIB",
                            RawSyncedLyrics: item.SyncedLyrics,
                            RawPlainLyrics: item.PlainLyrics));
                    }
                    return results;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsService] Search error: {ex.Message}");
        }

        return Array.Empty<LyricsSearchResult>();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LyricLine>> FetchLyricsByResultAsync(
        LyricsSearchResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        string? synced = result.RawSyncedLyrics;
        string? plain = result.RawPlainLyrics;

        if (string.IsNullOrWhiteSpace(synced) && string.IsNullOrWhiteSpace(plain) && result.Id.HasValue)
        {
            try
            {
                string getUrl = $"https://lrclib.net/api/get/{result.Id.Value}";
                using var response = await _httpClient.GetAsync(getUrl, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var dto = JsonSerializer.Deserialize<LrclibResponseDto>(content, JsonOptions);
                    if (dto != null)
                    {
                        synced = dto.SyncedLyrics;
                        plain = dto.PlainLyrics;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[LyricsService] FetchByResult get error: {ex.Message}");
            }
        }

        var dtoMock = new LrclibResponseDto(
            Id: result.Id,
            SyncedLyrics: synced,
            PlainLyrics: plain,
            TrackName: result.TrackName,
            ArtistName: result.ArtistName,
            AlbumName: result.AlbumName,
            Duration: result.Duration?.TotalSeconds);

        var rawLines = ExtractLrclibRawLines(dtoMock);
        return await ConvertRawLinesToLyricLinesAsync(rawLines, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LyricLine>> ParseCustomLyricsAsync(
        string rawContent,
        string? format = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawContent)) return Array.Empty<LyricLine>();
        var rawLines = ParseRawLyrics(rawContent, format);
        return await ConvertRawLinesToLyricLinesAsync(rawLines, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void SaveLyricsToCache(
        string title,
        string artist,
        string album,
        IReadOnlyList<LyricLine> lyrics)
    {
        if (string.IsNullOrWhiteSpace(title) || lyrics == null) return;
        string cacheKey = $"{artist.Trim()} - {title.Trim()} - {album.Trim()}";
        _cache[cacheKey] = lyrics;
    }

    private async Task<List<RawLyricItem>?> FetchFromUnisonAsync(
        string title, string artist, string album, TimeSpan duration, CancellationToken ct)
    {
        string url = $"https://unison.boidu.dev/lyrics?song={Uri.EscapeDataString(title)}&artist={Uri.EscapeDataString(artist)}";
        if (!string.IsNullOrWhiteSpace(album))
        {
            url += $"&album={Uri.EscapeDataString(album)}";
        }
        if (duration > TimeSpan.Zero)
        {
            url += $"&duration={(int)Math.Round(duration.TotalSeconds)}";
        }

        System.Diagnostics.Debug.WriteLine($"[LyricsService] GET {url}");
        try
        {
            using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[LyricsService] Unison responded {(int)response.StatusCode} {response.StatusCode}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var envelope = JsonSerializer.Deserialize<UnisonEnvelopeDto>(content, JsonOptions);
                if (envelope is { Success: true, Data.Lyrics: not null } && !string.IsNullOrWhiteSpace(envelope.Data.Lyrics))
                {
                    var data = envelope.Data;
                    System.Diagnostics.Debug.WriteLine($"[LyricsService] Unison match accepted (song='{data.Song}', artist='{data.Artist}', format='{data.Format}', syncType='{data.SyncType}')");
                    return ParseRawLyrics(data.Lyrics, data.Format);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsService] Unison HttpRequestException: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsService] Unison unexpected error: {ex.Message}");
        }

        return null;
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

    internal static List<RawLyricItem> ExtractLrclibRawLines(LrclibResponseDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.SyncedLyrics))
        {
            return ParseLrc(dto.SyncedLyrics);
        }

        if (!string.IsNullOrWhiteSpace(dto.PlainLyrics))
        {
            var rawLines = new List<RawLyricItem>();
            var plain = dto.PlainLyrics.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < plain.Length; i++)
            {
                var lineText = plain[i].Trim();
                if (!string.IsNullOrEmpty(lineText))
                {
                    rawLines.Add(new RawLyricItem(TimeSpan.Zero, lineText));
                }
            }
            return rawLines;
        }

        return new List<RawLyricItem>();
    }

    internal static List<RawLyricItem> ParseRawLyrics(string lyrics, string? format = null)
    {
        if (string.IsNullOrWhiteSpace(lyrics)) return new List<RawLyricItem>();

        if (string.Equals(format, "ttml", StringComparison.OrdinalIgnoreCase) ||
            (lyrics.Contains("<tt", StringComparison.OrdinalIgnoreCase) && lyrics.Contains("</tt>", StringComparison.OrdinalIgnoreCase)))
        {
            var ttmlResult = ParseTtml(lyrics);
            if (ttmlResult.Count > 0) return ttmlResult;
        }

        if (string.Equals(format, "lrc", StringComparison.OrdinalIgnoreCase) || LrcTimestampRegex.IsMatch(lyrics))
        {
            var lrcResult = ParseLrc(lyrics);
            if (lrcResult.Count > 0) return lrcResult;
        }

        var result = new List<RawLyricItem>();
        var plain = lyrics.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in plain)
        {
            var lineText = line.Trim();
            if (!string.IsNullOrEmpty(lineText))
            {
                result.Add(new RawLyricItem(TimeSpan.Zero, lineText));
            }
        }
        return result;
    }

    internal static List<RawLyricItem> ParseTtml(string ttmlContent)
    {
        var rawMatches = new List<RawLyricItem>();
        if (string.IsNullOrWhiteSpace(ttmlContent)) return rawMatches;

        var matches = TtmlParagraphRegex.Matches(ttmlContent);
        foreach (Match match in matches)
        {
            string beginStr = match.Groups["begin"].Value;
            string rawContent = match.Groups["content"].Value;

            var timestamp = ParseTimestamp(beginStr);
            string text = HtmlTagRegex.Replace(rawContent, string.Empty);
            text = System.Net.WebUtility.HtmlDecode(text).Trim();
            text = Regex.Replace(text, @"\s+", " ");

            if (!string.IsNullOrEmpty(text))
            {
                rawMatches.Add(new RawLyricItem(timestamp, text));
            }
        }

        rawMatches.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return MergeConsecutiveBilingualLines(rawMatches);
    }

    internal static TimeSpan ParseTimestamp(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr)) return TimeSpan.Zero;
        timeStr = timeStr.Trim();

        var match = TtmlTimestampRegex.Match(timeStr);
        if (!match.Success)
        {
            if (double.TryParse(timeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double secs))
            {
                return TimeSpan.FromSeconds(secs);
            }
            return TimeSpan.Zero;
        }

        if (match.Groups["rawMs"].Success && int.TryParse(match.Groups["rawMs"].Value, CultureInfo.InvariantCulture, out int rawMs))
        {
            return TimeSpan.FromMilliseconds(rawMs);
        }

        if (match.Groups["rawSec"].Success && double.TryParse(match.Groups["rawSec"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double rawSec))
        {
            return TimeSpan.FromSeconds(rawSec);
        }

        int hours = match.Groups["hours"].Success ? int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture) : 0;
        int min = match.Groups["min"].Success ? int.Parse(match.Groups["min"].Value, CultureInfo.InvariantCulture) : 0;
        int sec = match.Groups["sec"].Success ? int.Parse(match.Groups["sec"].Value, CultureInfo.InvariantCulture) : 0;
        int ms = 0;
        if (match.Groups["ms"].Success)
        {
            string msStr = match.Groups["ms"].Value;
            if (msStr.Length == 1) ms = int.Parse(msStr, CultureInfo.InvariantCulture) * 100;
            else if (msStr.Length == 2) ms = int.Parse(msStr, CultureInfo.InvariantCulture) * 10;
            else if (msStr.Length == 3) ms = int.Parse(msStr, CultureInfo.InvariantCulture);
            else if (msStr.Length > 3) ms = int.Parse(msStr.Substring(0, 3), CultureInfo.InvariantCulture);
        }

        return new TimeSpan(0, hours, min, sec, ms);
    }

    private async Task<IReadOnlyList<LyricLine>> ConvertRawLinesToLyricLinesAsync(
        IReadOnlyList<RawLyricItem> rawLines, CancellationToken ct)
    {
        var result = new List<LyricLine>(rawLines.Count);
        int index = 0;

        foreach (var item in rawLines)
        {
            ct.ThrowIfCancellationRequested();
            string text = item.Text;
            string? translation = item.Translation;
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
                Timestamp: item.Timestamp,
                Text: text,
                RomajiText: romaji,
                Segments: segments,
                Translation: translation));
        }

        return result;
    }

    internal static List<RawLyricItem> ParseLrc(string lrcContent)
    {
        var unmerged = new List<RawLyricItem>();
        var lines = lrcContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var matches = LrcTimestampRegex.Matches(rawLine);
            if (matches.Count == 0) continue;

            string lineText = LrcTimestampRegex.Replace(rawLine, string.Empty).Trim();
            if (string.IsNullOrEmpty(lineText)) continue;

            // Check for inline delimiter translations: e.g. "Original // Translation" or "Original | Translation"
            string text = lineText;
            string? translation = null;

            if (lineText.Contains(" // "))
            {
                var parts = lineText.Split(new[] { " // " }, 2, StringSplitOptions.None);
                text = parts[0].Trim();
                translation = parts[1].Trim();
            }
            else if (lineText.Contains(" | "))
            {
                var parts = lineText.Split(new[] { " | " }, 2, StringSplitOptions.None);
                text = parts[0].Trim();
                translation = parts[1].Trim();
            }

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
                unmerged.Add(new RawLyricItem(timestamp, text, translation));
            }
        }

        unmerged.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return MergeConsecutiveBilingualLines(unmerged);
    }

    private static List<RawLyricItem> MergeConsecutiveBilingualLines(List<RawLyricItem> items)
    {
        if (items.Count <= 1) return items;

        var merged = new List<RawLyricItem>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var current = items[i];
            if (current.Translation == null && i + 1 < items.Count)
            {
                var next = items[i + 1];
                // If timestamps are identical or within 150ms and next has no separate translation
                if (next.Translation == null && Math.Abs((next.Timestamp - current.Timestamp).TotalMilliseconds) <= 150)
                {
                    // Check if one has Japanese/CJK and other is Latin translation, or simply two paired lines
                    merged.Add(new RawLyricItem(current.Timestamp, current.Text, next.Text));
                    i++; // Skip the paired next line
                    continue;
                }
            }
            merged.Add(current);
        }

        return merged;
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

    internal sealed record RawLyricItem(TimeSpan Timestamp, string Text, string? Translation = null);

    internal sealed record UnisonEnvelopeDto(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("data")] UnisonDataDto? Data,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("hint")] string? Hint);

    internal sealed record UnisonDataDto(
        [property: JsonPropertyName("id")] int? Id,
        [property: JsonPropertyName("videoId")] string? VideoId,
        [property: JsonPropertyName("song")] string? Song,
        [property: JsonPropertyName("artist")] string? Artist,
        [property: JsonPropertyName("album")] string? Album,
        [property: JsonPropertyName("duration")] double? Duration,
        [property: JsonPropertyName("lyrics")] string? Lyrics,
        [property: JsonPropertyName("format")] string? Format,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("syncType")] string? SyncType,
        [property: JsonPropertyName("confidence")] string? Confidence);

    internal sealed record LrclibResponseDto(
        [property: JsonPropertyName("id")] int? Id,
        [property: JsonPropertyName("syncedLyrics")] string? SyncedLyrics,
        [property: JsonPropertyName("plainLyrics")] string? PlainLyrics,
        [property: JsonPropertyName("trackName")] string? TrackName,
        [property: JsonPropertyName("artistName")] string? ArtistName,
        [property: JsonPropertyName("albumName")] string? AlbumName,
        [property: JsonPropertyName("duration")] double? Duration);
}

