using System;
using System.Text.Json;
using System.Threading.Tasks;
using TrackDot.Models;
using TrackDot.Services;
using TrackDot.Tests.Fakes;
using Xunit;

namespace TrackDot.Tests;

public class DiscordRpcServiceTests
{
    private static PlaybackSnapshot CreatePlayback(MediaPlaybackState state, double positionSec, double durationSec)
    {
        return new PlaybackSnapshot(
            State: state,
            Position: TimeSpan.FromSeconds(positionSec),
            StartTime: TimeSpan.Zero,
            EndTime: TimeSpan.FromSeconds(durationSec),
            TimelineUpdatedAt: DateTimeOffset.UtcNow,
            Capabilities: new TransportCapabilities(true, true, true, true, true, true, true, true));
    }

    [Fact]
    public async Task UpdatePresenceAsync_WhenMasterToggleDisabled_ClearsAndDisconnects()
    {
        var mediaService = new FakeMediaControllerService();
        var settings = new WindowSettingsService(
            initialEnableDiscordRpc: false,
            initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());
        var mockClient = new MockDiscordIpcClient { IsConnected = true };

        using var service = new DiscordRpcService(mediaService, settings, mockClient);

        var session = new MediaSessionSnapshot(
            SourceAppUserModelId: "Spotify.exe",
            Title: "Test Song",
            Artist: "Test Artist",
            AlbumTitle: "Test Album",
            Artwork: null,
            Playback: CreatePlayback(MediaPlaybackState.Playing, 10, 200));

        await service.UpdatePresenceAsync(session);

        Assert.True(mockClient.ClearCount > 0);
        Assert.False(mockClient.IsConnected);
    }

    [Fact]
    public async Task UpdatePresenceAsync_WhenNewAppDiscovered_AutoRegistersAsDisabledAndDoesNotBroadcast()
    {
        var mediaService = new FakeMediaControllerService();
        var settings = new WindowSettingsService(
            initialEnableDiscordRpc: true,
            initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());
        var mockClient = new MockDiscordIpcClient();

        using var service = new DiscordRpcService(mediaService, settings, mockClient);

        var session = new MediaSessionSnapshot(
            SourceAppUserModelId: "chrome.exe",
            Title: "Secret Video",
            Artist: "YouTuber",
            AlbumTitle: "",
            Artwork: null,
            Playback: CreatePlayback(MediaPlaybackState.Playing, 5, 60));

        await service.UpdatePresenceAsync(session);

        // Verify app was registered in settings
        var registered = Assert.Single(settings.RegisteredSourceApps);
        Assert.Equal("chrome.exe", registered.Aumid);
        Assert.False(registered.DiscordRpcEnabled); // Privacy default: Disabled

        // Verify no activity was sent
        Assert.Empty(mockClient.SentActivities);
    }

    [Fact]
    public async Task UpdatePresenceAsync_WhenAppExplicitlyAllowed_BroadcastsLivePresence()
    {
        var mediaService = new FakeMediaControllerService();
        var settings = new WindowSettingsService(
            initialEnableDiscordRpc: true,
            initialDiscordShowTimestamps: true,
            initialDiscordShowAlbum: true,
            initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());

        // Pre-register and allow Spotify
        settings.RegisterOrUpdateSourceApp("Spotify.exe", "Spotify");
        settings.SetSourceAppDiscordEnabled("Spotify.exe", true);

        var mockClient = new MockDiscordIpcClient();
        using var service = new DiscordRpcService(mediaService, settings, mockClient);

        var session = new MediaSessionSnapshot(
            SourceAppUserModelId: "Spotify.exe",
            Title: "Song Title",
            Artist: "Artist Name",
            AlbumTitle: "Great Album",
            Artwork: null,
            Playback: CreatePlayback(MediaPlaybackState.Playing, 30, 180));

        await service.UpdatePresenceAsync(session);

        Assert.True(mockClient.IsConnected);
        Assert.NotEmpty(mockClient.SentActivities);

        var lastActivity = mockClient.SentActivities[^1];
        Assert.NotNull(lastActivity);

        var json = JsonSerializer.Serialize(lastActivity);
        Assert.Contains("\"type\":2", json);
        Assert.Contains("Song Title", json);
        Assert.Contains("Artist Name", json);
        Assert.Contains("Great Album", json);
        Assert.Contains("\"start\":", json);
        Assert.Contains("\"end\":", json);
    }

    [Fact]
    public async Task UpdatePresenceAsync_WhenPausedAndPauseStatusEnabled_BroadcastsPausedState()
    {
        var mediaService = new FakeMediaControllerService();
        var settings = new WindowSettingsService(
            initialEnableDiscordRpc: true,
            initialDiscordShowPauseStatus: true,
            initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());

        settings.RegisterOrUpdateSourceApp("Spotify.exe", "Spotify");
        settings.SetSourceAppDiscordEnabled("Spotify.exe", true);

        var mockClient = new MockDiscordIpcClient();
        using var service = new DiscordRpcService(mediaService, settings, mockClient);

        var session = new MediaSessionSnapshot(
            SourceAppUserModelId: "Spotify.exe",
            Title: "Paused Song",
            Artist: "Some Artist",
            AlbumTitle: "Album",
            Artwork: null,
            Playback: CreatePlayback(MediaPlaybackState.Paused, 50, 200));

        await service.UpdatePresenceAsync(session);

        var lastActivity = mockClient.SentActivities[^1];
        Assert.NotNull(lastActivity);

        var json = JsonSerializer.Serialize(lastActivity);
        Assert.Contains("\"type\":2", json);
        Assert.Contains("Paused Song", json);
        Assert.Contains("Some Artist", json);
        Assert.Contains("Paused", json);
    }

    [Fact]
    public async Task UpdatePresenceAsync_WhenPausedAndPauseStatusDisabled_ClearsPresence()
    {
        var mediaService = new FakeMediaControllerService();
        var settings = new WindowSettingsService(
            initialEnableDiscordRpc: true,
            initialDiscordShowPauseStatus: false,
            initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());

        settings.RegisterOrUpdateSourceApp("Spotify.exe", "Spotify");
        settings.SetSourceAppDiscordEnabled("Spotify.exe", true);

        var mockClient = new MockDiscordIpcClient { IsConnected = true };
        using var service = new DiscordRpcService(mediaService, settings, mockClient);

        var session = new MediaSessionSnapshot(
            SourceAppUserModelId: "Spotify.exe",
            Title: "Paused Song",
            Artist: "Some Artist",
            AlbumTitle: "Album",
            Artwork: null,
            Playback: CreatePlayback(MediaPlaybackState.Paused, 50, 200));

        await service.UpdatePresenceAsync(session);

        Assert.True(mockClient.ClearCount > 0);
    }

    [Fact]
    public async Task UpdatePresenceAsync_WhenSwitchingToDisabledApp_ClearsActivity()
    {
        var mediaService = new FakeMediaControllerService();
        var settings = new WindowSettingsService(
            initialEnableDiscordRpc: true,
            initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());

        settings.RegisterOrUpdateSourceApp("Spotify.exe", "Spotify");
        settings.SetSourceAppDiscordEnabled("Spotify.exe", true);

        settings.RegisterOrUpdateSourceApp("vlc.exe", "VLC");
        settings.SetSourceAppDiscordEnabled("vlc.exe", false);

        var mockClient = new MockDiscordIpcClient();
        using var service = new DiscordRpcService(mediaService, settings, mockClient);

        // 1. Play Spotify (allowed)
        var spotifySession = new MediaSessionSnapshot(
            SourceAppUserModelId: "Spotify.exe",
            Title: "Track A",
            Artist: "Artist A",
            AlbumTitle: "Album A",
            Artwork: null,
            Playback: CreatePlayback(MediaPlaybackState.Playing, 10, 100));

        await service.UpdatePresenceAsync(spotifySession);
        Assert.NotNull(mockClient.SentActivities[^1]);

        // 2. Switch to VLC (blocked)
        var vlcSession = new MediaSessionSnapshot(
            SourceAppUserModelId: "vlc.exe",
            Title: "Private Video",
            Artist: "",
            AlbumTitle: "",
            Artwork: null,
            Playback: CreatePlayback(MediaPlaybackState.Playing, 10, 100));

        await service.UpdatePresenceAsync(vlcSession);
        Assert.True(mockClient.ClearCount > 0);
    }

    [Fact]
    public async Task UpdatePresenceAsync_UsesDefaultClientId_WhenNoneProvided()
    {
        var mediaService = new FakeMediaControllerService();
        var settings = new WindowSettingsService(
            initialEnableDiscordRpc: true,
            initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());
        settings.RegisterOrUpdateSourceApp("Spotify.exe", "Spotify");
        settings.SetSourceAppDiscordEnabled("Spotify.exe", true);

        var mockClient = new MockDiscordIpcClient();
        using var service = new DiscordRpcService(mediaService, settings, mockClient);

        var session = new MediaSessionSnapshot(
            SourceAppUserModelId: "Spotify.exe",
            Title: "Song",
            Artist: "Artist",
            AlbumTitle: "Album",
            Artwork: null,
            Playback: CreatePlayback(MediaPlaybackState.Playing, 1, 100));

        await service.UpdatePresenceAsync(session);

        Assert.Equal(DiscordRpcService.DefaultDiscordClientId, mockClient.LastClientId);
    }

    [Fact]
    public async Task UpdatePresenceAsync_UsesExplicitClientId_WhenProvided()
    {
        var mediaService = new FakeMediaControllerService();
        var settings = new WindowSettingsService(
            initialEnableDiscordRpc: true,
            initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());
        settings.RegisterOrUpdateSourceApp("Spotify.exe", "Spotify");
        settings.SetSourceAppDiscordEnabled("Spotify.exe", true);

        var mockClient = new MockDiscordIpcClient();
        const string customId = "999999999999999999";
        using var service = new DiscordRpcService(mediaService, settings, mockClient, clientId: customId);

        var session = new MediaSessionSnapshot(
            SourceAppUserModelId: "Spotify.exe",
            Title: "Song",
            Artist: "Artist",
            AlbumTitle: "Album",
            Artwork: null,
            Playback: CreatePlayback(MediaPlaybackState.Playing, 1, 100));

        await service.UpdatePresenceAsync(session);

        Assert.Equal(customId, mockClient.LastClientId);
    }

    [Fact]
    public async Task UpdatePresenceAsync_UsesEnvironmentVariable_WhenEnvVarSet()
    {
        const string envKey = "TRACKDOT_DISCORD_CLIENT_ID";
        const string envId = "888888888888888888";
        var originalEnv = Environment.GetEnvironmentVariable(envKey);

        try
        {
            Environment.SetEnvironmentVariable(envKey, envId);

            var mediaService = new FakeMediaControllerService();
            var settings = new WindowSettingsService(
                initialEnableDiscordRpc: true,
                initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());
            settings.RegisterOrUpdateSourceApp("Spotify.exe", "Spotify");
            settings.SetSourceAppDiscordEnabled("Spotify.exe", true);

            var mockClient = new MockDiscordIpcClient();
            using var service = new DiscordRpcService(mediaService, settings, mockClient);

            var session = new MediaSessionSnapshot(
                SourceAppUserModelId: "Spotify.exe",
                Title: "Song",
                Artist: "Artist",
                AlbumTitle: "Album",
                Artwork: null,
                Playback: CreatePlayback(MediaPlaybackState.Playing, 1, 100));

            await service.UpdatePresenceAsync(session);

            Assert.Equal(envId, mockClient.LastClientId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, originalEnv);
        }
    }

    private class FakeArtworkLookupService : IArtworkLookupService
    {
        private readonly string? _url;
        public FakeArtworkLookupService(string? url) => _url = url;
        public Task<string?> GetArtworkUrlAsync(string title, string artist, string album = "", System.Threading.CancellationToken cancellationToken = default)
            => Task.FromResult(_url);
    }

    [Fact]
    public async Task UpdatePresenceAsync_WhenArtworkAvailable_UsesResolvedArtworkUrlAndSourceBadge()
    {
        var mediaService = new FakeMediaControllerService();
        var settings = new WindowSettingsService(
            initialEnableDiscordRpc: true,
            initialDiscordShowTimestamps: true,
            initialDiscordShowAlbum: true,
            initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());

        settings.RegisterOrUpdateSourceApp("AppleInc.AppleMusicWin_8wekyb3d8bbwe!App", "Apple Music");
        settings.SetSourceAppDiscordEnabled("AppleInc.AppleMusicWin_8wekyb3d8bbwe!App", true);

        var mockClient = new MockDiscordIpcClient();
        var fakeArtwork = new FakeArtworkLookupService("https://example.com/lemonade.jpg");
        using var service = new DiscordRpcService(mediaService, settings, mockClient, artworkLookupService: fakeArtwork);

        var session = new MediaSessionSnapshot(
            SourceAppUserModelId: "AppleInc.AppleMusicWin_8wekyb3d8bbwe!App",
            Title: "Love Drought",
            Artist: "Beyoncé",
            AlbumTitle: "Lemonade",
            Artwork: null,
            Playback: CreatePlayback(MediaPlaybackState.Playing, 70, 205));

        await service.UpdatePresenceAsync(session);

        Assert.NotEmpty(mockClient.SentActivities);
        var lastActivity = mockClient.SentActivities[^1];
        var json = JsonSerializer.Serialize(lastActivity, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        Assert.Contains("https://example.com/lemonade.jpg", json);
        Assert.Contains("Apple Music", json);
        Assert.Contains("Love Drought", json);
        Assert.Contains("Beyoncé", json);
        Assert.Contains("Lemonade", json);
    }

    [Theory]
    [InlineData("AppleInc.AppleMusicWin_8wekyb3d8bbwe!App", "Apple Music")]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("msedge.exe", "Web Player")]
    [InlineData("chrome.exe", "Web Player")]
    [InlineData("TIDAL.exe", "TIDAL")]
    [InlineData("AmazonMusic.exe", "Amazon Music")]
    [InlineData("Deezer.exe", "Deezer")]
    [InlineData("SoundCloud.exe", "SoundCloud")]
    public void ResolveSourceAppBadge_MapsKnownAppsCorrectly(string aumid, string expectedBadgeText)
    {
        var (image, text) = DiscordRpcService.ResolveSourceAppBadge(aumid, isPlaying: true);
        Assert.NotEmpty(image);
        Assert.Contains(expectedBadgeText, text);
    }
}


