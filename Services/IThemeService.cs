using System;
using TrackDot.Models;

namespace TrackDot.Services;

/// <summary>
/// Service managing application theme mode (System, Dark, Light) and
/// dynamic theme palette switching.
/// </summary>
public interface IThemeService : IDisposable
{
    /// <summary>
    /// Currently selected theme mode (System, Dark, Light).
    /// Setting this property updates and applies the effective theme immediately.
    /// </summary>
    AppThemeMode SelectedTheme { get; set; }

    /// <summary>
    /// Gets whether the active effective theme is Dark mode.
    /// </summary>
    bool IsEffectiveDark { get; }

    /// <summary>
    /// Raised when the effective theme changes (e.g. system theme switch or mode change).
    /// Parameter is true for dark, false for light.
    /// </summary>
    event EventHandler<bool>? EffectiveThemeChanged;

    /// <summary>
    /// Detects whether Windows system setting indicates Dark mode.
    /// </summary>
    bool DetectSystemIsDark();
}
