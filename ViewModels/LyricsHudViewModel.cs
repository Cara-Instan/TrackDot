using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TrackDot.Commands;
using TrackDot.Models;
using TrackDot.Services;

namespace TrackDot.ViewModels;

/// <summary>
/// ViewModel for the mini floating lyrics HUD overlay window.
/// </summary>
public sealed class LyricsHudViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly LyricsViewModel _lyricsViewModel;
    private readonly IMediaControllerService _mediaService;
    private readonly IWindowSettingsService _settingsService;
    private bool _disposed;

    public LyricLine? ActiveLine => _lyricsViewModel.ActiveLine;
    public LyricLine? ActiveNextLine => _lyricsViewModel.ActiveNextLine;

    public bool HasActiveLine => ActiveLine != null;
    public bool HasNextLine => ActiveNextLine != null;

    public string DisplayTitle => _lyricsViewModel.DisplayTitle;
    public string DisplayArtist => _lyricsViewModel.DisplayArtist;

    public bool IsLocked
    {
        get => _settingsService.LyricsHudIsLocked;
        set
        {
            if (_settingsService.LyricsHudIsLocked == value) return;
            _settingsService.LyricsHudIsLocked = value;
            OnPropertyChanged();
        }
    }

    public bool IsFuriganaVisible
    {
        get => _settingsService.LyricsHudShowFurigana;
        set
        {
            if (_settingsService.LyricsHudShowFurigana == value) return;
            _settingsService.LyricsHudShowFurigana = value;
            OnPropertyChanged();
        }
    }

    public bool IsTranslationVisible
    {
        get => _settingsService.LyricsHudShowTranslation;
        set
        {
            if (_settingsService.LyricsHudShowTranslation == value) return;
            _settingsService.LyricsHudShowTranslation = value;
            OnPropertyChanged();
        }
    }

    public double FontSize
    {
        get => _settingsService.LyricsHudFontSize;
        set
        {
            var clamped = Math.Clamp(value, 14.0, 60.0);
            if (Math.Abs(_settingsService.LyricsHudFontSize - clamped) < 0.1) return;
            _settingsService.LyricsHudFontSize = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SecondaryFontSize));
            OnPropertyChanged(nameof(RubyFontSize));
            OnPropertyChanged(nameof(NextLineFontSize));
        }
    }

    public double SecondaryFontSize => Math.Max(11.0, FontSize * 0.68);
    public double RubyFontSize => Math.Max(10.0, FontSize * 0.5);
    public double NextLineFontSize => Math.Max(12.0, FontSize * 0.8);

    public int OpacityPercent
    {
        get => _settingsService.LyricsHudOpacityPercent;
        set
        {
            var clamped = Math.Clamp(value, 20, 100);
            if (_settingsService.LyricsHudOpacityPercent == clamped) return;
            _settingsService.LyricsHudOpacityPercent = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowOpacity));
        }
    }

    public double WindowOpacity => OpacityPercent / 100.0;

    public System.Windows.Media.Brush? DynamicAccentBrush => _lyricsViewModel.DynamicAccentBrush;
    public System.Windows.Media.Brush? ArtworkAmbientGlowBrush => _lyricsViewModel.ArtworkAmbientGlowBrush;
    public bool HasDynamicAccent => _lyricsViewModel.HasDynamicAccent;

    public bool IsPlaying => _mediaService.Current.Playback.State == MediaPlaybackState.Playing;
    public bool CanPlayPause => _mediaService.Current.Playback.Capabilities.CanPlay || _mediaService.Current.Playback.Capabilities.CanPause;
    public bool CanGoNext => _mediaService.Current.Playback.Capabilities.CanGoNext;
    public bool CanGoPrevious => _mediaService.Current.Playback.Capabilities.CanGoPrevious;

    public AsyncRelayCommand ToggleLockCommand { get; }
    public AsyncRelayCommand ToggleFuriganaCommand { get; }
    public AsyncRelayCommand ToggleTranslationCommand { get; }
    public AsyncRelayCommand IncreaseFontSizeCommand { get; }
    public AsyncRelayCommand DecreaseFontSizeCommand { get; }
    public AsyncRelayCommand PlayPauseCommand { get; }
    public AsyncRelayCommand NextTrackCommand { get; }
    public AsyncRelayCommand PreviousTrackCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LyricsHudViewModel(
        LyricsViewModel lyricsViewModel,
        IMediaControllerService mediaService,
        IWindowSettingsService settingsService)
    {
        _lyricsViewModel = lyricsViewModel ?? throw new ArgumentNullException(nameof(lyricsViewModel));
        _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        ToggleLockCommand = new AsyncRelayCommand(
            execute: () =>
            {
                IsLocked = !IsLocked;
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

        IncreaseFontSizeCommand = new AsyncRelayCommand(
            execute: () =>
            {
                FontSize += 2.0;
                return Task.CompletedTask;
            });

        DecreaseFontSizeCommand = new AsyncRelayCommand(
            execute: () =>
            {
                FontSize -= 2.0;
                return Task.CompletedTask;
            });

        PlayPauseCommand = new AsyncRelayCommand(
            execute: async () =>
            {
                await _mediaService.TogglePlayPauseAsync().ConfigureAwait(false);
            });

        NextTrackCommand = new AsyncRelayCommand(
            execute: async () =>
            {
                await _mediaService.NextAsync().ConfigureAwait(false);
            });

        PreviousTrackCommand = new AsyncRelayCommand(
            execute: async () =>
            {
                await _mediaService.PreviousAsync().ConfigureAwait(false);
            });

        _lyricsViewModel.PropertyChanged += OnLyricsViewModelPropertyChanged;
        _mediaService.SnapshotChanged += OnMediaSnapshotChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    private void OnLyricsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;

        if (e.PropertyName is nameof(LyricsViewModel.ActiveLine) or nameof(LyricsViewModel.ActiveLineIndex))
        {
            OnPropertyChanged(nameof(ActiveLine));
            OnPropertyChanged(nameof(ActiveNextLine));
            OnPropertyChanged(nameof(HasActiveLine));
            OnPropertyChanged(nameof(HasNextLine));
        }
        else if (e.PropertyName is nameof(LyricsViewModel.DynamicAccentBrush) or nameof(LyricsViewModel.ArtworkAmbientGlowBrush) or nameof(LyricsViewModel.HasDynamicAccent))
        {
            OnPropertyChanged(nameof(DynamicAccentBrush));
            OnPropertyChanged(nameof(ArtworkAmbientGlowBrush));
            OnPropertyChanged(nameof(HasDynamicAccent));
        }
        else if (e.PropertyName is nameof(LyricsViewModel.DisplayTitle) or nameof(LyricsViewModel.DisplayArtist))
        {
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(DisplayArtist));
        }
    }

    private void OnMediaSnapshotChanged(object? sender, MediaSessionSnapshot snapshot)
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(CanPlayPause));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoPrevious));
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsFuriganaVisible));
        OnPropertyChanged(nameof(IsTranslationVisible));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(SecondaryFontSize));
        OnPropertyChanged(nameof(RubyFontSize));
        OnPropertyChanged(nameof(NextLineFontSize));
        OnPropertyChanged(nameof(OpacityPercent));
        OnPropertyChanged(nameof(WindowOpacity));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lyricsViewModel.PropertyChanged -= OnLyricsViewModelPropertyChanged;
        _mediaService.SnapshotChanged -= OnMediaSnapshotChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;
    }
}
