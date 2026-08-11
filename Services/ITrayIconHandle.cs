using System;

namespace TrackDot.Services;

/// <summary>
/// UI-thread seam around the Hardcodet
/// <c>TaskbarIcon</c>. The tray service forwards clicks, exit
/// notifications, and shutdown requests through this interface so
/// the WPF dependency stays out of the test path.
/// </summary>
/// <remarks>
/// The production implementation lives in
/// <c>TrayIconHandle</c> and owns the actual <c>TaskbarIcon</c>.
/// The interface deliberately exposes only the events the tray
/// service subscribes to and the lifecycle hooks it needs.
/// </remarks>
public interface ITrayIconHandle : IDisposable
{
    /// <summary>Raised when the user left-clicks the tray icon.</summary>
    event EventHandler? TrayLeftMouseDown;

    /// <summary>
    /// Updates the tooltip the notification area shows when the
    /// user hovers the tray icon. Used by the composition root
    /// to surface a non-fatal state (e.g. "media unavailable"
    /// when SMTC initialization failed) without changing the
    /// icon itself.
    /// </summary>
    void SetToolTipText(string? text);
}
