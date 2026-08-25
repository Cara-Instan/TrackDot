using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrackDot.Models;
using TrackDot.Services;

namespace TrackDot.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IMediaControllerService"/> for view-model tests.
/// Mirrors the production contract: a single active
/// <see cref="MediaSessionSnapshot"/>, an event for change
/// notifications, and the four transport commands. Tests drive the
/// state transitions explicitly via <see cref="Publish"/>; production
/// WinRT events never fire in tests.
/// </summary>
public sealed class FakeMediaControllerService : IMediaControllerService
{
    private MediaSessionSnapshot _current = MediaSessionSnapshot.Empty;
    private IReadOnlyList<MediaSessionInfo> _availableSessions = Array.Empty<MediaSessionInfo>();

    /// <summary>The most recent snapshot, defaulting to <see cref="MediaSessionSnapshot.Empty"/>.</summary>
    public MediaSessionSnapshot Current => _current;

    /// <inheritdoc/>
    public event EventHandler<MediaSessionSnapshot>? SnapshotChanged;

    // ── Feature 9 ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<MediaSessionInfo> AvailableSessions => _availableSessions;

    /// <inheritdoc/>
    public event EventHandler? SessionListChanged;

    /// <summary>How many times <see cref="SelectSessionAsync"/> was called.</summary>
    public int SelectSessionCallCount { get; private set; }

    /// <summary>The last AUMID passed to <see cref="SelectSessionAsync"/>.</summary>
    public string? LastSelectedAumid { get; private set; }

    /// <summary>
    /// Replaces <see cref="AvailableSessions"/> and raises
    /// <see cref="SessionListChanged"/>. Tests call this to drive session
    /// list transitions.
    /// </summary>
    public void PublishSessionList(IReadOnlyList<MediaSessionInfo> sessions)
    {
        _availableSessions = sessions;
        SessionListChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Feature 10 ──────────────────────────────────────────────────────────

    /// <summary>How many times <see cref="SetVolumeAsync"/> was called.</summary>
    public int SetVolumeCallCount { get; private set; }

    /// <summary>The last volume value passed to <see cref="SetVolumeAsync"/>.</summary>
    public double LastSetVolume { get; private set; }

    /// <summary>How many times <see cref="ToggleMuteAsync"/> was called.</summary>
    public int ToggleMuteCallCount { get; private set; }

    /// <summary>How many times <see cref="RefreshVolumeAsync"/> was called.</summary>
    public int RefreshVolumeCallCount { get; private set; }

    // ── Call counters ────────────────────────────────────────────────────────

    /// <summary>How many times <see cref="InitializeAsync"/> was called.</summary>
    public int InitializeCallCount { get; private set; }

    /// <summary>How many times the service was disposed.</summary>
    public int DisposeCallCount { get; private set; }

    /// <summary>How many times each transport command was invoked.</summary>
    public int TogglePlayPauseCallCount { get; private set; }
    public int PreviousCallCount { get; private set; }
    public int StopCallCount { get; private set; }
    public int NextCallCount { get; private set; }
    public int SeekCallCount { get; private set; }

    /// <summary>
    /// Set true to make every command throw, so the view-model's
    /// exception-swallow contract can be tested. Default false.
    /// </summary>
    public bool ThrowOnCommand { get; set; }

    /// <summary>The most recent snapshot published by the test.</summary>
    public MediaSessionSnapshot? LastPublished { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        InitializeCallCount++;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Replaces the current snapshot and raises
    /// <see cref="SnapshotChanged"/>. Tests call this to drive the
    /// view-model through state transitions.
    /// </summary>
    public void Publish(MediaSessionSnapshot snapshot)
    {
        _current = snapshot;
        LastPublished = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    public Task SelectSessionAsync(string sourceAppUserModelId)
    {
        SelectSessionCallCount++;
        LastSelectedAumid = sourceAppUserModelId;
        if (ThrowOnCommand) throw new InvalidOperationException("fake: SelectSession refused");
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(double volume)
    {
        SetVolumeCallCount++;
        LastSetVolume = volume;
        if (ThrowOnCommand) throw new InvalidOperationException("fake: SetVolume refused");
        return Task.CompletedTask;
    }

    public Task ToggleMuteAsync()
    {
        ToggleMuteCallCount++;
        if (ThrowOnCommand) throw new InvalidOperationException("fake: ToggleMute refused");
        return Task.CompletedTask;
    }

    public Task RefreshVolumeAsync()
    {
        RefreshVolumeCallCount++;
        if (ThrowOnCommand) throw new InvalidOperationException("fake: RefreshVolume refused");
        return Task.CompletedTask;
    }

    public Task TogglePlayPauseAsync()
    {
        TogglePlayPauseCallCount++;
        if (ThrowOnCommand) throw new InvalidOperationException("fake: TogglePlayPause refused");
        return Task.CompletedTask;
    }

    public Task PreviousAsync()
    {
        PreviousCallCount++;
        if (ThrowOnCommand) throw new InvalidOperationException("fake: Previous refused");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCallCount++;
        if (ThrowOnCommand) throw new InvalidOperationException("fake: Stop refused");
        return Task.CompletedTask;
    }

    public Task NextAsync()
    {
        NextCallCount++;
        if (ThrowOnCommand) throw new InvalidOperationException("fake: Next refused");
        return Task.CompletedTask;
    }

    public Task SeekAsync(double positionSeconds)
    {
        SeekCallCount++;
        if (ThrowOnCommand) throw new InvalidOperationException("fake: Seek refused");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCallCount++;
        SnapshotChanged = null;
        return ValueTask.CompletedTask;
    }
}
