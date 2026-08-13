using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TrackDot.Commands;
using TrackDot.Models;
using TrackDot.Services;

namespace TrackDot.ViewModels;

/// <summary>
/// ViewModel for the resizable, sticky lyrics window.
/// Manages lyric fetching, line-syncing with media playback,
/// furigana toggle, font scaling, and persistence settings.
/// </summary>
public sealed class LyricsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMediaControllerService _mediaService;
    private readonly ILyricsService _lyricsService;
    private readonly IUiTicker _ticker;
    private readonly IWindowSettingsService _settingsService;
    private readonly Func<DateTimeOffset> _clock;

    private CancellationTokenSource? _fetchCts;
    private IReadOnlyList<LyricLine> _lines = Array.Empty<LyricLine>();
    private int _activeLineIndex = -1;
    private bool _isLoading;
    private string _lastTrackKey = string.Empty;
    private double _windowHeight = 580.0;
    private bool _disposed;

    public IReadOnlyList<LyricLine> Lines
    {
        get => _lines;
        private set
        {
            if (ReferenceEquals(_lines, value)) return;
            _lines = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLyrics));
        }
    }

    public int ActiveLineIndex
    {
        get => _activeLineIndex;
        private set
        {
            if (_activeLineIndex == value) return;
            _activeLineIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveLine));
        }
    }

    public LyricLine? ActiveLine =>
        _activeLineIndex >= 0 && _activeLineIndex < _lines.Count ? _lines[_activeLineIndex] : null;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public bool HasLyrics => _lines.Count > 0;

    public string DisplayTitle => string.IsNullOrWhiteSpace(_mediaService.Current.Title)
        ? "No Track Playing"
        : _mediaService.Current.Title;

    public string DisplayArtist => _mediaService.Current.Artist;

    public bool IsFuriganaVisible
    {
        get => _settingsService.LyricsIsFuriganaVisible;
        set
        {
            if (_settingsService.LyricsIsFuriganaVisible == value) return;
            _settingsService.LyricsIsFuriganaVisible = value;
            OnPropertyChanged();
        }
    }

    public double OpacityPercent
    {
        get => _settingsService.LyricsOpacityPercent;
        set
        {
            if (Math.Abs(_settingsService.LyricsOpacityPercent - value) < 0.1) return;
            _settingsService.LyricsOpacityPercent = (int)Math.Round(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowOpacity));
        }
    }

    public double WindowOpacity => OpacityPercent / 100.0;

    public bool IsTopmost
    {
        get => _settingsService.LyricsIsTopmost;
        set
        {
            if (_settingsService.LyricsIsTopmost == value) return;
            _settingsService.LyricsIsTopmost = value;
            OnPropertyChanged();
        }
    }

    public double BaseFontSize => Math.Clamp(_windowHeight / 22.0, 14.0, 48.0);
    public double ActiveFontSize => BaseFontSize * 1.3;
    public double RubyFontSize => Math.Max(10.0, BaseFontSize * 0.55);

    public AsyncRelayCommand ToggleFuriganaCommand { get; }
    public AsyncRelayCommand ToggleTopmostCommand { get; }
    public AsyncRelayCommand<LyricLine> SeekToLineCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LyricsViewModel(
        IMediaControllerService mediaService,
        ILyricsService lyricsService,
        IUiTicker ticker,
        IWindowSettingsService settingsService,
        Func<DateTimeOffset>? clock = null)
    {
        _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
        _lyricsService = lyricsService ?? throw new ArgumentNullException(nameof(lyricsService));
        _ticker = ticker ?? throw new ArgumentNullException(nameof(ticker));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        ToggleFuriganaCommand = new AsyncRelayCommand(
            execute: () =>
            {
                IsFuriganaVisible = !IsFuriganaVisible;
                return Task.CompletedTask;
            });

        ToggleTopmostCommand = new AsyncRelayCommand(
            execute: () =>
            {
                IsTopmost = !IsTopmost;
                return Task.CompletedTask;
            });

        SeekToLineCommand = new AsyncRelayCommand<LyricLine>(
            execute: async line =>
            {
                if (line != null)
                {
                    await _mediaService.SeekAsync(line.Timestamp.TotalSeconds).ConfigureAwait(false);
                }
            });

        _mediaService.SnapshotChanged += OnMediaSnapshotChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;

        _ticker.Start(OnTick);
        _ = LoadLyricsForCurrentTrackAsync();
    }

    public void UpdateWindowHeight(double height)
    {
        if (height <= 0 || Math.Abs(_windowHeight - height) < 0.5) return;
        _windowHeight = height;
        OnPropertyChanged(nameof(BaseFontSize));
        OnPropertyChanged(nameof(ActiveFontSize));
        OnPropertyChanged(nameof(RubyFontSize));
    }

    private void OnMediaSnapshotChanged(object? sender, MediaSessionSnapshot snapshot)
    {
        if (_disposed) return;

        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplayArtist));

        string currentTrackKey = $"{snapshot.Artist.Trim()} - {snapshot.Title.Trim()}";
        if (!string.Equals(_lastTrackKey, currentTrackKey, StringComparison.OrdinalIgnoreCase))
        {
            _lastTrackKey = currentTrackKey;
            _ = LoadLyricsForCurrentTrackAsync();
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(IsFuriganaVisible));
        OnPropertyChanged(nameof(OpacityPercent));
        OnPropertyChanged(nameof(WindowOpacity));
        OnPropertyChanged(nameof(IsTopmost));
    }

    private async Task LoadLyricsForCurrentTrackAsync()
    {
        if (_disposed) return;

        _fetchCts?.Cancel();
        _fetchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _fetchCts = cts;

        var snapshot = _mediaService.Current;
        if (string.IsNullOrWhiteSpace(snapshot.Title))
        {
            Lines = Array.Empty<LyricLine>();
            ActiveLineIndex = -1;
            IsLoading = false;
            return;
        }

        IsLoading = true;
        ActiveLineIndex = -1;

        try
        {
            var result = await _lyricsService.FetchLyricsAsync(
                title: snapshot.Title,
                artist: snapshot.Artist,
                album: snapshot.AlbumTitle,
                duration: snapshot.Playback.EndTime,
                cancellationToken: cts.Token).ConfigureAwait(true);

            if (!cts.Token.IsCancellationRequested)
            {
                Lines = result;
                IsLoading = false;
                UpdateActiveLine();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsViewModel] Load lyrics exception: {ex.Message}");
            if (!cts.Token.IsCancellationRequested)
            {
                Lines = Array.Empty<LyricLine>();
                IsLoading = false;
            }
        }
    }

    private void OnTick()
    {
        if (_disposed) return;
        UpdateActiveLine();
    }

    private void UpdateActiveLine()
    {
        if (_lines.Count == 0)
        {
            ActiveLineIndex = -1;
            return;
        }

        var snapshot = _mediaService.Current;
        double positionSec = snapshot.Playback.Position.TotalSeconds;

        if (snapshot.Playback.State == MediaPlaybackState.Playing)
        {
            var interpolated = ProgressInterpolator.Evaluate(
                state: snapshot.Playback.State,
                baselinePosition: snapshot.Playback.Position,
                baselineTimestamp: snapshot.Playback.TimelineUpdatedAt,
                endTime: snapshot.Playback.EndTime,
                now: _clock());
            positionSec = interpolated.TotalSeconds;
        }

        int foundIndex = -1;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].Timestamp.TotalSeconds <= positionSec)
            {
                foundIndex = i;
            }
            else
            {
                break;
            }
        }

        ActiveLineIndex = foundIndex;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ticker.Stop();
        _fetchCts?.Cancel();
        _fetchCts?.Dispose();
        _mediaService.SnapshotChanged -= OnMediaSnapshotChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;
    }
}
