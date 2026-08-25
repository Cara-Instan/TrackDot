using System;

namespace TrackDot.Models;

/// <summary>
/// Persistent settings record for an individual media source application
/// (e.g. Spotify, Google Chrome, VLC), governing whether media playback
/// from this application is permitted to broadcast to Discord Rich Presence.
/// </summary>
/// <param name="Aumid">The Application User Model ID or process identifier.</param>
/// <param name="DisplayName">Formatted human-readable name (e.g. "Spotify", "Google Chrome").</param>
/// <param name="DiscordRpcEnabled">Whether Discord Rich Presence broadcasting is allowed for this app.</param>
/// <param name="DiscoveredAt">Timestamp when this application was first observed playing media.</param>
public sealed record SourceAppSetting(
    string Aumid,
    string DisplayName,
    bool DiscordRpcEnabled,
    DateTime DiscoveredAt)
{
    /// <summary>
    /// Creates a new <see cref="SourceAppSetting"/> with strict privacy default (DiscordRpcEnabled = false).
    /// </summary>
    public static SourceAppSetting CreateDefault(string aumid, string displayName) =>
        new(aumid, displayName, DiscordRpcEnabled: false, DiscoveredAt: DateTime.UtcNow);
}

