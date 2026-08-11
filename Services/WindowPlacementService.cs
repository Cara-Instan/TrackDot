using System;
using System.Windows;

namespace TrackDot.Services;

/// <summary>
/// Production <see cref="IWindowPlacementService"/>. Resolves the
/// work area from the live <c>SystemParameters</c> and delegates
/// the placement math to <see cref="WindowPlacement"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>SystemParameters.WorkArea</c> reports the monitor that
/// contains the taskbar (the "primary" monitor, in WPF's
/// terminology) in device-independent pixels. It updates when
/// the user changes display settings, so the popover's
/// show-time read picks up resolution changes without
/// subscription.
/// </para>
/// <para>
/// The work area is the rectangle of the monitor excluding the
/// taskbar and docked toolbars — exactly the region the popover
/// should sit above. Anchoring to the bottom-right with an
/// 8 px margin places the popover directly above the system
/// tray on standard Windows layouts.
/// </para>
/// </remarks>
public sealed class WindowPlacementService : IWindowPlacementService
{
    /// <inheritdoc/>
    public Rect GetWorkArea() => SystemParameters.WorkArea;

    /// <inheritdoc/>
    public Point ComputeAnchoredPosition(Size popoverSize, Rect workArea)
        => WindowPlacement.ComputeAnchoredPosition(workArea, popoverSize);

    /// <inheritdoc/>
    public Point ComputeAnchoredPosition(Size popoverSize)
        => ComputeAnchoredPosition(popoverSize, GetWorkArea());
}
