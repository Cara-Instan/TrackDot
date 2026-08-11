namespace TrackDot.Models;

/// <summary>
/// Application theme selection modes.
/// </summary>
public enum AppThemeMode
{
    /// <summary>
    /// Follow the Windows system theme setting (AppsUseLightTheme).
    /// </summary>
    System = 0,

    /// <summary>
    /// Force dark theme palette.
    /// </summary>
    Dark = 1,

    /// <summary>
    /// Force light theme palette.
    /// </summary>
    Light = 2,
}
