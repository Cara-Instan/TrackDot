using System;
using System.Windows;

namespace TrackDot.Services;

/// <summary>
/// Resolves the popover's screen position at show time. The
/// production implementation pulls the system work area (the
/// monitor containing the taskbar) from
/// <c>SystemParameters</c>; tests substitute a fake that returns
/// canned <see cref="Rect"/>s so the placement math can be
/// exercised without an active display.
/// </summary>
public interface IWindowPlacementService
{
    /// <summary>
    /// Returns the work area, in WPF device-independent pixels
    /// (DIPs), of the monitor the popover should anchor to. The
    /// popover calls this every time it is shown so a display
    /// change (resolution, monitor swap) is picked up without
    /// explicit invalidation.
    /// </summary>
    Rect GetWorkArea();

    /// <summary>
    /// Computes the popover's top-left position (in WPF DIPs)
    /// given the work area and the popover's desired size. The
    /// default implementation delegates to
    /// <see cref="WindowPlacement.ComputeAnchoredPosition"/>;
    /// production callers should use the parameterless
    /// <see cref="ComputeAnchoredPosition(Size)"/> overload that
    /// pulls the work area itself.
    /// </summary>
    Point ComputeAnchoredPosition(Size popoverSize, Rect workArea);

    /// <summary>
    /// Convenience: <c>ComputeAnchoredPosition(popoverSize,
    /// GetWorkArea())</c>. Production code calls this.
    /// </summary>
    Point ComputeAnchoredPosition(Size popoverSize);
}
