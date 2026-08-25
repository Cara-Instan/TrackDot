using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TrackDot.Commands;
using TrackDot.Models;
using TrackDot.Services;

namespace TrackDot.ViewModels;

/// <summary>
/// The main popover view-model. Subscribes to
/// <see cref="IMediaControllerService.SnapshotChanged"/> and
/// <see cref="IMediaControllerService.SessionListChanged"/>, mirrors
/// both into bindable properties, owns transport and volume
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
    private readonly IWindowSettingsService? _windowSettingsService;

    private MediaSessionSnapshot _snapshot = MediaSessionSnapshot.Empty;
    private System.Windows.Media.Color? _dominantArtworkColor;
    private System.Windows.Media.SolidColorBrush? _dynamicAccentBrush;
    private System.Windows.Media.Brush? _artworkAmbientGlowBrush;
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
            if (_isVisible)
            {
                _ = _service.RefreshVolumeAsync();
            }
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

    /// <summary>Extracted dominant color from current artwork (if available).</summary>
    public System.Windows.Media.Color? DominantArtworkColor => _dominantArtworkColor;

    /// <summary>Dynamic accent brush derived from album artwork when dynamic tinting is enabled.</summary>
    public System.Windows.Media.Brush? DynamicAccentBrush => (_windowSettingsService?.EnableDynamicTinting ?? true)
        ? _dynamicAccentBrush
        : null;

    /// <summary>Ambient background glow radial brush derived from artwork color.</summary>
    public System.Windows.Media.Brush? ArtworkAmbientGlowBrush => (_windowSettingsService?.EnableDynamicTinting ?? true)
        ? _artworkAmbientGlowBrush
        : null;

    /// <summary>True when a dynamic accent brush is active.</summary>
    public bool HasDynamicAccent => DynamicAccentBrush != null;

    /// <summary>The AUMID of the source app (Spotify, Chrome, etc.). Null when there is no active session.</summary>
    public string? SourceAppUserModelId => _snapshot.SourceAppUserModelId;

    /// <summary>Formatted human-readable application name parsed from SourceAppUserModelId.</summary>
    public string SourceAppName => string.IsNullOrWhiteSpace(_snapshot.SourceAppUserModelId)
        ? string.Empty
        : MainViewModelHelpers.FormatAppName(_snapshot.SourceAppUserModelId);

    /// <summary>True when the latest snapshot is <see cref="MediaPlaybackState.Playing"/>.</summary>
    public bool IsPlaying => _snapshot.Playback.State == MediaPlaybackState.Playing;

    /// <summary>Segoe icon glyph for play (\uE768) or pause (\uE769).</summary>
    public string PlayPauseIcon => IsPlaying ? "\uE769" : "\uE768";

    /// <summary>Vector path geometry string for play or pause icon.</summary>
    public string PlayPausePathData => IsPlaying
        ? "M 3,2 H 7 V 18 H 3 Z M 13,2 H 17 V 18 H 13 Z"
        : "M 4,2 L 18,10 L 4,18 Z";

    /// <summary>Tool tip text for Play / Pause action.</summary>
    public string PlayPauseToolTip => IsPlaying ? "Pause" : "Play";

    /// <summary>True when there is an active media session with metadata.</summary>
    public bool HasMedia => !ReferenceEquals(_snapshot, MediaSessionSnapshot.Empty)
        && !string.IsNullOrEmpty(_snapshot.Title);

    /// <summary>Position in seconds, clamped to <c>[0, DurationSeconds]</c>.</summary>
    public double PositionSeconds
    {
        get
        {
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

    /// <summary>True when seeking is supported by the active media session.</summary>
    public bool CanSeek => _snapshot.Playback.Capabilities.CanSeek || DurationSeconds > 0;

    private bool _isUserSeeking;

    /// <summary>
    /// True when the user is actively dragging or holding the seek slider.
    /// When true, the UI ticker skips updating <see cref="PositionSeconds"/>
    /// so the slider thumb does not jump back to the interpolated time.
    /// </summary>
    public bool IsUserSeeking
    {
        get => _isUserSeeking;
        set
        {
            if (_isUserSeeking == value) return;
            _isUserSeeking = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Duration in seconds. Zero when the source has not reported a duration.</summary>
    public double DurationSeconds => _snapshot.Playback.EndTime.TotalSeconds;

    /// <summary>Elapsed time as text, e.g. "1:23".</summary>
    public string ElapsedTimeText => FormatTime(TimeSpan.FromSeconds(PositionSeconds));

    /// <summary>Total duration as text, e.g. "4:56".</summary>
    public string DurationTimeText => FormatTime(_snapshot.Playback.EndTime);

    // ── Feature 9 — Session Picker ──────────────────────────────────────────

    /// <summary>
    /// The live list of available SMTC sessions. Forwarded directly from
    /// <see cref="IMediaControllerService.AvailableSessions"/>; raises
    /// <see cref="INotifyPropertyChanged.PropertyChanged"/> whenever
    /// <see cref="IMediaControllerService.SessionListChanged"/> fires.
    /// </summary>
    public IReadOnlyList<MediaSessionInfo> AvailableSessions => _service.AvailableSessions;

    /// <summary>
    /// <see langword="true"/> when two or more SMTC sessions are
    /// active simultaneously (e.g. Spotify + Chrome). Used to
    /// show / collapse the session-picker panel in the popover.
    /// </summary>
    public bool HasMultipleSessions => _service.AvailableSessions.Count > 1;

    // ── Feature 10 — Volume / Mute ───────────────────────────────────────────

    /// <summary>
    /// Current master volume of the SMTC source application, in [0.0, 1.0].
    /// Sourced from the snapshot's <see cref="MediaSessionSnapshot.Volume"/>.
    /// </summary>
    public double Volume => _snapshot.Volume;

    /// <summary>
    /// Volume expressed as an integer percentage [0–100] for binding to a
    /// Slider whose Maximum is 100.
    /// </summary>
    public double VolumePercent => Math.Round(_snapshot.Volume * 100.0);

    /// <summary>
    /// <see langword="true"/> when the audio session is muted.
    /// Sourced from <see cref="MediaSessionSnapshot.IsMuted"/>.
    /// </summary>
    public bool IsMuted => _snapshot.IsMuted;

    /// <summary>
    /// Speaker icon path data. Solid speaker when not muted; speaker
    /// with a cross when muted.
    /// </summary>
    public string MuteIconPathData => _snapshot.IsMuted
        ? "M 2,7 H 7 L 12,3 V 17 L 7,13 H 2 Z M 16,9 L 20,13 M 20,9 L 16,13"  // speaker + X
        : "M 2,7 H 7 L 12,3 V 17 L 7,13 H 2 Z M 14,6 Q 17,10 14,14 M 15,4 Q 20,10 15,16"; // speaker + waves

    /// <summary>Tool tip for the mute toggle button.</summary>
    public string MuteToolTip => _snapshot.IsMuted ? "Unmute" : "Mute";

    // ── Shuffle & Repeat ──────────────────────────────────────────────────

    /// <summary>True when shuffle mode is active.</summary>
    public bool IsShuffleActive => _snapshot.Playback.IsShuffleActive == true;

    /// <summary>True when the active session supports changing shuffle mode.</summary>
    public bool CanChangeShuffle => _snapshot.Playback.Capabilities.CanChangeShuffle;

    /// <summary>Current repeat mode (None, Track, List).</summary>
    public MediaAutoRepeatMode AutoRepeatMode => _snapshot.Playback.AutoRepeatMode;

    /// <summary>True when repeat mode is Track or List.</summary>
    public bool IsRepeatActive => _snapshot.Playback.AutoRepeatMode != MediaAutoRepeatMode.None;

    /// <summary>True when repeat is set to repeat one track.</summary>
    public bool IsRepeatTrack => _snapshot.Playback.AutoRepeatMode == MediaAutoRepeatMode.Track;

    /// <summary>True when the active session supports changing repeat mode.</summary>
    public bool CanChangeAutoRepeatMode => _snapshot.Playback.Capabilities.CanChangeAutoRepeatMode;

    /// <summary>Vector path for shuffle button.</summary>
    public string ShufflePathData => "M 10.59,9.17 L 5.41,4 L 4,5.41 L 9.17,10.59 L 10.59,9.17 Z M 14.5,4 L 16.54,6.04 L 4,18.59 L 5.41,20 L 17.96,7.46 L 20,9.5 V 4 H 14.5 Z M 14.5,20 H 20 V 14.5 L 17.96,16.54 L 13.41,12 L 12,13.41 L 16.54,17.96 L 14.5,20 Z";

    /// <summary>Vector path for repeat button.</summary>
    public string RepeatPathData => AutoRepeatMode == MediaAutoRepeatMode.Track
        ? "M 7,7 H 17 V 10 L 21,6 L 17,2 V 5 H 5 V 11 H 7 V 7 Z M 17,17 H 7 V 14 L 3,18 L 7,22 V 19 H 19 V 13 H 17 V 17 Z M 11.2,15 V 9.5 L 10,10.2 V 9.1 L 11.2,8.3 H 12.3 V 15 H 11.2 Z"
        : "M 7,7 H 17 V 10 L 21,6 L 17,2 V 5 H 5 V 11 H 7 V 7 Z M 17,17 H 7 V 14 L 3,18 L 7,22 V 19 H 19 V 13 H 17 V 17 Z";

    /// <summary>Tool tip text for Shuffle button.</summary>
    public string ShuffleToolTip => IsShuffleActive ? "Shuffle: On" : "Shuffle: Off";

    /// <summary>Tool tip text for Repeat button.</summary>
    public string RepeatToolTip => AutoRepeatMode switch
    {
        MediaAutoRepeatMode.Track => "Repeat: One track",
        MediaAutoRepeatMode.List  => "Repeat: All tracks",
        _                         => "Repeat: Off",
    };

    // ── Transport commands ───────────────────────────────────────────────────

    /// <summary>Toggles shuffle mode.</summary>
    public AsyncRelayCommand ToggleShuffleCommand { get; }

    /// <summary>Cycles repeat mode.</summary>
    public AsyncRelayCommand CycleRepeatCommand { get; }

    /// <summary>Previous track.</summary>
    public AsyncRelayCommand PreviousCommand { get; }

    /// <summary>Play / pause. Enabled when <c>CanPlay || CanPause</c>.</summary>
    public AsyncRelayCommand TogglePlayPauseCommand { get; }

    /// <summary>Stop. Enabled when <c>CanStop</c>.</summary>
    public AsyncRelayCommand StopCommand { get; }

    /// <summary>Next track.</summary>
    public AsyncRelayCommand NextCommand { get; }

    /// <summary>
    /// Seek command. Bound to the progress <see cref="Slider"/> value.
    /// Accepts a <c>double</c> (seconds) from the slider thumb.
    /// </summary>
    public AsyncRelayCommand<double> SeekCommand { get; }

    // ── Session-picker commands ──────────────────────────────────────────────

    /// <summary>
    /// Pins a session by AUMID. Bound to each session-picker button;
    /// <c>CommandParameter</c> is the session's
    /// <see cref="MediaSessionInfo.SourceAppUserModelId"/>.
    /// </summary>
    public AsyncRelayCommand<string> SelectSessionCommand { get; }

    // ── Volume commands ──────────────────────────────────────────────────────

    /// <summary>
    /// Sets the master volume. Accepts a <c>double</c> in [0, 100]
    /// (percent) from the volume slider; divides by 100 before
    /// forwarding to the service.
    /// </summary>
    public AsyncRelayCommand<double> SetVolumeCommand { get; }

    /// <summary>Toggles mute on the current audio session.</summary>
    public AsyncRelayCommand ToggleMuteCommand { get; }

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
        Func<DateTimeOffset>? clock = null,
        IWindowSettingsService? windowSettingsService = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(ticker);
        _service = service;
        _ticker = ticker;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _windowSettingsService = windowSettingsService;

        if (_windowSettingsService != null)
        {
            _windowSettingsService.SettingsChanged += OnSettingsChanged;
        }

        ToggleShuffleCommand = new AsyncRelayCommand(
            execute: () => _service.ToggleShuffleAsync(),
            canExecute: () => HasMedia);

        CycleRepeatCommand = new AsyncRelayCommand(
            execute: () => _service.CycleRepeatModeAsync(),
            canExecute: () => HasMedia);

        PreviousCommand = new AsyncRelayCommand(
            execute: () => _service.PreviousAsync(),
            canExecute: () => _snapshot.Playback.Capabilities.CanGoPrevious);

        TogglePlayPauseCommand = new AsyncRelayCommand(
            execute: () => _service.TogglePlayPauseAsync(),
            canExecute: () => _snapshot.Playback.Capabilities.CanPlay
                           || _snapshot.Playback.Capabilities.CanPause);

        StopCommand = new AsyncRelayCommand(
            execute: () => _service.StopAsync(),
            canExecute: () => _snapshot.Playback.Capabilities.CanStop);

        NextCommand = new AsyncRelayCommand(
            execute: () => _service.NextAsync(),
            canExecute: () => _snapshot.Playback.Capabilities.CanGoNext);

        SeekCommand = new AsyncRelayCommand<double>(
            execute: seconds => _service.SeekAsync(seconds),
            canExecute: _ => CanSeek);

        SelectSessionCommand = new AsyncRelayCommand<string>(
            execute: aumid => _service.SelectSessionAsync(aumid ?? string.Empty),
            canExecute: _ => true);

        SetVolumeCommand = new AsyncRelayCommand<double>(
            execute: pct => _service.SetVolumeAsync(pct / 100.0),
            canExecute: _ => HasMedia);

        ToggleMuteCommand = new AsyncRelayCommand(
            execute: () => _service.ToggleMuteAsync(),
            canExecute: () => HasMedia);

        _service.SnapshotChanged   += OnSnapshot;
        _service.SessionListChanged += OnSessionListChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(DynamicAccentBrush));
        OnPropertyChanged(nameof(ArtworkAmbientGlowBrush));
        OnPropertyChanged(nameof(HasDynamicAccent));
    }

    private void OnSnapshot(object? sender, MediaSessionSnapshot snapshot)
    {
        if (_disposed) return;

        bool artworkChanged = !ReferenceEquals(_snapshot.Artwork, snapshot.Artwork);
        _snapshot = snapshot;

        if (artworkChanged)
        {
            UpdateDynamicArtworkColor();
        }

        UpdateTicker();
        RaiseAllChanged();
        RaiseCommandStates();
    }

    private void UpdateDynamicArtworkColor()
    {
        var color = ColorExtractor.ExtractDominantColor(_snapshot.Artwork);
        if (_dominantArtworkColor != color)
        {
            _dominantArtworkColor = color;
            if (color.HasValue)
            {
                var c = color.Value;
                var accent = new System.Windows.Media.SolidColorBrush(c);
                accent.Freeze();
                _dynamicAccentBrush = accent;

                var glow = new System.Windows.Media.RadialGradientBrush(
                    System.Windows.Media.Color.FromArgb(90, c.R, c.G, c.B),
                    System.Windows.Media.Color.FromArgb(0, c.R, c.G, c.B))
                {
                    Center = new System.Windows.Point(0.2, 0.2),
                    GradientOrigin = new System.Windows.Point(0.2, 0.2),
                    RadiusX = 0.85,
                    RadiusY = 0.85
                };
                glow.Freeze();
                _artworkAmbientGlowBrush = glow;
            }
            else
            {
                _dynamicAccentBrush = null;
                _artworkAmbientGlowBrush = null;
            }
        }
        OnPropertyChanged(nameof(DominantArtworkColor));
        OnPropertyChanged(nameof(DynamicAccentBrush));
        OnPropertyChanged(nameof(ArtworkAmbientGlowBrush));
        OnPropertyChanged(nameof(HasDynamicAccent));
    }

    private void OnSessionListChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(AvailableSessions));
        OnPropertyChanged(nameof(HasMultipleSessions));
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
        UpdateTicker();
        if (!_isUserSeeking)
        {
            OnPropertyChanged(nameof(PositionSeconds));
            OnPropertyChanged(nameof(ElapsedTimeText));
        }
    }

    private void RaiseAllChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(AlbumTitle));
        OnPropertyChanged(nameof(Artwork));
        OnPropertyChanged(nameof(SourceAppUserModelId));
        OnPropertyChanged(nameof(SourceAppName));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayPauseIcon));
        OnPropertyChanged(nameof(PlayPausePathData));
        OnPropertyChanged(nameof(PlayPauseToolTip));
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(CanSeek));
        OnPropertyChanged(nameof(PositionSeconds));
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(ElapsedTimeText));
        OnPropertyChanged(nameof(DurationTimeText));
        // Shuffle & Repeat
        OnPropertyChanged(nameof(IsShuffleActive));
        OnPropertyChanged(nameof(CanChangeShuffle));
        OnPropertyChanged(nameof(AutoRepeatMode));
        OnPropertyChanged(nameof(IsRepeatActive));
        OnPropertyChanged(nameof(IsRepeatTrack));
        OnPropertyChanged(nameof(CanChangeAutoRepeatMode));
        OnPropertyChanged(nameof(ShufflePathData));
        OnPropertyChanged(nameof(RepeatPathData));
        OnPropertyChanged(nameof(ShuffleToolTip));
        OnPropertyChanged(nameof(RepeatToolTip));
        // Session picker
        OnPropertyChanged(nameof(AvailableSessions));
        OnPropertyChanged(nameof(HasMultipleSessions));
        // Volume / mute
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(MuteIconPathData));
        OnPropertyChanged(nameof(MuteToolTip));
    }

    private void RaiseCommandStates()
    {
        ToggleShuffleCommand.RaiseCanExecuteChanged();
        CycleRepeatCommand.RaiseCanExecuteChanged();
        PreviousCommand.RaiseCanExecuteChanged();
        TogglePlayPauseCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
        SeekCommand.RaiseCanExecuteChanged();
        SelectSessionCommand.RaiseCanExecuteChanged();
        SetVolumeCommand.RaiseCanExecuteChanged();
        ToggleMuteCommand.RaiseCanExecuteChanged();
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
        => MainViewModelHelpers.FormatTime(ts);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ticker.Stop();
        _service.SnapshotChanged    -= OnSnapshot;
        _service.SessionListChanged -= OnSessionListChanged;
        if (_windowSettingsService != null)
        {
            _windowSettingsService.SettingsChanged -= OnSettingsChanged;
        }
    }
}
