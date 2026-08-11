using System;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;

namespace TrackDot.Services;

/// <summary>
/// Production <see cref="ITrayIconHandle"/> — wraps the live
/// Hardcodet <see cref="TaskbarIcon"/> so the tray service can
/// subscribe to left-click events without taking a hard dependency
/// on <c>Hardcodet.Wpf.TaskbarNotification</c>.
/// </summary>
/// <remarks>
/// The composition root constructs this with the
/// <c>TaskbarIcon</c> defined in <c>App.xaml</c> resources. The
/// handle owns no WPF state of its own; it just relays events and
/// disposes the icon when the service shuts down.
/// </remarks>
internal sealed class TrayIconHandle : ITrayIconHandle
{
    private readonly TaskbarIcon _icon;

    public TrayIconHandle(TaskbarIcon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        _icon = icon;
        _icon.TrayLeftMouseDown += OnTrayLeftMouseDown;
    }

    /// <inheritdoc/>
    public event EventHandler? TrayLeftMouseDown;

    /// <inheritdoc/>
    public void SetToolTipText(string? text)
    {
        // Null resets the tooltip to empty (the TaskbarIcon
        // accepts an empty string but not null). The composition
        // root calls this once on SMTC init failure to flag the
        // degraded state, and the popover is unaffected.
        _icon.ToolTipText = text ?? string.Empty;
    }

    private void OnTrayLeftMouseDown(object sender, RoutedEventArgs e)
        => TrayLeftMouseDown?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc/>
    public void Dispose()
    {
        _icon.TrayLeftMouseDown -= OnTrayLeftMouseDown;
        // TaskbarIcon.Dispose removes the icon from the notification
        // area. The WPF framework will GC the rest.
        _icon.Dispose();
    }
}
