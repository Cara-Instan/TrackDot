using System;
using System.Threading;
using System.Threading.Tasks;

namespace TrackDot.Services;

/// <summary>
/// Abstraction over the Discord Named Pipe IPC connection.
/// Separated to allow comprehensive unit testing without requiring a live Discord desktop instance.
/// </summary>
public interface IDiscordIpcClient : IDisposable
{
    /// <summary>
    /// Gets whether the IPC pipe is currently connected to Discord.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Attempts to open a named pipe connection to Discord and perform the Opcode 0 handshake.
    /// </summary>
    Task<bool> ConnectAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a SET_ACTIVITY frame payload to Discord.
    /// </summary>
    Task SendSetActivityAsync(object? activity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the active Discord presence.
    /// </summary>
    Task ClearActivityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the underlying IPC connection.
    /// </summary>
    void Close();
}

