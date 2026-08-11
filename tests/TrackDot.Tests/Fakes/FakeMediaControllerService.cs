using System;
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

    /// <summary>The most recent snapshot, defaulting to <see cref="MediaSessionSnapshot.Empty"/>.</summary>
    public MediaSessionSnapshot Current => _current;

    /// <inheritdoc/>
    public event EventHandler<MediaSessionSnapshot>? SnapshotChanged;

    /// <summary>How many times <see cref="InitializeAsync"/> was called.</summary>
    public int InitializeCallCount { get; private set; }

    /// <summary>How many times the service was disposed.</summary>
    public int DisposeCallCount { get; private set; }

    /// <summary>How many times each transport command was invoked.</summary>
    public int TogglePlayPauseCallCount { get; private set; }
    public int PreviousCallCount { get; private set; }
    public int StopCallCount { get; private set; }
    public int NextCallCount { get; private set; }

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

    public ValueTask DisposeAsync()
    {
        DisposeCallCount++;
        SnapshotChanged = null;
        return ValueTask.CompletedTask;
    }
}
