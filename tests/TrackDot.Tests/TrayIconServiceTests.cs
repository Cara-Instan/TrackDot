using System;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Tests for the tray-icon service. The service itself owns a
/// real <c>TaskbarIcon</c> (a WPF <c>FrameworkElement</c> that
/// cannot be instantiated off the UI thread), so the tests
/// exercise it through a tiny <see cref="IPopoverHost"/> seam
/// plus a fake that records every show/hide/toggle call.
/// </summary>
public sealed class TrayIconServiceTests
{
    [Fact]
    public void Ctor_throws_on_null_arguments()
    {
        // The null-check must run BEFORE the icon handle is
        // subscribed to, so a null popover with a non-null icon
        // (and vice versa) fails the argument check, not the
        // subscription. A test-only icon that throws on use is
        // not safe for the null-check test.
        var safeIcon = new TestIconHandle();
        var safeHost = new FakePopoverHost();
        Assert.Throws<ArgumentNullException>(() => new TrayIconService(null!, safeHost));
        Assert.Throws<ArgumentNullException>(() => new TrayIconService(safeIcon, null!));
    }

    [Fact]
    public void TogglePopover_hides_visible_popover_then_shows_hidden_one()
    {
        var host = new FakePopoverHost();
        using var service = new TrayIconService(new TestIconHandle(), host);

        service.TogglePopover();
        Assert.True(host.IsShown);

        service.TogglePopover();
        Assert.False(host.IsShown);

        service.TogglePopover();
        Assert.True(host.IsShown);
    }

    [Fact]
    public void ShowPopover_is_idempotent()
    {
        var host = new FakePopoverHost();
        using var service = new TrayIconService(new TestIconHandle(), host);

        service.ShowPopover();
        service.ShowPopover();
        service.ShowPopover();

        Assert.Equal(1, host.ShowCount);
        Assert.Equal(0, host.HideCount);
    }

    [Fact]
    public void HidePopover_is_idempotent()
    {
        var host = new FakePopoverHost();
        using var service = new TrayIconService(new TestIconHandle(), host);

        service.ShowPopover();
        service.HidePopover();
        service.HidePopover();
        service.HidePopover();

        Assert.Equal(1, host.ShowCount);
        Assert.Equal(1, host.HideCount);
    }

    [Fact]
    public void HidePopover_when_already_hidden_is_a_noop()
    {
        var host = new FakePopoverHost();
        using var service = new TrayIconService(new TestIconHandle(), host);

        service.HidePopover();
        service.HidePopover();

        Assert.Equal(0, host.HideCount);
    }

    [Fact]
    public void RequestShutdown_raises_event_once_and_is_idempotent()
    {
        var host = new FakePopoverHost();
        using var service = new TrayIconService(new TestIconHandle(), host);

        int events = 0;
        service.ShutdownRequested += (_, _) => events++;

        service.RequestShutdown();
        service.RequestShutdown();
        service.RequestShutdown();

        Assert.Equal(1, events);
    }

    [Fact]
    public void Dispose_disposes_icon_handle_and_becomes_inert()
    {
        var handle = new TestIconHandle();
        var host = new FakePopoverHost();
        var service = new TrayIconService(handle, host);

        service.Dispose();

        Assert.Equal(1, handle.DisposeCount);
        // After dispose, further calls must not throw and must not
        // route through the host.
        service.TogglePopover();
        service.ShowPopover();
        service.HidePopover();
        Assert.Equal(0, host.TotalCalls);
    }

    [Fact]
    public void Tray_left_click_toggles_popover()
    {
        var host = new FakePopoverHost();
        var handle = new TestIconHandle();
        using var service = new TrayIconService(handle, host);

        handle.RaiseLeftClick();
        Assert.True(host.IsShown);

        handle.RaiseLeftClick();
        Assert.False(host.IsShown);
    }

    // --- helpers ---------------------------------------------------------

    /// <summary>
    /// A popover host seam — the tray service talks to this, not to
    /// the WPF window directly. Tests use a recording fake.
    /// </summary>
    private sealed class FakePopoverHost : IPopoverHost
    {
        private bool _isShown;

        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public int ToggleCount { get; private set; }
        public int TotalCalls => ShowCount + HideCount + ToggleCount;
        public bool IsShown => _isShown;

        public void ShowPopover()
        {
            ShowCount++;
            _isShown = true;
        }

        public void HidePopover()
        {
            HideCount++;
            _isShown = false;
        }
    }

    /// <summary>
    /// Inert stand-in for the tray-icon handle. Stores subscribers
    /// in a list and provides a <see cref="RaiseLeftClick"/> helper
    /// for tests that need to simulate a tray click.
    /// </summary>
    private sealed class TestIconHandle : ITrayIconHandle
    {
        private EventHandler? _leftClick;
        public int DisposeCount { get; private set; }
        public string? LastToolTip { get; private set; }
        public int SetToolTipCount { get; private set; }

        public event EventHandler? TrayLeftMouseDown
        {
            add => _leftClick += value;
            remove => _leftClick -= value;
        }

        public void RaiseLeftClick() => _leftClick?.Invoke(this, EventArgs.Empty);

        public void SetToolTipText(string? text)
        {
            LastToolTip = text;
            SetToolTipCount++;
        }

        public void Dispose() => DisposeCount++;
    }
}
