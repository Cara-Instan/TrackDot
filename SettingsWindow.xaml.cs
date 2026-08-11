using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using TrackDot.ViewModels;

namespace TrackDot;

/// <summary>
/// The settings window. A normal top-level WPF window (not a
/// popover) so the user can move, resize, and dismiss it
/// independently of the popover. Closing the window is
/// intercepted and turned into a <see cref="Hide"/> while the
/// application is still running.
/// </summary>
/// <remarks>
/// <para>
/// The window is opened by the tray menu via the composition
/// root. Exactly one instance is alive at any time
/// (<see cref="TrackDot.App"/> owns the field). The tray
/// "Settings" click handler calls <see cref="ShowSettings"/>
/// which is idempotent — a second click while the window is
/// already visible brings it to the foreground instead of
/// creating a duplicate.
/// </para>
/// <para>
/// The window's data context is a <see cref="SettingsViewModel"/>
/// that takes an <c>IStartupService</c> through the
/// composition root. The view-model persists every toggle
/// immediately, so a Close button (or Esc) simply hides the
/// window — the registry state already matches the checkbox.
/// </para>
/// </remarks>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// Set by the composition root when the application is
    /// in the process of shutting down. While <c>true</c>,
    /// the <c>Closing</c> handler lets the window close
    /// normally.
    /// </summary>
    public static bool IsShuttingDown { get; set; }

    private SettingsViewModel? _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wires the window to its view-model. Called by
    /// <c>App.OnStartup</c> after the view-model is
    /// constructed.
    /// </summary>
    public void SetViewModel(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        _viewModel = viewModel;
    }

    /// <summary>
    /// Shows the window. Idempotent: if already visible,
    /// the window is activated instead of creating a new
    /// instance. Hides instead of closes so the user's
    /// window position (set on first show) is preserved
    /// across opens.
    /// </summary>
    public void ShowSettings()
    {
        if (!IsVisible)
        {
            Show();
        }
        else
        {
            // Bring to foreground — a second tray click on
            // an already-open settings window must not move
            // it behind the popover or lose focus.
            Activate();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // While the application is shutting down, let the
        // close happen normally. Otherwise the user pressing
        // X (or Alt+F4, or the Close button) must not
        // terminate the tray process — just hide the window
        // and remain alive in the notification area.
        if (!IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        // Hide rather than close: closing would force
        // composition-root teardown to recreate the window
        // on every open. The XAML marks the button IsCancel
        // so Esc does the same thing.
        Hide();
    }
}