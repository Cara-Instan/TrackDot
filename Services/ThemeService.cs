using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using TrackDot.Models;

namespace TrackDot.Services;

/// <summary>
/// Service managing application theme mode (System, Dark, Light) and
/// dynamic palette updates in WPF application resources.
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

    /// <inheritdoc/>
    public void ApplyTheme(AppThemeMode mode)
    {
        _selectedTheme = mode;
        bool newIsDark = CalculateEffectiveIsDark(mode);

        UpdateApplicationResources(newIsDark);

        bool changed = _effectiveIsDark != newIsDark;
        _effectiveIsDark = newIsDark;

        EffectiveThemeChanged?.Invoke(this, newIsDark);
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

    private static void UpdateApplicationResources(bool isDark)
    {
        void Action()
        {
            var res = Application.Current?.Resources;
            if (res == null) return;

            // Palette definitions for Dark and Light mode
            var panelColor = isDark ? ColorFromHex("#1A1B1E") : ColorFromHex("#FFFFFF");
            var panelBorderColor = isDark ? ColorFromHex("#2E3036") : ColorFromHex("#E5E7EB");
            var textColor = isDark ? ColorFromHex("#F3F4F6") : ColorFromHex("#111827");
            var mutedColor = isDark ? ColorFromHex("#9CA3AF") : ColorFromHex("#6B7280");
            var accentColor = isDark ? ColorFromHex("#8AB4F8") : ColorFromHex("#1A73E8");
            var accentHoverColor = isDark ? ColorFromHex("#A3C5FF") : ColorFromHex("#1765CC");
            var accentPressedColor = isDark ? ColorFromHex("#719FE4") : ColorFromHex("#1557B0");
            var accentFgColor = isDark ? ColorFromHex("#121316") : ColorFromHex("#FFFFFF");
            var badgeBgColor = isDark ? ColorFromHex("#27282D") : ColorFromHex("#F3F4F6");
            var badgeBorderColor = isDark ? ColorFromHex("#37383E") : ColorFromHex("#E5E7EB");
            var buttonHoverColor = isDark ? ColorFromHex("#2B2D33") : ColorFromHex("#E5E7EB");
            var buttonPressedColor = isDark ? ColorFromHex("#393C44") : ColorFromHex("#D1D5DB");
            var progressTrackColor = isDark ? ColorFromHex("#2D2F35") : ColorFromHex("#E5E7EB");
            var artworkBgColor = isDark ? ColorFromHex("#26282E") : ColorFromHex("#F3F4F6");
            var statusErrorColor = isDark ? ColorFromHex("#F28B82") : ColorFromHex("#D93025");

            SetOrUpdateBrush(res, "PanelBrush", panelColor);
            SetOrUpdateBrush(res, "PanelBorderBrush", panelBorderColor);
            SetOrUpdateBrush(res, "TextBrush", textColor);
            SetOrUpdateBrush(res, "MutedBrush", mutedColor);
            SetOrUpdateBrush(res, "AccentBrush", accentColor);
            SetOrUpdateBrush(res, "AccentHoverBrush", accentHoverColor);
            SetOrUpdateBrush(res, "AccentPressedBrush", accentPressedColor);
            SetOrUpdateBrush(res, "AccentForegroundBrush", accentFgColor);
            SetOrUpdateBrush(res, "BadgeBackgroundBrush", badgeBgColor);
            SetOrUpdateBrush(res, "BadgeBorderBrush", badgeBorderColor);
            SetOrUpdateBrush(res, "ButtonHoverBrush", buttonHoverColor);
            SetOrUpdateBrush(res, "ButtonPressedBrush", buttonPressedColor);
            SetOrUpdateBrush(res, "ProgressTrackBrush", progressTrackColor);
            SetOrUpdateBrush(res, "ArtworkBackgroundBrush", artworkBgColor);
            SetOrUpdateBrush(res, "StatusErrorBrush", statusErrorColor);

            // Context Menu SystemColors overrides
            SetOrUpdateBrush(res, SystemColors.HighlightBrushKey, buttonHoverColor);
            SetOrUpdateBrush(res, SystemColors.HighlightTextBrushKey, textColor);
            SetOrUpdateBrush(res, SystemColors.MenuHighlightBrushKey, buttonHoverColor);
            SetOrUpdateBrush(res, SystemColors.MenuBrushKey, panelColor);
            SetOrUpdateBrush(res, SystemColors.ControlBrushKey, panelColor);
        }

        var app = Application.Current;
        if (app != null)
        {
            if (app.Dispatcher.CheckAccess())
            {
                Action();
            }
            else
            {
                app.Dispatcher.Invoke(Action);
            }
        }
    }

    private static void SetOrUpdateBrush(ResourceDictionary res, object key, Color color)
    {
        if (res.Contains(key) && res[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
        }
        else
        {
            res[key] = new SolidColorBrush(color);
        }
    }

    private static Color ColorFromHex(string hex)
    {
        return (Color)ColorConverter.ConvertFromString(hex);
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
