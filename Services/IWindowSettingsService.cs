using System;

namespace TrackDot.Services;

/// <summary>
/// Service managing persistent window settings such as Pin to Top
/// and window opacity (transparency).
/// </summary>
public interface IWindowSettingsService
{
    /// <summary>
    /// <see langword="true"/> when the main popover is pinned to top
    /// and should stay visible when focus is deactivated.
    /// </summary>
    bool IsPinned { get; set; }

    /// <summary>
    /// Window opacity in range [0.2, 1.0].
    /// </summary>
    double WindowOpacity { get; set; }

    /// <summary>
    /// Window opacity expressed as an integer percentage [20–100].
    /// </summary>
    int OpacityPercent { get; set; }

    /// <summary>
    /// <see langword="true"/> when system-wide global hotkeys are enabled.
    /// </summary>
    bool EnableGlobalHotkeys { get; set; }

    /// <summary>
    /// Event raised when any window setting changes.
    /// </summary>
    event EventHandler? SettingsChanged;
}
