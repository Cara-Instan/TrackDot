using System.Windows;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Tests for the pure placement math in
/// <see cref="WindowPlacement"/> and the production
/// <see cref="WindowPlacementService"/> that wraps
/// <c>SystemParameters.WorkArea</c>.
/// </summary>
/// <remarks>
/// The production wrapper itself cannot be unit-tested
/// (it reads <c>SystemParameters</c> which requires a real
/// WPF dispatcher). The pure helper is tested exhaustively
/// here, and a thin fake is provided so a future caller that
/// uses the interface contract can be exercised without the
/// real service.
/// </remarks>
public sealed class WindowPlacementTests
{
    // ----- WindowPlacement.ComputeAnchoredPosition ----------------------

    [Fact]
    public void ComputeAnchoredPosition_anchors_to_bottom_right_with_default_margin()
    {
        // 1920x1080 work area (typical 1080p, taskbar on bottom).
        // Popover 360x128 with the default 8 px margin should
        // sit at (1920 - 360 - 8, 1080 - 128 - 8) = (1552, 944).
        var workArea = new Rect(0, 0, 1920, 1080);
        var popoverSize = new Size(360, 128);

        var p = WindowPlacement.ComputeAnchoredPosition(workArea, popoverSize);

        Assert.Equal(1552, p.X);
        Assert.Equal(944, p.Y);
    }

    [Fact]
    public void ComputeAnchoredPosition_uses_supplied_margin()
    {
        var workArea = new Rect(0, 0, 1000, 800);
        var popoverSize = new Size(200, 100);

        var p = WindowPlacement.ComputeAnchoredPosition(workArea, popoverSize, marginPixels: 12);

        Assert.Equal(1000 - 200 - 12, p.X);
        Assert.Equal(800 - 100 - 12, p.Y);
    }

    [Fact]
    public void ComputeAnchoredPosition_zero_margin_anchors_to_corner()
    {
        var workArea = new Rect(0, 0, 1000, 800);
        var popoverSize = new Size(200, 100);

        var p = WindowPlacement.ComputeAnchoredPosition(workArea, popoverSize, marginPixels: 0);

        Assert.Equal(800, p.X);
        Assert.Equal(700, p.Y);
    }

    [Fact]
    public void ComputeAnchoredPosition_clamps_to_work_area_left_when_popover_is_wide()
    {
        // Popover wider than the work area — the result is
        // pinned to the work-area's left edge so the popover
        // does not render entirely off-screen.
        var workArea = new Rect(100, 200, 200, 300);
        var popoverSize = new Size(500, 50);

        var p = WindowPlacement.ComputeAnchoredPosition(workArea, popoverSize, marginPixels: 0);

        Assert.Equal(100, p.X);
        // Vertical fit is fine.
        Assert.Equal(200 + 300 - 50, p.Y);
    }

    [Fact]
    public void ComputeAnchoredPosition_clamps_to_work_area_top_when_popover_is_tall()
    {
        var workArea = new Rect(0, 0, 500, 100);
        var popoverSize = new Size(50, 200);

        var p = WindowPlacement.ComputeAnchoredPosition(workArea, popoverSize, marginPixels: 0);

        // Vertical clamp.
        Assert.Equal(0, p.Y);
        // Horizontal fit is fine.
        Assert.Equal(500 - 50, p.X);
    }

    [Fact]
    public void ComputeAnchoredPosition_handles_secondary_monitor_work_area()
    {
        // A monitor placed to the right of a 1920-wide primary:
        // its work area starts at x=1920. The popover's anchor
        // is its bottom-right; the math must use the supplied
        // work-area rect, not assume (0, 0) origin.
        var workArea = new Rect(1920, 0, 2560, 1440);
        var popoverSize = new Size(360, 128);

        var p = WindowPlacement.ComputeAnchoredPosition(workArea, popoverSize, marginPixels: 8);

        Assert.Equal(1920 + 2560 - 360 - 8, p.X);
        Assert.Equal(1440 - 128 - 8, p.Y);
    }

    [Fact]
    public void ComputeAnchoredPosition_negative_margin_is_treated_as_zero()
    {
        // The handoff says the margin is 8-12 px. Negative
        // values are nonsense and the production wrapper does
        // not know about them. The pure helper clamps to 0
        // rather than throwing so a bad caller does not crash
        // the show path.
        var workArea = new Rect(0, 0, 1000, 800);
        var popoverSize = new Size(200, 100);

        var p = WindowPlacement.ComputeAnchoredPosition(workArea, popoverSize, marginPixels: -50);

        Assert.Equal(800, p.X);
        Assert.Equal(700, p.Y);
    }

    [Fact]
    public void ComputeAnchoredPosition_nan_margin_is_treated_as_zero()
    {
        var workArea = new Rect(0, 0, 1000, 800);
        var popoverSize = new Size(200, 100);

        var p = WindowPlacement.ComputeAnchoredPosition(workArea, popoverSize, marginPixels: double.NaN);

        Assert.Equal(800, p.X);
        Assert.Equal(700, p.Y);
    }

    // ----- IWindowPlacementService contract -----------------------------

    [Fact]
    public void Service_compute_anchored_position_with_work_area_delegates_to_helper()
    {
        // A fake service that returns a fixed work area, plus
        // a popover size, exercises the same code path
        // production uses (ComputeAnchoredPosition(Size,
        // Rect)). The pure helper's tests above cover the
        // underlying math; this test ensures the
        // double-dispatch in the service interface stays
        // consistent.
        var service = new FakePlacementService(new Rect(0, 0, 1000, 800));
        var p = service.ComputeAnchoredPosition(new Size(200, 100), service.GetWorkArea());

        // Default margin is 8 px → bottom-right at (800, 700)
        // minus 8 px margin = (792, 692).
        Assert.Equal(792, p.X);
        Assert.Equal(692, p.Y);
    }

    [Fact]
    public void Service_parameterless_compute_anchored_position_uses_getWorkArea()
    {
        var service = new FakePlacementService(new Rect(0, 0, 1920, 1080));
        var p = service.ComputeAnchoredPosition(new Size(360, 128));

        Assert.Equal(1920 - 360 - 8, p.X);
        Assert.Equal(1080 - 128 - 8, p.Y);
        Assert.Equal(1, service.GetWorkAreaCount);
    }

    // ----- helpers ------------------------------------------------------

    /// <summary>
    /// Recording fake. Returns the configured work area and
    /// counts how many times it was read. The production
    /// wrapper's <c>SystemParameters.WorkArea</c> read cannot
    /// be substituted in a unit test (no WPF dispatcher), so
    /// tests that need to drive the show path inject this fake
    /// through <c>MainWindow.SetPlacement</c>.
    /// </summary>
    private sealed class FakePlacementService : IWindowPlacementService
    {
        private readonly Rect _workArea;

        public FakePlacementService(Rect workArea) => _workArea = workArea;

        public int GetWorkAreaCount { get; private set; }

        public Rect GetWorkArea()
        {
            GetWorkAreaCount++;
            return _workArea;
        }

        public Point ComputeAnchoredPosition(Size popoverSize, Rect workArea)
            => WindowPlacement.ComputeAnchoredPosition(workArea, popoverSize);

        public Point ComputeAnchoredPosition(Size popoverSize)
            => ComputeAnchoredPosition(popoverSize, GetWorkArea());
    }
}
