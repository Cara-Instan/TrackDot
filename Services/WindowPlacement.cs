using System;
using System.Windows;

namespace TrackDot.Services;

/// <summary>
/// Pure math for placing a popover above the notification area
/// (taskbar) on a given work-area rectangle. No WPF / Win32
/// references — the monitor- and DPI-resolution code lives in
/// <see cref="WindowPlacementService"/>; this class only does the
/// "given a work area, given a popover size, where do I anchor?"
/// computation.
/// </summary>
/// <remarks>
/// <para>
/// The placement is "anchored to the lower-right corner of the
/// work area" with a configurable <see cref="MarginPixels"/>
/// (typically 8–12 px) so the popover sits just above and to the
/// left of the system tray. If the popover would fall off the
/// left or top edge of the work area, the result is clamped so
/// the popover remains fully visible.
/// </para>
/// <para>
/// Coordinates are in the same units as the supplied
/// <paramref name="workArea"/> rectangle. WPF's
/// <c>SystemParameters.WorkArea</c> is in DIPs (1/96th inch) for
/// WPF, so for the popover the placement code is unitless and
/// works for any DPI as long as the caller supplies the
/// WPF-DIPs work area.
/// </para>
/// </remarks>
internal static class WindowPlacement
{
    /// <summary>Default margin between the popover and the work-area edges, in pixels.</summary>
    public const double DefaultMarginPixels = 8.0;

    /// <summary>
    /// Computes the popover's top-left position in the same
    /// coordinate space as <paramref name="workArea"/>. The
    /// popover's bottom-right corner sits at
    /// <c>(workArea.Right - margin, workArea.Bottom - margin)</c>;
    /// the top-left is derived by subtracting the popover size. If
    /// the popover is wider or taller than the work area, the
    /// result is clamped so it remains fully visible.
    /// </summary>
    /// <param name="workArea">The work area to anchor against, in pixels (DIPs).</param>
    /// <param name="popoverSize">The popover's desired size, in pixels (DIPs).</param>
    /// <param name="marginPixels">Distance from the work-area's bottom and right edges, in pixels.</param>
    public static Point ComputeAnchoredPosition(
        Rect workArea,
        Size popoverSize,
        double marginPixels = DefaultMarginPixels)
    {
        // Negative margins are nonsense; clamp to >=0. The handoff
        // says "8–12 px" — values inside that range are the
        // supported contract. Out-of-range inputs are clamped
        // rather than rejected so the production wrapper doesn't
        // need to know.
        if (double.IsNaN(marginPixels) || marginPixels < 0) marginPixels = 0;

        // Anchor point: just above the bottom-right of the work
        // area, with the configured margin on both axes. The
        // popover's top-left is the anchor minus the popover size.
        double anchorRight = workArea.Right - marginPixels;
        double anchorBottom = workArea.Bottom - marginPixels;

        double left = anchorRight - popoverSize.Width;
        double top = anchorBottom - popoverSize.Height;

        // Clamp into the work area. A popover that is larger than
        // the work area is still placed so its top-left sits at
        // the work-area's top-left — the caller can decide whether
        // to resize the popover or accept partial visibility.
        if (left < workArea.Left) left = workArea.Left;
        if (top < workArea.Top) top = workArea.Top;

        return new Point(left, top);
    }
}
