using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using TrackDot.Services;
using TrackDot.ViewModels;

namespace TrackDot;

/// <summary>
/// The floating popover. Owns no data; binds to a
/// <see cref="MainViewModel"/> supplied at construction time by the
/// composition root. Closing the window is intercepted and turned
/// into a <see cref="Hide"/> while the application is still
/// running, so the user never accidentally terminates the tray
/// process.
/// </summary>
public partial class MainWindow : Window, IPopoverHost
{
    /// <summary>
    /// Set by the composition root when the application is in the
    /// process of shutting down. While <c>true</c>, the
    /// <c>Closing</c> handler lets the window close normally.
    /// </summary>
    public static bool IsShuttingDown { get; set; }

    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wires the popover to its view-model. Called by
    /// <c>App.OnStartup</c> after the view-model is constructed.
    /// </summary>
    public void SetViewModel(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        _viewModel = viewModel;
    }

    /// <summary>Shows the popover. Idempotent.</summary>
        void IPopoverHost.ShowPopover() => ShowPopover();

        /// <summary>Hides the popover. Idempotent.</summary>
        void IPopoverHost.HidePopover() => HidePopover();

        /// <summary>
        /// Shows the popover and tells the view-model it's visible
        /// (so the timer starts). Calling twice is a no-op.
        /// </summary>
    public void ShowPopover()
    {
        if (!IsVisible)
        {
            Show();
        }
        if (_viewModel is not null && !_viewModel.IsVisible)
        {
            _viewModel.IsVisible = true;
        }
    }

    /// <summary>
    /// Hides the popover and tells the view-model it's hidden
    /// (so the timer stops). Calling twice is a no-op.
    /// </summary>
    public void HidePopover()
    {
        if (IsVisible)
        {
            Hide();
        }
        if (_viewModel is not null && _viewModel.IsVisible)
        {
            _viewModel.IsVisible = false;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!IsActive) HidePopover();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HidePopover();
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // While the application is shutting down, let the close
        // happen normally. Otherwise the user pressing X (or
        // Alt+F4) must not terminate the tray process — just hide
        // the popover and remain alive in the notification area.
        if (!IsShuttingDown)
        {
            e.Cancel = true;
            HidePopover();
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        // Rounded WPF chrome is intentionally used for Windows 10 compatibility.
    }
}