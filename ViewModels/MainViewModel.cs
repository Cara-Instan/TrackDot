using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TrackDot.Commands;
using TrackDot.Models;
using TrackDot.Services;

namespace TrackDot.ViewModels;

/// <summary>
/// The main popover view-model. Subscribes to
/// <see cref="IMediaControllerService.SnapshotChanged"/>, mirrors
/// the snapshot into bindable properties, owns the four transport
/// <see cref="AsyncRelayCommand"/> instances, and drives
/// <see cref="ProgressInterpolator"/> while the popover is visible
/// and playback is <see cref="MediaPlaybackState.Playing"/>.
/// </summary>
/// <remarks>
/// <para>
/// The view-model is the only place that translates empty strings
/// from the snapshot into user-facing defaults (e.g. "Nothing
/// playing" for an empty title). The mapper (Task 3) keeps empty
/// strings as empty so the view-model can apply its own rules.
/// </para>
/// <para>
/// The 250 ms tick is provided by <see cref="IUiTicker"/>. The
/// production implementation is a
/// <c>System.Windows.Threading.DispatcherTimer</c>; tests inject a
/// fake. The tick is started when the popover becomes visible AND
/// the latest snapshot is <see cref="MediaPlaybackState.Playing"/>,
/// and stopped otherwise. A new authoritative snapshot always
/// restarts the tick from the new baseline.
/// </para>
/// <para>
/// All public members are safe to call from the WPF UI thread.
/// The service marshals its callbacks back to the UI thread
/// internally; the view-model does no further marshalling.
/// </para>
/// </remarks>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private const string NothingPlayingText = "Nothing playing";

    private readonly IMediaControllerService _service;
    private readonly IUiTicker _ticker;
    private readonly Func<DateTimeOffset> _clock;

    private MediaSessionSnapshot _snapshot = MediaSessionSnapshot.Empty;
    private bool _isVisible;
    private bool _disposed;

    /// <summary>
    /// Backing field for <see cref="IsVisible"/>. Exposed so the
    /// popover's show/hide handler can toggle the timer without
    /// re-publishing a snapshot.
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            UpdateTicker();
        }
    }

    /// <summary>User-facing title. Empty in the snapshot becomes "Nothing playing".</summary>
    public string Title => string.IsNullOrEmpty(_snapshot.Title) ? NothingPlayingText : _snapshot.Title;

    /// <summary>User-facing artist. Empty in the snapshot becomes the empty string.</summary>
    public string Artist => _snapshot.Artist;

    /// <summary>User-facing album. Empty in the snapshot becomes the empty string.</summary>
    public string AlbumTitle => _snapshot.AlbumTitle;

    /// <summary>Decoded artwork (already frozen by the service). Null until a snapshot supplies it.</summary>
    public System.Windows.Media.ImageSource? Artwork => _snapshot.Artwork;

    /// <summary>The AUMID of the source app (Spotify, Chrome, etc.). Null when there is no active session.</summary>
    public string? SourceAppUserModelId => _snapshot.SourceAppUserModelId;

    /// <summary>True when the latest snapshot is <see cref="MediaPlaybackState.Playing"/>.</summary>
    public bool IsPlaying => _snapshot.Playback.State == MediaPlaybackState.Playing;

    /// <summary>True when there is an active media session with metadata.</summary>
    public bool HasMedia => !ReferenceEquals(_snapshot, MediaSessionSnapshot.Empty)
        && !string.IsNullOrEmpty(_snapshot.Title);

    /// <summary>Position in seconds, clamped to <c>[0, DurationSeconds]</c>.</summary>
    public double PositionSeconds
    {
        get
        {
            // If the popover is visible AND we're playing, return
            // the interpolated value. Otherwise return the
            // snapshot's last-known position, clamped.
            if (_isVisible && IsPlaying)
            {
                var interpolated = ProgressInterpolator.Evaluate(
                    state: _snapshot.Playback.State,
                    baselinePosition: _snapshot.Playback.Position,
                    baselineTimestamp: _snapshot.Playback.TimelineUpdatedAt,
                    endTime: _snapshot.Playback.EndTime,
                    now: _clock());
                return ClampPositionToSeconds(interpolated);
            }
            return ClampPositionToSeconds(_snapshot.Playback.Position);
        }
    }

    /// <summary>Duration in seconds. Zero when the source has not reported a duration.</summary>
    public double DurationSeconds => _snapshot.Playback.EndTime.TotalSeconds;

    /// <summary>Elapsed time as text, e.g. "1:23".</summary>
    public string ElapsedTimeText => FormatTime(TimeSpan.FromSeconds(PositionSeconds));

    /// <summary>Total duration as text, e.g. "4:56".</summary>
    public string DurationTimeText => FormatTime(_snapshot.Playback.EndTime);

    /// <summary>Previous track.</summary>
    public AsyncRelayCommand PreviousCommand { get; }

    /// <summary>Play / pause. Enabled when <c>CanPlay || CanPause</c>.</summary>
    public AsyncRelayCommand TogglePlayPauseCommand { get; }

    /// <summary>Stop. Enabled when <c>CanStop</c>.</summary>
    public AsyncRelayCommand StopCommand { get; }

    /// <summary>Next track.</summary>
    public AsyncRelayCommand NextCommand { get; }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Creates the view-model. Production code passes a
    /// <see cref="DispatcherUiTicker"/> and the default
    /// <c>() =&gt; DateTimeOffset.UtcNow</c> clock. Tests inject a
    /// fake ticker and a deterministic clock.
    /// </summary>
    public MainViewModel(
        IMediaControllerService service,
        IUiTicker ticker,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(ticker);
        _service = service;
        _ticker = ticker;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        // Build the four commands. canExecute is re-evaluated
        // every time the user clicks (the command's latch blocks
        // overlapping clicks), and re-evaluation is forced by
        // RaiseCanExecuteChanged() inside OnSnapshot — see below.
        // Use the parameterless ctor: these commands don't bind a
        // CommandParameter from XAML, and the parameterless form
        // sidesteps the lambda forwarding.
        PreviousCommand = new AsyncRelayCommand(
            execute: () => _service.PreviousAsync(),
            canExecute: () => _snapshot.Playback.Capabilities.CanGoPrevious);

        TogglePlayPauseCommand = new AsyncRelayCommand(
            execute: () => _service.TogglePlayPauseAsync(),
            // Mirror the service-side gate (Task 5b gotcha #4):
            // CanPlay || CanPause. Splitting these into separate
            // buttons would diverge from the guard.
            canExecute: () => _snapshot.Playback.Capabilities.CanPlay
                          || _snapshot.Playback.Capabilities.CanPause);

        StopCommand = new AsyncRelayCommand(
            execute: () => _service.StopAsync(),
            canExecute: () => _snapshot.Playback.Capabilities.CanStop);

        NextCommand = new AsyncRelayCommand(
            execute: () => _service.NextAsync(),
            canExecute: () => _snapshot.Playback.Capabilities.CanGoNext);

        _service.SnapshotChanged += OnSnapshot;
    }

    private void OnSnapshot(object? sender, MediaSessionSnapshot snapshot)
    {
        if (_disposed) return;

        _snapshot = snapshot;
        UpdateTicker();
        RaiseAllChanged();
        RaiseCommandStates();
    }

    /// <summary>
    /// Start the tick when the popover is visible AND playback is
    /// <see cref="MediaPlaybackState.Playing"/>. Stop otherwise.
    /// Restarting on every snapshot ensures the baseline the tick
    /// reads is always the latest authoritative one.
    /// </summary>
    private void UpdateTicker()
    {
        if (_isVisible && IsPlaying)
        {
            _ticker.Start(OnTick);
        }
        else
        {
            _ticker.Stop();
        }
    }

    private void OnTick()
    {
        if (_disposed) return;
        // Re-evaluate the ticker first: if the user hid the
        // popover or the source paused during the dispatch, stop
        // the timer instead of leaving it running.
        UpdateTicker();
        // Notify just the position properties — the rest of the
        // snapshot hasn't changed.
        OnPropertyChanged(nameof(PositionSeconds));
        OnPropertyChanged(nameof(ElapsedTimeText));
    }

    private void RaiseAllChanged()
    {
        // The exhaustive list of properties derived from the
        // snapshot. Adding a new bindable property here is the
        // only place to update.
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(AlbumTitle));
        OnPropertyChanged(nameof(Artwork));
        OnPropertyChanged(nameof(SourceAppUserModelId));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(PositionSeconds));
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(ElapsedTimeText));
        OnPropertyChanged(nameof(DurationTimeText));
    }

    private void RaiseCommandStates()
    {
        PreviousCommand.RaiseCanExecuteChanged();
        TogglePlayPauseCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private double ClampPositionToSeconds(TimeSpan position)
    {
        if (position < TimeSpan.Zero) return 0d;
        var end = _snapshot.Playback.EndTime;
        if (end > TimeSpan.Zero && position > end) return end.TotalSeconds;
        return position.TotalSeconds;
    }

    /// <summary>
    /// Test-visible formatter. Same format as
    /// <see cref="TrackDot.Converters.TimeSpanTextConverter"/>:
    /// <c>m:ss</c> under an hour, <c>h:mm:ss</c> over.
    /// </summary>
    internal static string FormatTime(TimeSpan ts)
        => TrackDot.Converters.MainViewModelHelpers.FormatTime(ts);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ticker.Stop();
        _service.SnapshotChanged -= OnSnapshot;
    }
}
