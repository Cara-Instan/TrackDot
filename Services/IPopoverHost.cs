namespace TrackDot.Services;

/// <summary>
/// UI-thread seam that the tray service talks to in order to
/// show or hide the popover window. The production implementation
/// forwards to <c>MainWindow</c>; tests substitute a recording
/// fake so the tray service can be exercised without a WPF
/// dispatcher.
/// </summary>
public interface IPopoverHost
{
    /// <summary>Show the popover. Must be safe to call repeatedly.</summary>
    void ShowPopover();

    /// <summary>Hide the popover. Must be safe to call when already hidden.</summary>
    void HidePopover();

    /// <summary>
    /// True if the popover is currently visible from the host's
    /// point of view. MUST reflect the popover's actual visibility
    /// — the tray service reads this to decide between Show and Hide
    /// on a tray click, so a stale answer causes wrong-branch bugs
    /// (the user has to click the tray icon twice). Must be read on
    /// the UI thread that owns the popover window. Production:
    /// <c>MainWindow.IsVisible</c>.
    /// </summary>
    bool IsPopoverVisible { get; }
}
