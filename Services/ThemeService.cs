using System;
using Microsoft.Win32;
using TrackDot.Models;

namespace TrackDot.Services;

/// <summary>
/// Pure state machine for the application's theme preference. Owns
/// the selected <see cref="AppThemeMode"/>, the resolved
/// <see cref="IsEffectiveDark"/> flag, and the
/// <see cref="EffectiveThemeChanged"/> event that subscribers
/// (e.g. <see cref="WpfThemePaletteApplier"/>) react to.
///
/// This class deliberately knows nothing about WPF — it does not
/// touch <c>Application.Current</c>, resource dictionaries, or
/// dispatcher threads. That keeps it trivially unit-testable and
/// means a test that constructs it never deadlocks on a
/// cross-STA <c>Dispatcher.Invoke</c>.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string PersonalizeValueName = "AppsUseLightTheme";

    private const string TrackDotKeyPath = @"Software\TrackDot";
    private const string ThemeValueName = "Theme";

    private AppThemeMode _selectedTheme;
    private bool _effectiveIsDark;
    private bool _disposed;

    /// <inheritdoc/>
    public AppThemeMode SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (_selectedTheme == value && _effectiveIsDark == CalculateEffectiveIsDark(value))
                return;

            _selectedTheme = value;
            SaveThemePreference(value);
            ApplyTheme(value);
        }
    }

    /// <inheritdoc/>
    public bool IsEffectiveDark => _effectiveIsDark;

    /// <inheritdoc/>
    public event EventHandler<bool>? EffectiveThemeChanged;

    public ThemeService(AppThemeMode? initialMode = null)
    {
        _selectedTheme = initialMode ?? LoadThemePreference();
        _effectiveIsDark = CalculateEffectiveIsDark(_selectedTheme);

        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch
        {
            // Non-fatal if SystemEvents listener fails in restricted host
        }
    }

    /// <summary>
    /// Recomputes the effective dark flag for <paramref name="mode"/>
    /// and raises <see cref="EffectiveThemeChanged"/> if it actually
    /// changed. Used by both the public setter and the system
    /// preference callback. No WPF side effects — the WPF palette
    /// applier listens to the event.
    /// </summary>
    private void ApplyTheme(AppThemeMode mode)
    {
        _selectedTheme = mode;
        bool newIsDark = CalculateEffectiveIsDark(mode);

        bool changed = _effectiveIsDark != newIsDark;
        _effectiveIsDark = newIsDark;

        if (changed)
        {
            EffectiveThemeChanged?.Invoke(this, newIsDark);
        }
    }

    /// <inheritdoc/>
    public bool DetectSystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            if (key != null)
            {
                var raw = key.GetValue(PersonalizeValueName);
                if (raw is int val)
                {
                    // 0 = Dark mode, 1 = Light mode
                    return val == 0;
                }
            }
        }
        catch
        {
            // Swallow registry read failures and default to dark
        }

        return true; // Default to dark on Windows fallback
    }

    private bool CalculateEffectiveIsDark(AppThemeMode mode)
    {
        return mode switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => DetectSystemIsDark(),
        };
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_selectedTheme == AppThemeMode.System)
        {
            ApplyTheme(AppThemeMode.System);
        }
    }

    private static AppThemeMode LoadThemePreference()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TrackDotKeyPath);
            var value = key?.GetValue(ThemeValueName) as string;
            if (Enum.TryParse<AppThemeMode>(value, out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            // Fallback to default
        }
        return AppThemeMode.System;
    }

    private static void SaveThemePreference(AppThemeMode mode)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(TrackDotKeyPath);
            key?.SetValue(ThemeValueName, mode.ToString());
        }
        catch
        {
            // Non-fatal if registry write fails
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
        catch
        {
            // Best effort
        }
    }
}
