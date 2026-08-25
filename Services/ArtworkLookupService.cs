using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TrackDot.Services;

/// <summary>
/// Fetches and caches high-resolution album artwork URLs from online providers
/// (iTunes Search API and Deezer API) for Discord Rich Presence.
/// </summary>
public class ArtworkLookupService : IArtworkLookupService
{
    private static readonly Regex CleanPatternRegex = new(
        @"\s*(\(|\[)(?:feat\.?|ft\.?|remastered|official|video|audio|lyrics|deluxe|version).*?(\)|\])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ArtworkLookupService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TrackDot/0.3.0 (https://github.com/herlandroando/TrackDot)");
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GetArtworkUrlAsync(
        string title,
        string artist,
        string album = "",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string cacheKey = $"{artist.Trim()} - {title.Trim()} - {album.Trim()}";
        if (_cache.TryGetValue(cacheKey, out var cachedUrl))
        {
            return cachedUrl;
        }

        try
        {
            // 1. Try iTunes Search API with full title and artist
            string query = string.IsNullOrWhiteSpace(artist) ? title : $"{title} {artist}";
            var itunesUrl = await FetchFromItunesAsync(query, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(itunesUrl))
            {
                _cache[cacheKey] = itunesUrl;
                return itunesUrl;
            }

            // 2. If title has noise like (feat. ...), clean it and retry iTunes
            var cleanedTitle = CleanPatternRegex.Replace(title, string.Empty).Trim();
            if (!string.Equals(cleanedTitle, title, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(cleanedTitle))
            {
                string cleanedQuery = string.IsNullOrWhiteSpace(artist) ? cleanedTitle : $"{cleanedTitle} {artist}";
                itunesUrl = await FetchFromItunesAsync(cleanedQuery, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(itunesUrl))
                {
                    _cache[cacheKey] = itunesUrl;
                    return itunesUrl;
                }
            }

            // 3. Fallback to Deezer Search API
            var deezerUrl = await FetchFromDeezerAsync(query, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(deezerUrl))
            {
                _cache[cacheKey] = deezerUrl;
                return deezerUrl;
            }

            // 4. Try Deezer with cleaned title if applicable
            if (!string.Equals(cleanedTitle, title, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(cleanedTitle))
            {
                string cleanedQuery = string.IsNullOrWhiteSpace(artist) ? cleanedTitle : $"{cleanedTitle} {artist}";
                deezerUrl = await FetchFromDeezerAsync(cleanedQuery, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(deezerUrl))
                {
                    _cache[cacheKey] = deezerUrl;
                    return deezerUrl;
                }
            }

            // If an album title is present and distinct from track title, try album search
            if (!string.IsNullOrWhiteSpace(album) && !string.IsNullOrWhiteSpace(artist) &&
                !string.Equals(album, title, StringComparison.OrdinalIgnoreCase))
            {
                string albumQuery = $"{album} {artist}";
                itunesUrl = await FetchFromItunesAsync(albumQuery, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(itunesUrl))
                {
                    _cache[cacheKey] = itunesUrl;
                    return itunesUrl;
                }
            }

            // No artwork found — cache miss so we don't spam requests
            _cache[cacheKey] = null;
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtworkLookupService] Error fetching artwork: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> FetchFromItunesAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query.Trim())}&entity=song&limit=5";
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var root = JsonSerializer.Deserialize<ItunesSearchResponseDto>(content, JsonOptions);
            if (root?.Results != null && root.Results.Count > 0)
            {
                foreach (var item in root.Results)
                {
                    if (!string.IsNullOrWhiteSpace(item.ArtworkUrl100))
                    {
                        // Upgrade to high-res (600x600)
                        return UpgradeItunesArtworkResolution(item.ArtworkUrl100);
                    }
                    if (!string.IsNullOrWhiteSpace(item.ArtworkUrl60))
                    {
                        return UpgradeItunesArtworkResolution(item.ArtworkUrl60);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtworkLookupService] iTunes fetch error: {ex.Message}");
        }

        return null;
    }

    private static string UpgradeItunesArtworkResolution(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        // Replaces 100x100bb, 60x60bb, etc. with 600x600bb
        return Regex.Replace(url, @"\d+x\d+bb", "600x600bb");
    }

    private async Task<string?> FetchFromDeezerAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"https://api.deezer.com/search?q={Uri.EscapeDataString(query.Trim())}&limit=5";
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var root = JsonSerializer.Deserialize<DeezerSearchResponseDto>(content, JsonOptions);
            if (root?.Data != null && root.Data.Count > 0)
            {
                foreach (var item in root.Data)
                {
                    if (item.Album != null)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Album.CoverXl)) return item.Album.CoverXl;
                        if (!string.IsNullOrWhiteSpace(item.Album.CoverBig)) return item.Album.CoverBig;
                        if (!string.IsNullOrWhiteSpace(item.Album.CoverMedium)) return item.Album.CoverMedium;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtworkLookupService] Deezer fetch error: {ex.Message}");
        }

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record ItunesSearchResponseDto(
        [property: JsonPropertyName("resultCount")] int ResultCount,
        [property: JsonPropertyName("results")] List<ItunesTrackDto>? Results);

    private sealed record ItunesTrackDto(
        [property: JsonPropertyName("trackName")] string? TrackName,
        [property: JsonPropertyName("artistName")] string? ArtistName,
        [property: JsonPropertyName("artworkUrl100")] string? ArtworkUrl100,
        [property: JsonPropertyName("artworkUrl60")] string? ArtworkUrl60);

    private sealed record DeezerSearchResponseDto(
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("data")] List<DeezerTrackDto>? Data);

    private sealed record DeezerTrackDto(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("album")] DeezerAlbumDto? Album);

    private sealed record DeezerAlbumDto(
        [property: JsonPropertyName("cover_xl")] string? CoverXl,
        [property: JsonPropertyName("cover_big")] string? CoverBig,
        [property: JsonPropertyName("cover_medium")] string? CoverMedium);
}

