using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TrackDot.Models;
using TrackDot.ViewModels;

namespace TrackDot.Services;

/// <summary>
/// Service managing Discord Rich Presence lifecycle, application discovery,
/// privacy filtering, album artwork resolution, and periodic updates.
/// </summary>
public sealed class DiscordRpcService : IDiscordRpcService
{
    public const string DefaultDiscordClientId = "1541730552602693632";

    private static readonly JsonSerializerOptions ActivityJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IMediaControllerService _mediaController;
    private readonly IWindowSettingsService _settings;
    private readonly IDiscordIpcClient _client;
    private readonly IArtworkLookupService _artworkLookupService;
    private readonly string _clientId;

    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private string? _lastActivityHash;
    private DateTime _lastSentTime = DateTime.MinValue;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsConnected => _client.IsConnected;

    public DiscordRpcService(
        IMediaControllerService mediaController,
        IWindowSettingsService settings,
        IDiscordIpcClient? client = null,
        string? clientId = null,
        IArtworkLookupService? artworkLookupService = null)
    {
        _mediaController = mediaController ?? throw new ArgumentNullException(nameof(mediaController));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _client = client ?? new DiscordNamedPipeIpcClient();
        _artworkLookupService = artworkLookupService ?? new ArtworkLookupService();

        var envClientId = Environment.GetEnvironmentVariable("TRACKDOT_DISCORD_CLIENT_ID");
        _clientId = !string.IsNullOrWhiteSpace(clientId)
            ? clientId
            : (!string.IsNullOrWhiteSpace(envClientId) ? envClientId : DefaultDiscordClientId);

        _mediaController.SnapshotChanged += OnSnapshotChanged;
        _settings.SettingsChanged += OnSettingsChanged;

        // Process current session state immediately
        _ = UpdatePresenceAsync(_mediaController.Current);
    }

    private void OnSnapshotChanged(object? sender, MediaSessionSnapshot snapshot)
    {
        _ = UpdatePresenceAsync(snapshot);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _ = UpdatePresenceAsync(_mediaController.Current);
    }

    /// <inheritdoc/>
    public async Task UpdatePresenceAsync(MediaSessionSnapshot session)
    {
        if (_disposed) return;

        await _updateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // 1. Master Privacy Switch: if disabled, clear & disconnect
            if (!_settings.EnableDiscordRpc)
            {
                if (_client.IsConnected)
                {
                    await _client.ClearActivityAsync().ConfigureAwait(false);
                    _client.Close();
                }
                _lastActivityHash = null;
                return;
            }

            // 2. Empty / invalid session: clear presence
            if (session == null || session == MediaSessionSnapshot.Empty || string.IsNullOrWhiteSpace(session.Title))
            {
                if (_client.IsConnected)
                {
                    await _client.ClearActivityAsync().ConfigureAwait(false);
                }
                _lastActivityHash = null;
                return;
            }

            // 3. Dynamic Source App Discovery & Registration
            var aumid = session.SourceAppUserModelId ?? "UnknownApp";
            var displayName = MainViewModelHelpers.FormatAppName(aumid);
            if (string.IsNullOrWhiteSpace(displayName)) displayName = aumid;

            // Auto-registers the app with default (DiscordRpcEnabled = false) if not already registered
            var registeredApp = _settings.RegisterOrUpdateSourceApp(aumid, displayName);

            // 4. Per-App Privacy Filter: if this app is not enabled, do not broadcast
            if (!registeredApp.DiscordRpcEnabled)
            {
                if (_client.IsConnected)
                {
                    await _client.ClearActivityAsync().ConfigureAwait(false);
                }
                _lastActivityHash = null;
                return;
            }

            // 5. Resolve album artwork URL (iTunes / Deezer)
            string? artworkUrl = null;
            if (!string.IsNullOrWhiteSpace(session.Title))
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    artworkUrl = await _artworkLookupService.GetArtworkUrlAsync(
                        session.Title,
                        session.Artist,
                        session.AlbumTitle,
                        cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    artworkUrl = null;
                }
            }

            // 6. Build Discord Activity based on Playback state
            var activity = BuildActivity(session, artworkUrl);
            if (activity == null)
            {
                if (_client.IsConnected)
                {
                    await _client.ClearActivityAsync().ConfigureAwait(false);
                }
                _lastActivityHash = null;
                return;
            }

            // 7. Compute hash to avoid duplicate frames
            var activityJson = JsonSerializer.Serialize(activity, ActivityJsonOptions);
            if (_client.IsConnected && _lastActivityHash == activityJson && (DateTime.UtcNow - _lastSentTime).TotalSeconds < 15)
            {
                return;
            }

            // 8. Ensure IPC connection
            if (!_client.IsConnected)
            {
                var connected = await _client.ConnectAsync(_clientId).ConfigureAwait(false);
                if (!connected) return;
            }

            // 9. Send Activity
            await _client.SendSetActivityAsync(activity).ConfigureAwait(false);
            _lastActivityHash = activityJson;
            _lastSentTime = DateTime.UtcNow;
        }
        catch
        {
            // Fail silently without crashing or interrupting media playback
            _lastActivityHash = null;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private object? BuildActivity(MediaSessionSnapshot session, string? artworkUrl)
    {
        var playback = session.Playback;
        if (playback.State == MediaPlaybackState.Stopped || playback.State == MediaPlaybackState.None)
        {
            return null;
        }

        var largeImage = !string.IsNullOrWhiteSpace(artworkUrl) ? artworkUrl : "trackdot_logo";
        var (smallImage, smallText) = ResolveSourceAppBadge(session.SourceAppUserModelId, isPlaying: playback.State == MediaPlaybackState.Playing);

        if (playback.State == MediaPlaybackState.Paused)
        {
            if (!_settings.DiscordShowPauseStatus)
            {
                return null;
            }

            var pauseStateText = string.IsNullOrWhiteSpace(session.Artist) ? "Paused" : $"Paused • {Truncate(session.Artist, 100)}";
            var pauseAlbumText = _settings.DiscordShowAlbum && !string.IsNullOrWhiteSpace(session.AlbumTitle)
                ? Truncate(session.AlbumTitle, 120)
                : (string.IsNullOrWhiteSpace(session.Title) ? "TrackDot" : Truncate(session.Title, 120));

            return new
            {
                type = 2,
                details = Truncate(session.Title, 120),
                state = pauseStateText,
                assets = new
                {
                    large_image = largeImage,
                    large_text = pauseAlbumText,
                    small_image = smallImage,
                    small_text = smallText
                },
                instance = false
            };
        }

        if (playback.State == MediaPlaybackState.Playing)
        {
            var artistText = string.IsNullOrWhiteSpace(session.Artist) ? "TrackDot" : Truncate(session.Artist, 100);
            var albumText = _settings.DiscordShowAlbum && !string.IsNullOrWhiteSpace(session.AlbumTitle)
                ? Truncate(session.AlbumTitle, 120)
                : (string.IsNullOrWhiteSpace(session.Title) ? "TrackDot" : Truncate(session.Title, 120));

            object? timestamps = null;
            if (_settings.DiscordShowTimestamps && playback.EndTime > TimeSpan.Zero)
            {
                var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var startEpoch = nowEpoch - (long)playback.Position.TotalSeconds;
                var endEpoch = startEpoch + (long)playback.EndTime.TotalSeconds;

                if (endEpoch > startEpoch)
                {
                    timestamps = new
                    {
                        start = startEpoch,
                        end = endEpoch
                    };
                }
            }

            return new
            {
                type = 2,
                details = Truncate(session.Title, 120),
                state = artistText,
                timestamps,
                assets = new
                {
                    large_image = largeImage,
                    large_text = albumText,
                    small_image = smallImage,
                    small_text = smallText
                },
                instance = false
            };
        }

        return null;
    }

    public static (string Image, string Text) ResolveSourceAppBadge(string? aumid, bool isPlaying)
    {
        var lower = (aumid ?? string.Empty).ToLowerInvariant();
        string statusSuffix = isPlaying ? string.Empty : " (Paused)";

        if (lower.Contains("applemusic") || lower.Contains("appleinc") || lower.Contains("itunes"))
        {
            return ("https://upload.wikimedia.org/wikipedia/commons/thumb/5/5f/Apple_Music_icon.svg/512px-Apple_Music_icon.svg.png", $"Apple Music{statusSuffix}");
        }
        if (lower.Contains("spotify"))
        {
            return ("https://upload.wikimedia.org/wikipedia/commons/thumb/1/19/Spotify_logo_without_text.svg/512px-Spotify_logo_without_text.svg.png", $"Spotify{statusSuffix}");
        }
        if (lower.Contains("youtubemusic") || lower.Contains("youtube"))
        {
            return ("https://upload.wikimedia.org/wikipedia/commons/thumb/6/6a/Youtube_Music_icon.svg/512px-Youtube_Music_icon.svg.png", $"YouTube Music{statusSuffix}");
        }
        if (lower.Contains("chrome") || lower.Contains("msedge") || lower.Contains("firefox") || lower.Contains("brave") || lower.Contains("opera"))
        {
            return ("https://upload.wikimedia.org/wikipedia/commons/thumb/6/6a/Youtube_Music_icon.svg/512px-Youtube_Music_icon.svg.png", $"Web Player{statusSuffix}");
        }
        if (lower.Contains("tidal"))
        {
            return ("https://upload.wikimedia.org/wikipedia/commons/thumb/0/07/Tidal_logo.svg/512px-Tidal_logo.svg.png", $"TIDAL{statusSuffix}");
        }
        if (lower.Contains("amazonmusic"))
        {
            return ("https://upload.wikimedia.org/wikipedia/commons/thumb/d/d3/Amazon_Music_logo.svg/512px-Amazon_Music_logo.svg.png", $"Amazon Music{statusSuffix}");
        }
        if (lower.Contains("deezer"))
        {
            return ("https://upload.wikimedia.org/wikipedia/commons/thumb/d/db/Deezer_logo.svg/512px-Deezer_logo.svg.png", $"Deezer{statusSuffix}");
        }
        if (lower.Contains("soundcloud"))
        {
            return ("https://upload.wikimedia.org/wikipedia/commons/thumb/a/a2/Antu_soundcloud.svg/512px-Antu_soundcloud.svg.png", $"SoundCloud{statusSuffix}");
        }

        return (isPlaying ? "play" : "pause", isPlaying ? "TrackDot" : "Paused");
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _mediaController.SnapshotChanged -= OnSnapshotChanged;
        _settings.SettingsChanged -= OnSettingsChanged;

        try
        {
            if (_client.IsConnected)
            {
                _client.ClearActivityAsync().GetAwaiter().GetResult();
            }
        }
        catch { }

        _client.Dispose();
        _updateLock.Dispose();
    }
}
