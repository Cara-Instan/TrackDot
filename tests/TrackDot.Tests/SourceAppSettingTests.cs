using System;
using System.Text.Json;
using TrackDot.Models;
using Xunit;

namespace TrackDot.Tests;

public class SourceAppSettingTests
{
    [Fact]
    public void CreateDefault_SetsStrictPrivacyDefaults()
    {
        var app = SourceAppSetting.CreateDefault("Spotify.exe", "Spotify");

        Assert.Equal("Spotify.exe", app.Aumid);
        Assert.Equal("Spotify", app.DisplayName);
        Assert.False(app.DiscordRpcEnabled);
        Assert.True(app.DiscoveredAt <= DateTime.UtcNow);
    }

    [Fact]
    public void SourceAppSetting_JsonRoundtrip_PreservesData()
    {
        var original = new SourceAppSetting(
            Aumid: "Chrome.exe",
            DisplayName: "Google Chrome",
            DiscordRpcEnabled: true,
            DiscoveredAt: new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<SourceAppSetting>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Aumid, deserialized.Aumid);
        Assert.Equal(original.DisplayName, deserialized.DisplayName);
        Assert.Equal(original.DiscordRpcEnabled, deserialized.DiscordRpcEnabled);
        Assert.Equal(original.DiscoveredAt, deserialized.DiscoveredAt);
    }
}

