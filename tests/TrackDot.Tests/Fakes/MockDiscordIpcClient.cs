using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrackDot.Services;

namespace TrackDot.Tests.Fakes;

public sealed class MockDiscordIpcClient : IDiscordIpcClient
{
    public bool IsConnected { get; set; }
    public bool ConnectResult { get; set; } = true;
    public string? LastClientId { get; private set; }
    public List<object?> SentActivities { get; } = new();
    public int ClearCount { get; private set; }
    public int CloseCount { get; private set; }

    public Task<bool> ConnectAsync(string clientId, CancellationToken cancellationToken = default)
    {
        LastClientId = clientId;
        IsConnected = ConnectResult;
        return Task.FromResult(ConnectResult);
    }

    public Task SendSetActivityAsync(object? activity, CancellationToken cancellationToken = default)
    {
        SentActivities.Add(activity);
        return Task.CompletedTask;
    }

    public Task ClearActivityAsync(CancellationToken cancellationToken = default)
    {
        ClearCount++;
        SentActivities.Add(null);
        return Task.CompletedTask;
    }

    public void Close()
    {
        CloseCount++;
        IsConnected = false;
    }

    public void Dispose()
    {
        Close();
    }
}

