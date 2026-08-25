using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using TrackDot.Models;
using TrackDot.ViewModels;
using Windows.Media.Control;

namespace TrackDot.Services;

/// <summary>
/// Owns the SMTC <see cref="GlobalSystemMediaTransportControlsSessionManager"/>
/// lifecycle for TrackDot. Wires up event subscriptions, centralises
/// session replacement behind a generation counter so stale async
/// results cannot leak across session switches, and publishes a
/// fresh <see cref="MediaSessionSnapshot"/> on every authoritative
/// event.
/// </summary>
/// <remarks>
/// <para>
/// The service is the only place in the codebase that touches WinRT
/// SMTC objects. Everything downstream (view model, view) talks to
/// it through the immutable <see cref="MediaSessionSnapshot"/>
/// contract.
/// </para>
/// <para>
/// Marshalling: every WinRT event handler (which runs on an arbitrary
/// thread-pool thread) and every async continuation (which resumes on
/// a thread-pool thread by default) is funnelled through the
/// <see cref="SynchronizationContext"/> captured at construction
/// time. The WPF app installs the dispatcher context in
/// <c>App.OnStartup</c>, so subscribers to
/// <see cref="SnapshotChanged"/> can safely bind to view-model
/// properties without a Dispatcher.Invoke.
/// </para>
/// <para>
/// Generation: each call to <see cref="SetCurrentSessionAsync"/>
/// bumps an internal counter. Every async continuation captures the
/// counter at entry and discards its result if the counter has
/// advanced. This is the only thing standing between a fast
/// session switch and the wrong track being displayed.
/// </para>
/// </remarks>
public sealed class MediaControllerService : IMediaControllerService
{
    private readonly SynchronizationContext _context;
    private readonly object _gate = new();
    private readonly AudioVolumeService _volumeService;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;

    // Pinned AUMID from SelectSessionAsync. Null means "follow OS default".
    private string? _pinnedAumid;

    private int _generation;
    private bool _initialized;
    private bool _disposed;

    // Feature 9 — session list
    private IReadOnlyList<MediaSessionInfo> _availableSessions = Array.Empty<MediaSessionInfo>();

    /// <summary>
    /// Builds a service that marshals every published snapshot to
    /// <paramref name="context"/>. Pass <c>null</c> to use the
    /// current context (the WPF dispatcher context, when constructed
    /// from <c>App.OnStartup</c>).
    /// </summary>
    public MediaControllerService(SynchronizationContext? context = null)
    {
        _context = context ?? SynchronizationContext.Current
            ?? throw new InvalidOperationException(
                "No SynchronizationContext was supplied and none is current. " +
                "Construct MediaControllerService from a thread with an installed context " +
                "(e.g. the WPF UI thread).");
        _volumeService = new AudioVolumeService();
    }

    /// <summary>
    /// Test-only seam that drops the active session to <c>null</c>
    /// and forces the cached capabilities to <see cref="TransportCapabilities.None"/>.
    /// Used to exercise the no-session short-circuit path without a
    /// live WinRT session manager.
    /// </summary>
    /// <remarks>
    /// Exposed via <c>InternalsVisibleTo TrackDot.Tests</c>. The
    /// production code path never calls this; the WinRT event
    /// subscriptions in <see cref="SetCurrentSessionAsync"/> are
    /// intentionally bypassed because there is no real SMTC session
    /// to subscribe to in unit tests.
    /// </remarks>
    internal void ClearSessionForTest()
    {
        lock (_gate)
        {
            _currentSession = null;
            _generation++;
        }

        var previous = Volatile.Read(ref _currentSnapshot);
        var next = previous with
        {
            Playback = previous.Playback with { Capabilities = TransportCapabilities.None }
        };
        Volatile.Write(ref _currentSnapshot, next);
    }

    /// <summary>
    /// Test-only seam that updates the cached
    /// <see cref="TransportCapabilities"/> so the capability gate
    /// sees the test-supplied flags. The session itself is not
    /// touched (use <see cref="ClearSessionForTest"/> to drop it).
    /// </summary>
    internal void SetCapabilitiesForTest(TransportCapabilities capabilities)
    {
        var previous = Volatile.Read(ref _currentSnapshot);
        var next = previous with
        {
            Playback = previous.Playback with { Capabilities = capabilities }
        };
        Volatile.Write(ref _currentSnapshot, next);
    }

    /// <inheritdoc/>
    public MediaSessionSnapshot Current => Volatile.Read(ref _currentSnapshot);

    private MediaSessionSnapshot _currentSnapshot = MediaSessionSnapshot.Empty;

    /// <inheritdoc/>
    public event EventHandler<MediaSessionSnapshot>? SnapshotChanged;

    // ── Feature 9 ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<MediaSessionInfo> AvailableSessions
        => Volatile.Read(ref _availableSessions);

    /// <inheritdoc/>
    public event EventHandler? SessionListChanged;

    /// <inheritdoc/>
    public async Task SelectSessionAsync(string sourceAppUserModelId)
    {
        if (_disposed) return;
        if (string.IsNullOrEmpty(sourceAppUserModelId)) return;

        var manager = _manager;
        if (manager is null) return;

        // Find the requested session in the live list
        var sessions = manager.GetSessions();
        GlobalSystemMediaTransportControlsSession? target = null;
        foreach (var s in sessions)
        {
            if (string.Equals(s.SourceAppUserModelId, sourceAppUserModelId,
                              StringComparison.OrdinalIgnoreCase))
            {
                target = s;
                break;
            }
        }
        if (target is null) return;

        // Store the pin BEFORE adopting the session so that subsequent
        // CurrentSessionChanged events respect the user's choice.
        lock (_gate) { _pinnedAumid = sourceAppUserModelId; }

        await SetCurrentSessionAsync(target).ConfigureAwait(true);
        if (_manager is not null)
        {
            RefreshSessionList(_manager);
        }
    }

    // ── Feature 10 ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task SetVolumeAsync(double volume)
    {
        if (_disposed) return Task.CompletedTask;
        var aumid = Volatile.Read(ref _currentSnapshot).SourceAppUserModelId;
        _volumeService.SetVolume(aumid, (float)Math.Clamp(volume, 0.0, 1.0));
        PublishVolumeUpdate();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ToggleMuteAsync()
    {
        if (_disposed) return Task.CompletedTask;
        var aumid = Volatile.Read(ref _currentSnapshot).SourceAppUserModelId;
        if (string.IsNullOrEmpty(aumid)) return Task.CompletedTask;
        var (_, currentMute) = _volumeService.GetVolumeInfo(aumid);
        _volumeService.SetMute(aumid, !currentMute);
        PublishVolumeUpdate();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RefreshVolumeAsync()
    {
        if (_disposed) return Task.CompletedTask;
        PublishVolumeUpdate();
        return Task.CompletedTask;
    }

    // ── Core SMTC init ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_initialized) return;
            _initialized = true;
        }

        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(true);

        lock (_gate)
        {
            if (_manager is not null) return;
            _manager = manager;
        }

        manager.CurrentSessionChanged += OnCurrentSessionChanged;
        manager.SessionsChanged       += OnSessionsChanged;

        var initial = manager.GetCurrentSession();
        if (initial is null)
        {
            Publish(MediaSessionSnapshot.Empty);
        }
        else
        {
            await SetCurrentSessionAsync(initial).ConfigureAwait(true);
        }

        // Publish an initial session list, reflecting the adopted session.
        RefreshSessionList(manager);
    }

    // ── Transport commands ───────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task TogglePlayPauseAsync()
        => InvokeOnSessionAsync(
            capability: CanTogglePlayPause,
            action: session =>
            {
                var info = session.GetPlaybackInfo();
                var state = MediaPropertyMapper.MapPlaybackStatus(info.PlaybackStatus);

                return state == MediaPlaybackState.Playing
                    ? session.TryPauseAsync().AsTask()
                    : session.TryPlayAsync().AsTask();
            });

    /// <inheritdoc/>
    public Task PreviousAsync()
        => InvokeOnSessionAsync(
            capability: caps => caps.CanGoPrevious,
            action: static session => session.TrySkipPreviousAsync().AsTask());

    /// <inheritdoc/>
    public Task StopAsync()
        => InvokeOnSessionAsync(
            capability: caps => caps.CanStop,
            action: static session => session.TryStopAsync().AsTask());

    /// <inheritdoc/>
    public Task NextAsync()
        => InvokeOnSessionAsync(
            capability: caps => caps.CanGoNext,
            action: static session => session.TrySkipNextAsync().AsTask());

    /// <inheritdoc/>
    public Task SeekAsync(double positionSeconds)
    {
        if (_disposed) return Task.CompletedTask;
        var ticks = (long)(positionSeconds * TimeSpan.TicksPerSecond);
        var position = TimeSpan.FromTicks(Math.Max(0, ticks));

        var session = Volatile.Read(ref _currentSession);
        if (session is null) return Task.CompletedTask;

        return session.TryChangePlaybackPositionAsync(position.Ticks).AsTask();
    }

    private static bool CanTogglePlayPause(TransportCapabilities caps)
    {
        return caps.CanPlay || caps.CanPause;
    }

    // ── Session replacement ──────────────────────────────────────────────────

    private async Task SetCurrentSessionAsync(GlobalSystemMediaTransportControlsSession? session)
    {
        GlobalSystemMediaTransportControlsSession? previous;
        int generation;
        lock (_gate)
        {
            previous = _currentSession;
            _currentSession = session;
            _generation++;
            generation = _generation;
        }

        if (previous is not null)
        {
            previous.MediaPropertiesChanged   -= OnMediaPropertiesChanged;
            previous.PlaybackInfoChanged      -= OnPlaybackInfoChanged;
            previous.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        if (session is null)
        {
            Publish(MediaSessionSnapshot.Empty);
            return;
        }

        session.MediaPropertiesChanged    += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged       += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        var aumid = session.SourceAppUserModelId;
        var (vol, muted) = _volumeService.GetVolumeInfo(aumid);

        var prevSnapshot = Volatile.Read(ref _currentSnapshot);
        var sessionSnapshot = prevSnapshot with
        {
            SourceAppUserModelId = aumid,
            Volume = vol,
            IsMuted = muted
        };
        Publish(sessionSnapshot);

        await RefreshMediaPropertiesAsync(session, generation).ConfigureAwait(true);
        RefreshPlaybackInfoAsync(session, generation);
        RefreshTimelinePropertiesAsync(session, generation);
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
        => Post(() => _ = HandleCurrentSessionChangedAsync());

    private async Task HandleCurrentSessionChangedAsync()
    {
        if (_disposed) return;

        var manager = _manager;
        if (manager is null) return;

        // Respect the user's pinned session if it still exists.
        string? pinned;
        lock (_gate) { pinned = _pinnedAumid; }

        GlobalSystemMediaTransportControlsSession? next;
        if (pinned is not null)
        {
            var sessions = manager.GetSessions();
            next = null;
            foreach (var s in sessions)
            {
                if (string.Equals(s.SourceAppUserModelId, pinned,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    next = s;
                    break;
                }
            }
            // Pinned source disappeared — fall back to OS default and clear pin.
            if (next is null)
            {
                lock (_gate) { _pinnedAumid = null; }
                next = manager.GetCurrentSession();
            }
        }
        else
        {
            next = manager.GetCurrentSession();
        }

        await SetCurrentSessionAsync(next).ConfigureAwait(true);
        RefreshSessionList(manager);
    }

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args)
        => Post(() =>
        {
            if (_disposed) return;
            if (_manager is null) return;
            RefreshSessionList(_manager);
        });

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
        => Post(() => _ = RefreshMediaPropertiesAsync(sender, Volatile.Read(ref _generation)));

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
        => Post(() => RefreshPlaybackInfoAsync(sender, Volatile.Read(ref _generation)));

    private void OnTimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args)
        => Post(() => RefreshTimelinePropertiesAsync(sender, Volatile.Read(ref _generation)));

    // ── Session list ─────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds <see cref="AvailableSessions"/> from the live SMTC
    /// session list and raises <see cref="SessionListChanged"/>.
    /// Must be called on the UI thread.
    /// </summary>
    private void RefreshSessionList(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        if (_disposed) return;

        var current = Volatile.Read(ref _currentSnapshot);
        var currentAumid = current.SourceAppUserModelId;

        var sessions = manager.GetSessions();
        var list = new List<MediaSessionInfo>(sessions.Count);
        foreach (var s in sessions)
        {
            var aumid = s.SourceAppUserModelId ?? string.Empty;
            list.Add(new MediaSessionInfo(
                SourceAppUserModelId: aumid,
                DisplayName: MainViewModelHelpers.FormatAppName(aumid),
                IsCurrent: string.Equals(aumid, currentAumid, StringComparison.OrdinalIgnoreCase)));
        }

        Volatile.Write(ref _availableSessions, list);
        SessionListChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Async property reads — generation guarded ────────────────────────────

    private async Task RefreshMediaPropertiesAsync(
        GlobalSystemMediaTransportControlsSession session, int generation)
    {
        try
        {
            var props = await session.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(true);
            if (!IsCurrentGeneration(generation)) return;
            if (props is null) return;

            var artwork = await DecodeArtworkAsync(props.Thumbnail).ConfigureAwait(true);
            if (!IsCurrentGeneration(generation)) return;

            PublishMediaUpdate(
                title: props.Title,
                artist: props.Artist,
                albumTitle: props.AlbumTitle,
                artwork: artwork);
        }
        catch (Exception)
        {
            // Transient COM error — let the next authoritative event retry.
        }
    }

    private void RefreshPlaybackInfoAsync(
        GlobalSystemMediaTransportControlsSession session, int generation)
    {
        try
        {
            var info = session.GetPlaybackInfo();
            if (!IsCurrentGeneration(generation)) return;

            PublishPlaybackInfoUpdate(info);
        }
        catch (Exception) { }
    }

    private void RefreshTimelinePropertiesAsync(
        GlobalSystemMediaTransportControlsSession session, int generation)
    {
        try
        {
            var timeline = session.GetTimelineProperties();
            if (!IsCurrentGeneration(generation)) return;

            PublishTimelineUpdate(timeline);
        }
        catch (Exception) { }
    }

    private static Task<ImageSource?> DecodeArtworkAsync(object? thumbnail)
    {
        if (thumbnail is null)
            return Task.FromResult<ImageSource?>(null);

        return ThumbnailDecoder.DecodeAsync(
            openStream: () =>
            {
                var reference = (Windows.Storage.Streams.IRandomAccessStreamReference)thumbnail;
                return OpenThumbnailAsManagedStreamAsync(reference);
            });
    }

    private static async Task<Stream> OpenThumbnailAsManagedStreamAsync(
        Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        var winrtStream = await reference.OpenReadAsync();
        return winrtStream.AsStreamForRead();
    }

    // ── Publish path ─────────────────────────────────────────────────────────

    private void PublishMediaUpdate(
        string title, string artist, string albumTitle, ImageSource? artwork)
    {
        if (_disposed) return;

        var previous = Volatile.Read(ref _currentSnapshot);
        var next = previous with
        {
            Title      = title      ?? string.Empty,
            Artist     = artist     ?? string.Empty,
            AlbumTitle = albumTitle ?? string.Empty,
            Artwork    = artwork ?? previous.Artwork,
        };

        Volatile.Write(ref _currentSnapshot, next);
        SnapshotChanged?.Invoke(this, next);
    }

    private void PublishPlaybackInfoUpdate(GlobalSystemMediaTransportControlsSessionPlaybackInfo info)
    {
        if (_disposed) return;

        var controlsShape = info.Controls is { } c
            ? new MediaPropertyMapper.ControlsShape(
                CanPlay:       c.IsPlayEnabled,
                CanPause:      c.IsPauseEnabled,
                CanStop:       c.IsStopEnabled,
                CanGoPrevious: c.IsPreviousEnabled,
                CanGoNext:     c.IsNextEnabled,
                CanSeek:       c.IsPlaybackPositionEnabled)
            : null;

        var playbackInfoShape = new MediaPropertyMapper.PlaybackInfoShape(info.PlaybackStatus, controlsShape);

        var previous = Volatile.Read(ref _currentSnapshot);
        var timelineShape = new MediaPropertyMapper.TimelineShape(
            Position:    previous.Playback.Position,
            StartTime:   previous.Playback.StartTime,
            EndTime:     previous.Playback.EndTime,
            LastUpdated: previous.Playback.TimelineUpdatedAt);

        var playback = MediaPropertyMapper.BuildPlaybackSnapshot(
            playbackInfo: playbackInfoShape,
            timeline: timelineShape,
            capturedAt: DateTimeOffset.UtcNow);

        var next = previous with { Playback = playback };

        Volatile.Write(ref _currentSnapshot, next);
        SnapshotChanged?.Invoke(this, next);
    }

    private void PublishTimelineUpdate(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline)
    {
        if (_disposed) return;

        var timelineShape = new MediaPropertyMapper.TimelineShape(
            Position:    timeline.Position,
            StartTime:   timeline.StartTime,
            EndTime:     timeline.EndTime,
            LastUpdated: DateTimeOffset.UtcNow);

        var previous = Volatile.Read(ref _currentSnapshot);
        var playbackInfoShape = new MediaPropertyMapper.PlaybackInfoShape(
            Status: PlaybackStateToSmts(previous.Playback.State),
            Controls: new MediaPropertyMapper.ControlsShape(
                CanPlay:       previous.Playback.Capabilities.CanPlay,
                CanPause:      previous.Playback.Capabilities.CanPause,
                CanStop:       previous.Playback.Capabilities.CanStop,
                CanGoPrevious: previous.Playback.Capabilities.CanGoPrevious,
                CanGoNext:     previous.Playback.Capabilities.CanGoNext,
                CanSeek:       previous.Playback.Capabilities.CanSeek));

        var playback = MediaPropertyMapper.BuildPlaybackSnapshot(
            playbackInfo: playbackInfoShape,
            timeline: timelineShape,
            capturedAt: timelineShape.LastUpdated);

        var next = previous with { Playback = playback };

        Volatile.Write(ref _currentSnapshot, next);
        SnapshotChanged?.Invoke(this, next);
    }

    /// <summary>
    /// Re-reads volume/mute from CoreAudio for the current SMTC source
    /// and publishes a refreshed snapshot. Called after
    /// <see cref="SetVolumeAsync"/> and <see cref="ToggleMuteAsync"/>
    /// complete so the view model sees the updated values immediately.
    /// </summary>
    private void PublishVolumeUpdate()
    {
        if (_disposed) return;
        var previous = Volatile.Read(ref _currentSnapshot);
        var (vol, muted) = _volumeService.GetVolumeInfo(previous.SourceAppUserModelId);
        var next = previous with { Volume = vol, IsMuted = muted };
        Volatile.Write(ref _currentSnapshot, next);
        SnapshotChanged?.Invoke(this, next);
    }

    private static GlobalSystemMediaTransportControlsSessionPlaybackStatus PlaybackStateToSmts(
        MediaPlaybackState state)
        => state switch
        {
            MediaPlaybackState.Closed   => GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed,
            MediaPlaybackState.Opened   => GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened,
            MediaPlaybackState.Changing => GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing,
            MediaPlaybackState.Stopped  => GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped,
            MediaPlaybackState.Playing  => GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            MediaPlaybackState.Paused   => GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused,
            _                           => GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed,
        };

    private void Publish(MediaSessionSnapshot snapshot)
    {
        if (_disposed) return;
        Volatile.Write(ref _currentSnapshot, snapshot);
        SnapshotChanged?.Invoke(this, snapshot);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool IsCurrentGeneration(int generation)
        => generation == Volatile.Read(ref _generation);

    private void Post(Action callback)
    {
        if (_disposed) return;
        _context.Post(_ =>
        {
            if (_disposed) return;
            callback();
        }, null);
    }

    private Task InvokeOnSessionAsync(
        Func<TransportCapabilities, bool> capability,
        Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
        => DispatchGuardedCommandAsync(
            capability: capability,
            tryCommand: () =>
            {
                var session = Volatile.Read(ref _currentSession);
                if (session is null) return Task.FromResult(false);
                return action(session);
            },
            refresh: () =>
            {
                var session = Volatile.Read(ref _currentSession);
                if (session is null) return;
                RefreshPlaybackInfoAsync(session, Volatile.Read(ref _generation));
            });

    internal async Task DispatchGuardedCommandAsync(
        Func<TransportCapabilities, bool> capability,
        Func<Task<bool>> tryCommand,
        Action refresh)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(tryCommand);
        ArgumentNullException.ThrowIfNull(refresh);

        if (_disposed) return;

        var snapshot = Volatile.Read(ref _currentSnapshot);
        if (!capability(snapshot.Playback.Capabilities)) return;

        bool success;
        try
        {
            success = await tryCommand().ConfigureAwait(true);
        }
        catch (Exception)
        {
            refresh();
            return;
        }

        if (!success) refresh();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        GlobalSystemMediaTransportControlsSessionManager? manager;
        GlobalSystemMediaTransportControlsSession? session;
        lock (_gate)
        {
            manager = _manager;
            session = _currentSession;
            _manager = null;
            _currentSession = null;
        }

        if (manager is not null)
        {
            manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            manager.SessionsChanged       -= OnSessionsChanged;
        }

        if (session is not null)
        {
            session.MediaPropertiesChanged    -= OnMediaPropertiesChanged;
            session.PlaybackInfoChanged       -= OnPlaybackInfoChanged;
            session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        Volatile.Write(ref _generation, _generation + 1);

        _volumeService.Dispose();

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
