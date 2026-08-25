using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TrackDot.Services;

/// <summary>
/// Production implementation of <see cref="IDiscordIpcClient"/> using native Windows
/// Named Pipes (<c>\\.\pipe\discord-ipc-{0..9}</c>).
/// Zero external NuGet dependencies, pure async I/O.
/// </summary>
public sealed class DiscordNamedPipeIpcClient : IDiscordIpcClient
{
    private const int HandshakeOpcode = 0;
    private const int FrameOpcode = 1;
    private const int CloseOpcode = 2;

    private NamedPipeClientStream? _pipe;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <inheritdoc/>
    public bool IsConnected => _pipe != null && _pipe.IsConnected;

    /// <inheritdoc/>
    public async Task<bool> ConnectAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (IsConnected) return true;

        Close();

        for (int i = 0; i < 10; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var pipeName = $"discord-ipc-{i}";
            try
            {
                var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(500);

                await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);

                // Handshake Opcode 0: {"v": 1, "client_id": "<clientId>"}
                var handshakePayload = JsonSerializer.Serialize(new
                {
                    v = 1,
                    client_id = clientId
                }, JsonOpts);

                await SendPacketAsync(pipe, HandshakeOpcode, handshakePayload, cts.Token).ConfigureAwait(false);

                // Read handshake response
                var (opcode, responseJson) = await ReadPacketAsync(pipe, cts.Token).ConfigureAwait(false);
                if (opcode == FrameOpcode || opcode == HandshakeOpcode)
                {
                    _pipe = pipe;
                    return true;
                }

                pipe.Dispose();
            }
            catch
            {
                // Pipe unavailable or timeout on this slot; try next slot
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task SendSetActivityAsync(object? activity, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _pipe == null) return;

        var pid = Environment.ProcessId;
        var nonce = Guid.NewGuid().ToString("N");

        var payload = JsonSerializer.Serialize(new
        {
            cmd = "SET_ACTIVITY",
            args = new
            {
                pid,
                activity
            },
            nonce
        }, JsonOpts);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsConnected || _pipe == null) return;
            await SendPacketAsync(_pipe, FrameOpcode, payload, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Close();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ClearActivityAsync(CancellationToken cancellationToken = default)
    {
        await SendSetActivityAsync(null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendPacketAsync(NamedPipeClientStream stream, int opcode, string jsonPayload, CancellationToken ct)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);
        var headerBytes = new byte[8];

        BitConverter.TryWriteBytes(headerBytes.AsSpan(0, 4), opcode);
        BitConverter.TryWriteBytes(headerBytes.AsSpan(4, 4), payloadBytes.Length);

        await stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        if (payloadBytes.Length > 0)
        {
            await stream.WriteAsync(payloadBytes, ct).ConfigureAwait(false);
        }
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<(int Opcode, string Json)> ReadPacketAsync(NamedPipeClientStream stream, CancellationToken ct)
    {
        var header = new byte[8];
        int readHeader = 0;
        while (readHeader < 8)
        {
            int r = await stream.ReadAsync(header.AsMemory(readHeader, 8 - readHeader), ct).ConfigureAwait(false);
            if (r <= 0) throw new EndOfStreamException("Discord IPC pipe closed during header read.");
            readHeader += r;
        }

        var opcode = BitConverter.ToInt32(header, 0);
        var length = BitConverter.ToInt32(header, 4);

        if (length <= 0) return (opcode, string.Empty);

        var payload = new byte[length];
        int readPayload = 0;
        while (readPayload < length)
        {
            int r = await stream.ReadAsync(payload.AsMemory(readPayload, length - readPayload), ct).ConfigureAwait(false);
            if (r <= 0) throw new EndOfStreamException("Discord IPC pipe closed during payload read.");
            readPayload += r;
        }

        return (opcode, Encoding.UTF8.GetString(payload));
    }

    /// <inheritdoc/>
    public void Close()
    {
        if (_pipe != null)
        {
            try
            {
                _pipe.Dispose();
            }
            catch { }
            _pipe = null;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        _sendLock.Dispose();
    }
}
