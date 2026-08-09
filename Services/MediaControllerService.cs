using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using TrackDot.Models;
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

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private int _generation;
    private bool _initialized;
    private bool _disposed;

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

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;
        }

        // RequestAsync() yields the system SMTC manager. The first
        // call may take a moment; subsequent calls are cheap. We do
        // NOT hold the lock across the await - the manager hands
        // itself back on a worker thread and we want other
        // initialisation paths (status, etc.) to make progress.
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(true);

        lock (_gate)
        {
            // Another thread may have raced us to initialize. If
            // the manager is already set, discard ours - we will
            // not subscribe twice.
            if (_manager is not null)
            {
                return;
            }
            _manager = manager;
        }

        manager.CurrentSessionChanged += OnCurrentSessionChanged;

        // Publish Empty first, then adopt whatever session the
        // manager already tracks (typically the OS-default media
        // app, e.g. Spotify if it was running at boot).
        var initial = manager.GetCurrentSession();
        if (initial is null)
        {
            Publish(MediaSessionSnapshot.Empty);
        }
        else
        {
            await SetCurrentSessionAsync(initial).ConfigureAwait(true);
        }
    }

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

    private static bool CanTogglePlayPause(TransportCapabilities caps)
    {
        // Play/Pause is one button that maps to either Play or Pause
        // depending on the current state. Treat the command as
        // supported if EITHER flag is true - the service picks the
        // right direction at dispatch time.
        return caps.CanPlay || caps.CanPause;
    }
    // -------------------------------------------------------------------
    // Session replacement (the heart of the generation guard)
    // -------------------------------------------------------------------

    private async Task SetCurrentSessionAsync(GlobalSystemMediaTransportControlsSession? session)
    {
        // Detach from any previous session BEFORE attaching to the
        // new one - never subscribe twice, never leak a handler.
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
            previous.MediaPropertiesChanged  -= OnMediaPropertiesChanged;
            previous.PlaybackInfoChanged     -= OnPlaybackInfoChanged;
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

        // Kick off the initial read for each of the three property
        // groups. Each continuation captures its generation in a
        // local; stale completions are silently discarded.
        await RefreshMediaPropertiesAsync(session, generation).ConfigureAwait(true);
        RefreshPlaybackInfoAsync(session, generation);
        RefreshTimelinePropertiesAsync(session, generation);
    }

    // -------------------------------------------------------------------
    // Event handlers (run on WinRT thread-pool threads, never on the UI)
    // -------------------------------------------------------------------

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        // Marshal to the UI thread BEFORE we touch any state. The
        // GetCurrentSession() call itself is safe off-thread, but
        // SetCurrentSessionAsync mutates fields and ultimately
        // publishes to subscribers that bind to the dispatcher.
        Post(() => _ = HandleCurrentSessionChangedAsync());
    }

    private async Task HandleCurrentSessionChangedAsync()
    {
        if (_disposed) return;

        var manager = _manager;
        if (manager is null) return;

        var next = manager.GetCurrentSession();
        await SetCurrentSessionAsync(next).ConfigureAwait(true);
    }

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

    // -------------------------------------------------------------------
    // Async property reads - generation guarded
    // -------------------------------------------------------------------

    private async Task RefreshMediaPropertiesAsync(
        GlobalSystemMediaTransportControlsSession session, int generation)
    {
        try
        {
            var props = await session.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(true);
            if (!IsCurrentGeneration(generation)) return;
            if (props is null) return;

            // Decode artwork via the (Task 4) pipeline. Stubbed for
            // now to keep this commit scope-bounded.
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
            // SMTC may throw transient COM errors when the source
            // app is torn down mid-read. Swallow and let the next
            // authoritative event try again. Logging is owned by
            // Task 9.
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
        catch (Exception)
        {
            // Same rationale as RefreshMediaPropertiesAsync.
        }
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
        catch (Exception)
        {
            // Same rationale.
        }
    }

    /// <summary>
    /// Decodes the SMTC media-properties thumbnail via
    /// <see cref="ThumbnailDecoder"/>. The runtime class
    /// <c>IRandomAccessStreamReference</c> is projected into a
    /// managed <c>Stream</c> here so the decoder itself stays
    /// testable without a live SMTC session.
    /// </summary>
    /// <param name="thumbnail">
    /// <see cref="GlobalSystemMediaTransportControlsSessionMediaProperties.Thumbnail"/>
    /// typed as <see cref="object"/> to avoid forcing a WinRT-using
    /// directive on every file that touches this method. SMTC may
    /// return null when the source app has not populated artwork.
    /// </param>
    private static Task<ImageSource?> DecodeArtworkAsync(object? thumbnail)
    {
        if (thumbnail is null)
        {
            return Task.FromResult<ImageSource?>(null);
        }

        // Adapter: IRandomAccessStreamReference.OpenReadAsync()
        // returns IAsyncOperation<IRandomAccessStreamWithContentType>;
        // we convert to IAsyncOperation<IInputStream>, project to
        // Task<IRandomAccessStream>, then to a managed Stream.
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

    // -------------------------------------------------------------------
    // Publish path - all UI thread, all generation-guarded
    // -------------------------------------------------------------------

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
            // Task 4 will plumb a frozen ImageSource through here.
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
                CanGoNext:     c.IsNextEnabled)
            : null;

        var playbackInfoShape = new MediaPropertyMapper.PlaybackInfoShape(info.PlaybackStatus, controlsShape);

        var previous = Volatile.Read(ref _currentSnapshot);
        // Keep the previous timeline - a playback-only update should
        // not reset position/start/end. Use the previous timeline's
        // own LastUpdated as the baseline if we had one.
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
        // Keep the previous playback status/controls - a timeline-
        // only update should not flip state or capabilities.
        var playbackInfoShape = new MediaPropertyMapper.PlaybackInfoShape(
            Status: PlaybackStateToSmts(previous.Playback.State),
            Controls: new MediaPropertyMapper.ControlsShape(
                CanPlay:       previous.Playback.Capabilities.CanPlay,
                CanPause:      previous.Playback.Capabilities.CanPause,
                CanStop:       previous.Playback.Capabilities.CanStop,
                CanGoPrevious: previous.Playback.Capabilities.CanGoPrevious,
                CanGoNext:     previous.Playback.Capabilities.CanGoNext));

        var playback = MediaPropertyMapper.BuildPlaybackSnapshot(
            playbackInfo: playbackInfoShape,
            timeline: timelineShape,
            capturedAt: timelineShape.LastUpdated);

        var next = previous with { Playback = playback };

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

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private bool IsCurrentGeneration(int generation)
    {
        var current = Volatile.Read(ref _generation);
        return generation == current;
    }

    /// <summary>
    /// Marshals <paramref name="callback"/> to the captured
    /// synchronization context. Used by every WinRT event handler so
    /// that state mutation happens on the UI thread.
    /// </summary>
    private void Post(Action callback)
    {
        if (_disposed) return;

        // _context.Post never blocks the caller; if the context has
        // been shut down (e.g. the dispatcher closed) the callback
        // is silently dropped, which matches our semantics.
        _context.Post(_ =>
        {
            if (_disposed) return;
            callback();
        }, null);
    }

    /// <summary>
    /// Wraps the WinRT-specific dispatch path with the three guards
    /// (no-session, capability short-circuit, failed-try refresh).
    /// The pure guard logic lives in
    /// <see cref="DispatchGuardedCommandAsync"/>, which the unit
    /// tests exercise without a live WinRT session.
    /// </summary>
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

    /// <summary>
    /// Pure guard logic for a transport command. Used both by the
    /// production path (via <see cref="InvokeOnSessionAsync"/>) and
    /// by unit tests, which pass delegate-based fakes for
    /// <paramref name="tryCommand"/> and <paramref name="refresh"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three guards run in order:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <b>Disposed short-circuit.</b> If the service has been
    ///     disposed, returns immediately.
    ///   </item>
    ///   <item>
    ///     <b>Capability short-circuit.</b> Reads the cached
    ///     <see cref="TransportCapabilities"/> from
    ///     <see cref="_currentSnapshot"/> and skips the dispatch
    ///     when <paramref name="capability"/> returns false. This is
    ///     the second line of defence below <c>canExecute</c>, and
    ///     necessary for headless callers that do not consult the
    ///     UI.
    ///   </item>
    ///   <item>
    ///     <b>Failed-try refresh.</b> A <c>false</c> return or
    ///     thrown exception from <paramref name="tryCommand"/>
    ///     triggers <paramref name="refresh"/>, which re-reads
    ///     playback info on the captured session, republishes the
    ///     snapshot, and (through the view-model's
    ///     <c>RaiseCanExecuteChanged</c>) refreshes the button
    ///     state.
    ///   </item>
    /// </list>
    /// <para>
    /// Exposed as <c>internal</c> so unit tests can drive every
    /// branch without a WinRT session.
    /// </para>
    /// </remarks>
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
            // Source app rejected the command (e.g. session
            // closing mid-dispatch). Schedule a playback refresh so
            // the next authoritative state is observed promptly.
            refresh();
            return;
        }

        if (!success)
        {
            // Session refused the command - capability may have
            // flipped, the session may be tearing down. Refresh.
            refresh();
        }
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
        }

        if (session is not null)
        {
            session.MediaPropertiesChanged    -= OnMediaPropertiesChanged;
            session.PlaybackInfoChanged       -= OnPlaybackInfoChanged;
            session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        // Best-effort: bump the generation so any in-flight
        // continuations drop their results.
        Volatile.Write(ref _generation, _generation + 1);

        // Hand back to the caller's task scheduler. The service
        // has no managed resources to release beyond the lock.
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
