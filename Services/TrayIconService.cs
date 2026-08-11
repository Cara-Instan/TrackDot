using System;

namespace TrackDot.Services;

/// <summary>
/// Owns the tray-icon lifecycle and the popover visibility
/// transitions. Constructed once at app startup; lives for the
/// duration of the process.
/// </summary>
/// <remarks>
/// <para>
/// The service keeps a small piece of UI state (whether the
/// popover is currently visible) and routes every state change
/// through <see cref="IPopoverHost"/>. <see cref="ShowPopover"/>,
/// <see cref="HidePopover"/>, and <see cref="TogglePopover"/> are
/// all idempotent — calling <c>ShowPopover</c> when the popover
/// is already visible is a no-op, etc.
/// </para>
/// <para>
/// <see cref="RequestShutdown"/> raises <see cref="ShutdownRequested"/>
/// exactly once per service instance. The composition layer
/// (<c>App.OnExit</c>) subscribes once and tears down services in
/// reverse construction order.
/// </para>
/// </remarks>
public sealed class TrayIconService : IDisposable
{
    private readonly ITrayIconHandle _icon;
    private readonly IPopoverHost _popover;
    private bool _isPopoverVisible;
    private bool _disposed;
    private bool _shutdownRequested;

    /// <summary>
    /// Raised when the user picked <em>Exit TrackDot</em> from the
    /// tray context menu. Raised at most once per service
    /// instance.
    /// </summary>
    public event EventHandler? ShutdownRequested;

    /// <summary>True if the popover is currently visible.</summary>
    public bool IsPopoverVisible => _isPopoverVisible;

    /// <summary>
    /// Creates the tray service. The handle is wired to forward
    /// left-click events to <see cref="TogglePopover"/>. Caller
    /// must dispose the service on application exit.
    /// </summary>
    public TrayIconService(ITrayIconHandle icon, IPopoverHost popover)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(popover);

        _icon = icon;
        _popover = popover;
        _icon.TrayLeftMouseDown += OnTrayLeftMouseDown;
    }

    /// <summary>Show the popover. No-op if already visible.</summary>
    public void ShowPopover()
    {
        if (_disposed || _isPopoverVisible) return;
        _isPopoverVisible = true;
        _popover.ShowPopover();
    }

    /// <summary>Hide the popover. No-op if already hidden.</summary>
    public void HidePopover()
    {
        if (_disposed || !_isPopoverVisible) return;
        _isPopoverVisible = false;
        _popover.HidePopover();
    }

    /// <summary>Toggle the popover's visibility.</summary>
    public void TogglePopover()
    {
        if (_disposed) return;
        if (_isPopoverVisible) HidePopover();
        else ShowPopover();
    }

    /// <summary>
    /// Request application shutdown. Raises
    /// <see cref="ShutdownRequested"/> once. Safe to call from the
    /// tray menu, the Exit command, or programmatically; subsequent
    /// calls are ignored.
    /// </summary>
    public void RequestShutdown()
    {
        if (_disposed || _shutdownRequested) return;
        _shutdownRequested = true;
        ShutdownRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnTrayLeftMouseDown(object? sender, EventArgs e) => TogglePopover();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.TrayLeftMouseDown -= OnTrayLeftMouseDown;
        _icon.Dispose();
    }
}
