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
/// furigana toggle, dual-language translation, font scaling,
/// manual search, local file imports, and persistence settings.
/// </summary>
public sealed class LyricsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMediaControllerService _mediaService;
    private readonly ILyricsService _lyricsService;
    private readonly IUiTicker _ticker;
    private readonly IWindowSettingsService _settingsService;
    private readonly Func<DateTimeOffset> _clock;

    private CancellationTokenSource? _fetchCts;
    private CancellationTokenSource? _searchCts;
    private IReadOnlyList<LyricLine> _lines = Array.Empty<LyricLine>();
    private int _activeLineIndex = -1;
    private bool _isLoading;
    private string _lastTrackKey = string.Empty;
    private double _windowHeight = 580.0;
    private bool _disposed;
    private System.Windows.Media.Color? _dominantArtworkColor;
    private System.Windows.Media.SolidColorBrush? _dynamicAccentBrush;
    private System.Windows.Media.RadialGradientBrush? _artworkAmbientGlowBrush;

    private bool _isSearchPanelOpen;
    private string _searchQuery = string.Empty;
    private bool _isSearching;
    private IReadOnlyList<LyricsSearchResult> _searchResults = Array.Empty<LyricsSearchResult>();

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
            OnPropertyChanged(nameof(ActiveNextLine));
        }
    }

    public LyricLine? ActiveLine =>
        _activeLineIndex >= 0 && _activeLineIndex < _lines.Count ? _lines[_activeLineIndex] : null;

    public LyricLine? ActiveNextLine =>
        _activeLineIndex + 1 >= 0 && _activeLineIndex + 1 < _lines.Count ? _lines[_activeLineIndex + 1] : null;

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
    public string DisplayAlbum => _mediaService.Current.AlbumTitle;

    /// <summary>Extracted dominant color from current artwork (if available).</summary>
    public System.Windows.Media.Color? DominantArtworkColor => _dominantArtworkColor;

    /// <summary>Dynamic accent brush derived from album artwork when dynamic tinting is enabled.</summary>
    public System.Windows.Media.Brush? DynamicAccentBrush => (_settingsService?.EnableDynamicTinting ?? true)
        ? _dynamicAccentBrush
        : null;

    /// <summary>Ambient background glow radial brush derived from artwork color.</summary>
    public System.Windows.Media.Brush? ArtworkAmbientGlowBrush => (_settingsService?.EnableDynamicTinting ?? true)
        ? _artworkAmbientGlowBrush
        : null;

    /// <summary>True when a dynamic accent brush is active.</summary>
    public bool HasDynamicAccent => DynamicAccentBrush != null;

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

    public bool IsTranslationVisible
    {
        get => _settingsService.LyricsShowTranslation;
        set
        {
            if (_settingsService.LyricsShowTranslation == value) return;
            _settingsService.LyricsShowTranslation = value;
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

    private double _manualOffsetSeconds = 0.0;

    /// <summary>
    /// Manual timing offset in seconds added to playback position when evaluating current lyric line.
    /// Positive values make lyrics appear earlier; negative values delay lyrics.
    /// </summary>
    public double ManualOffsetSeconds
    {
        get => _manualOffsetSeconds;
        set
        {
            var clamped = Math.Clamp(value, -10.0, 10.0);
            if (Math.Abs(_manualOffsetSeconds - clamped) < 0.01) return;
            _manualOffsetSeconds = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OffsetDisplay));
            OnPropertyChanged(nameof(HasNonZeroOffset));
            UpdateActiveLine();
        }
    }

    /// <summary>Formatted offset display, e.g. "+0.5s", "-1.0s", "0.0s".</summary>
    public string OffsetDisplay => _manualOffsetSeconds switch
    {
        > 0.001 => $"+{_manualOffsetSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}s",
        < -0.001 => $"{_manualOffsetSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}s",
        _ => "0.0s"
    };

    /// <summary>True when a non-zero manual offset is active.</summary>
    public bool HasNonZeroOffset => Math.Abs(_manualOffsetSeconds) > 0.01;

    public double BaseFontSize => Math.Clamp(_windowHeight / 22.0, 14.0, 48.0);
    public double ActiveFontSize => BaseFontSize * 1.3;
    public double RubyFontSize => Math.Max(10.0, BaseFontSize * 0.55);

    #region Search Panel State
    public bool IsSearchPanelOpen
    {
        get => _isSearchPanelOpen;
        set
        {
            if (_isSearchPanelOpen == value) return;
            _isSearchPanelOpen = value;
            OnPropertyChanged();
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery == value) return;
            _searchQuery = value;
            OnPropertyChanged();
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (_isSearching == value) return;
            _isSearching = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<LyricsSearchResult> SearchResults
    {
        get => _searchResults;
        private set
        {
            if (ReferenceEquals(_searchResults, value)) return;
            _searchResults = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSearchResults));
        }
    }

    public bool HasSearchResults => _searchResults.Count > 0;
    #endregion

    public AsyncRelayCommand ToggleFuriganaCommand { get; }
    public AsyncRelayCommand ToggleTranslationCommand { get; }
    public AsyncRelayCommand ToggleTopmostCommand { get; }
    public AsyncRelayCommand<LyricLine> SeekToLineCommand { get; }
    public AsyncRelayCommand OffsetEarlierCommand { get; }
    public AsyncRelayCommand OffsetLaterCommand { get; }
    public AsyncRelayCommand ResetOffsetCommand { get; }

    public AsyncRelayCommand OpenSearchPanelCommand { get; }
    public AsyncRelayCommand CloseSearchPanelCommand { get; }
    public AsyncRelayCommand SearchLyricsCommand { get; }
    public AsyncRelayCommand<LyricsSearchResult> SelectSearchResultCommand { get; }
    public AsyncRelayCommand LoadLrcFileCommand { get; }

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

        OffsetEarlierCommand = new AsyncRelayCommand(
            execute: () =>
            {
                ManualOffsetSeconds -= 0.5;
                return Task.CompletedTask;
            });

        OffsetLaterCommand = new AsyncRelayCommand(
            execute: () =>
            {
                ManualOffsetSeconds += 0.5;
                return Task.CompletedTask;
            });

        ResetOffsetCommand = new AsyncRelayCommand(
            execute: () =>
            {
                ManualOffsetSeconds = 0.0;
                return Task.CompletedTask;
            });

        ToggleFuriganaCommand = new AsyncRelayCommand(
            execute: () =>
            {
                IsFuriganaVisible = !IsFuriganaVisible;
                return Task.CompletedTask;
            });

        ToggleTranslationCommand = new AsyncRelayCommand(
            execute: () =>
            {
                IsTranslationVisible = !IsTranslationVisible;
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

        OpenSearchPanelCommand = new AsyncRelayCommand(
            execute: () =>
            {
                SearchQuery = $"{_mediaService.Current.Title} {_mediaService.Current.Artist}".Trim();
                IsSearchPanelOpen = true;
                return SearchLyricsInternalAsync();
            });

        CloseSearchPanelCommand = new AsyncRelayCommand(
            execute: () =>
            {
                IsSearchPanelOpen = false;
                return Task.CompletedTask;
            });

        SearchLyricsCommand = new AsyncRelayCommand(
            execute: SearchLyricsInternalAsync);

        SelectSearchResultCommand = new AsyncRelayCommand<LyricsSearchResult>(
            execute: async candidate =>
            {
                if (candidate == null) return;
                await ApplySearchResultAsync(candidate).ConfigureAwait(false);
            });

        LoadLrcFileCommand = new AsyncRelayCommand(
            execute: () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Lyric files (*.lrc;*.ttml;*.txt)|*.lrc;*.ttml;*.txt|All files (*.*)|*.*",
                    Title = "Open Lyrics File"
                };

                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        string content = System.IO.File.ReadAllText(dialog.FileName);
                        return LoadCustomLyricsAsync(content);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LyricsViewModel] File read error: {ex.Message}");
                    }
                }
                return Task.CompletedTask;
            });

        _mediaService.SnapshotChanged += OnMediaSnapshotChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;

        UpdateDynamicArtworkColor();
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

    public async Task LoadCustomLyricsAsync(string rawContent, string? format = null)
    {
        if (_disposed || string.IsNullOrWhiteSpace(rawContent)) return;

        IsLoading = true;
        try
        {
            var parsed = await _lyricsService.ParseCustomLyricsAsync(rawContent, format).ConfigureAwait(true);
            if (parsed != null && parsed.Count > 0)
            {
                Lines = parsed;
                var snapshot = _mediaService.Current;
                if (!string.IsNullOrWhiteSpace(snapshot.Title))
                {
                    _lyricsService.SaveLyricsToCache(snapshot.Title, snapshot.Artist, snapshot.AlbumTitle, parsed);
                }
                IsLoading = false;
                IsSearchPanelOpen = false;
                UpdateActiveLine();
            }
            else
            {
                IsLoading = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsViewModel] Custom lyrics error: {ex.Message}");
            IsLoading = false;
        }
    }

    private async Task SearchLyricsInternalAsync()
    {
        if (_disposed || string.IsNullOrWhiteSpace(SearchQuery)) return;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchCts = cts;

        IsSearching = true;
        try
        {
            var results = await _lyricsService.SearchCandidatesAsync(SearchQuery, cts.Token).ConfigureAwait(true);
            if (!cts.Token.IsCancellationRequested)
            {
                SearchResults = results;
                IsSearching = false;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsViewModel] Search error: {ex.Message}");
            if (!cts.Token.IsCancellationRequested)
            {
                SearchResults = Array.Empty<LyricsSearchResult>();
                IsSearching = false;
            }
        }
    }

    private async Task ApplySearchResultAsync(LyricsSearchResult candidate)
    {
        if (_disposed || candidate == null) return;

        IsLoading = true;
        try
        {
            var parsed = await _lyricsService.FetchLyricsByResultAsync(candidate).ConfigureAwait(true);
            if (parsed != null && parsed.Count > 0)
            {
                Lines = parsed;
                var snapshot = _mediaService.Current;
                if (!string.IsNullOrWhiteSpace(snapshot.Title))
                {
                    _lyricsService.SaveLyricsToCache(snapshot.Title, snapshot.Artist, snapshot.AlbumTitle, parsed);
                }
                IsLoading = false;
                IsSearchPanelOpen = false;
                UpdateActiveLine();
            }
            else
            {
                IsLoading = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsViewModel] Apply candidate error: {ex.Message}");
            IsLoading = false;
        }
    }

    private void OnMediaSnapshotChanged(object? sender, MediaSessionSnapshot snapshot)
    {
        if (_disposed) return;

        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplayArtist));
        OnPropertyChanged(nameof(DisplayAlbum));

        UpdateDynamicArtworkColor();

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
        OnPropertyChanged(nameof(IsTranslationVisible));
        OnPropertyChanged(nameof(OpacityPercent));
        OnPropertyChanged(nameof(WindowOpacity));
        OnPropertyChanged(nameof(IsTopmost));
        OnPropertyChanged(nameof(DominantArtworkColor));
        OnPropertyChanged(nameof(DynamicAccentBrush));
        OnPropertyChanged(nameof(ArtworkAmbientGlowBrush));
        OnPropertyChanged(nameof(HasDynamicAccent));
    }

    private void UpdateDynamicArtworkColor()
    {
        var artwork = _mediaService.Current.Artwork;
        var color = ColorExtractor.ExtractDominantColor(artwork);
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

    public async Task LoadLyricsForCurrentTrackAsync()
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

        double effectivePositionSec = positionSec + _manualOffsetSeconds;

        int foundIndex = -1;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].Timestamp.TotalSeconds <= effectivePositionSec)
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
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _mediaService.SnapshotChanged -= OnMediaSnapshotChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;
    }
}

