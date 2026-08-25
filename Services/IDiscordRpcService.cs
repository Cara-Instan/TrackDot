using System;
using System.Threading.Tasks;
using TrackDot.Models;

namespace TrackDot.Services;

/// <summary>
/// Service responsible for managing Discord Rich Presence integration,
/// handling dynamic source app registration, privacy filtering, and broadcasting
/// live playback metadata.
/// </summary>
public interface IDiscordRpcService : IDisposable
{
    /// <summary>
    /// Gets whether the underlying Discord IPC connection is active.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Evaluates privacy settings and updates or clears Discord presence for the given session snapshot.
    /// </summary>
    Task UpdatePresenceAsync(MediaSessionSnapshot session);
}

