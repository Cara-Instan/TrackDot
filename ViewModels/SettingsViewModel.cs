using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrackDot.Models;
using TrackDot.Services;

namespace TrackDot.ViewModels;

/// <summary>
/// Settings window view-model. Exposes the launch-at-sign-in
/// toggle and theme selection (System, Dark, Light).
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IStartupService _startup;
    private readonly IThemeService? _theme;
    private bool _disposed;

    /// <summary>
    /// Backing field for <see cref="LaunchAtSignIn"/>. Set
    /// from <see cref="IStartupService.IsEnabled"/> at
    /// construction; mutated by the property setter which
    /// also persists to the registry.
    /// </summary>
    private bool _launchAtSignIn;

    /// <summary>
    /// Diagnostic message shown in the window footer.
    /// Non-empty only when a save attempt failed; otherwise
    /// the empty string.
    /// </summary>
    private string _statusMessage = string.Empty;

    /// <summary>
    /// True when TrackDot is registered to launch at
    /// sign-in.
    /// </summary>
    public bool LaunchAtSignIn
    {
        get => _launchAtSignIn;
        set
        {
            if (_launchAtSignIn == value) return;
            _launchAtSignIn = value;
            OnPropertyChanged();

            try
            {
                if (value) _startup.Enable();
                else _startup.Disable();
                StatusMessage = string.Empty;
            }
            catch (Exception ex)
            {
                _launchAtSignIn = !value;
                OnPropertyChanged();
                StatusMessage = ex.Message;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
    }

    /// <summary>
    /// Selected application theme mode (System, Dark, Light).
    /// </summary>
    public AppThemeMode SelectedTheme
    {
        get => _theme?.SelectedTheme ?? AppThemeMode.System;
        set
        {
            if (_theme == null || _theme.SelectedTheme == value) return;
            _theme.SelectedTheme = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(IsLightTheme));
        }
    }

    /// <summary>Convenience helper for RadioButton binding.</summary>
    public bool IsSystemTheme
    {
        get => SelectedTheme == AppThemeMode.System;
        set { if (value) SelectedTheme = AppThemeMode.System; }
    }

    /// <summary>Convenience helper for RadioButton binding.</summary>
    public bool IsDarkTheme
    {
        get => SelectedTheme == AppThemeMode.Dark;
        set { if (value) SelectedTheme = AppThemeMode.Dark; }
    }

    /// <summary>Convenience helper for RadioButton binding.</summary>
    public bool IsLightTheme
    {
        get => SelectedTheme == AppThemeMode.Light;
        set { if (value) SelectedTheme = AppThemeMode.Light; }
    }

    /// <summary>
    /// Non-empty when the most recent toggle failed to
    /// persist to the registry.
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The value name used for the Run-key entry.</summary>
    public string RegistryValueName => RegistryKeyFactory.ValueName;

    /// <summary>The registry path used for the Run-key entry.</summary>
    public string RegistryKeyPath => @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Constructs a view-model bound to startup and theme services.</summary>
    public SettingsViewModel(IStartupService startup, IThemeService? theme = null)
    {
        ArgumentNullException.ThrowIfNull(startup);
        _startup = startup;
        _theme = theme;
        _launchAtSignIn = startup.IsEnabled;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
