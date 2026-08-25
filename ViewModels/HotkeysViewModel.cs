using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TrackDot.Models;
using TrackDot.Services;

namespace TrackDot.ViewModels;

/// <summary>
/// View model for HotkeysWindow, exposing configured hotkey gestures dynamically.
/// </summary>
public sealed class HotkeysViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IWindowSettingsService? _windowSettings;
    private bool _disposed;

    public string ToggleWindowGesture => GetGesture(HotkeyAction.ToggleWindow);
    public string PlayPauseGesture => GetGesture(HotkeyAction.PlayPause);
    public string NextTrackGesture => GetGesture(HotkeyAction.NextTrack);
    public string PreviousTrackGesture => GetGesture(HotkeyAction.PreviousTrack);
    public string StopTrackGesture => GetGesture(HotkeyAction.StopTrack);
    public string SettingsGesture => GetGesture(HotkeyAction.OpenSettings);
    public string MuteGesture => GetGesture(HotkeyAction.ToggleMute);
    public string VolumeUpGesture => GetGesture(HotkeyAction.VolumeUp);
    public string VolumeDownGesture => GetGesture(HotkeyAction.VolumeDown);

    public HotkeysViewModel(IWindowSettingsService? windowSettings = null)
    {
        _windowSettings = windowSettings;
        if (_windowSettings != null)
        {
            _windowSettings.SettingsChanged += OnSettingsChanged;
        }
    }

    private string GetGesture(HotkeyAction action)
    {
        if (_windowSettings != null)
        {
            return _windowSettings.GetHotkeyBinding(action).GestureText;
        }
        var def = HotkeyBinding.GetDefaults().FirstOrDefault(d => d.Action == action);
        return def?.GestureText ?? "None";
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(ToggleWindowGesture));
        OnPropertyChanged(nameof(PlayPauseGesture));
        OnPropertyChanged(nameof(NextTrackGesture));
        OnPropertyChanged(nameof(PreviousTrackGesture));
        OnPropertyChanged(nameof(StopTrackGesture));
        OnPropertyChanged(nameof(SettingsGesture));
        OnPropertyChanged(nameof(MuteGesture));
        OnPropertyChanged(nameof(VolumeUpGesture));
        OnPropertyChanged(nameof(VolumeDownGesture));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_windowSettings != null)
        {
            _windowSettings.SettingsChanged -= OnSettingsChanged;
        }
    }
}
