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
}
