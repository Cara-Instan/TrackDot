using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using TrackDot.Services;
using TrackDot.ViewModels;

namespace TrackDot;

/// <summary>
/// The floating popover. Owns no data; binds to a
/// <see cref="MainViewModel"/> supplied at construction time by the composition root. Closing the window is intercepted and turned into a <see cref="Hide"/> while the application is still running, so the user never accidentally terminates the tray process.
/// </summary>
public partial class MainWindow : Window, IPopoverHost
{
    /// <summary>
    /// Signals that the application is shutting down. While <c>true</c>,
    /// the <c>Closing</c> handler lets the window close normally.
    /// Call <see cref="BeginShutdown"/> from the composition root during
    /// teardown instead of setting a static property.
    /// </summary>
    private bool _isShuttingDown;

    /// <summary>
    /// Called by the composition root (<c>App.OnExit</c>) before
    /// closing the window so that the <c>Closing</c> handler does not
    /// cancel the close.
    /// </summary>
    public void BeginShutdown() => _isShuttingDown = true;

    private MainViewModel? _viewModel;
    private IWindowPlacementService? _placement;

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

    /// <summary>
    /// Wires the popover to the window-placement service. Called
    /// by <c>App.OnStartup</c> after the placement service is
    /// constructed. The popover calls into the service on every
    /// <see cref="ShowPopover"/> so a display change is picked up
    /// without a separate cache-invalidation path.
    /// </summary>
    public void SetPlacement(IWindowPlacementService placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        _placement = placement;
    }

    /// <summary>Shows the popover. Idempotent.</summary>
    void IPopoverHost.ShowPopover() => ShowPopover();

    /// <summary>Hides the popover. Idempotent.</summary>
    void IPopoverHost.HidePopover() => HidePopover();

    /// <summary>
    /// Shows the popover, positions it above the system tray on
    /// the taskbar monitor, and tells the view-model it's
    /// visible (so the timer starts). Calling twice is a no-op.
    /// </summary>
    public void ShowPopover()
    {
        if (!IsVisible)
        {
            // Position first so the very first frame draws at
            // the right place. ApplyPlacement updates Left/Top
            // only — the popover's size is set by the
            // SizeToContent="Height" attribute on the root
            // Window and is known by the time Show() returns.
            ApplyPlacement();
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

    /// <summary>
    /// Resolves the popover's screen position from the
    /// configured placement service. No-op when the service
    /// has not been wired (test path: the popover is exercised
    /// without a placement service so placement can be skipped).
    /// </summary>
    private void ApplyPlacement()
    {
        if (_placement is null) return;
        // Use DesiredSize (SizeToContent-driven) when set,
        // otherwise fall back to the Width/Height set in XAML.
        var width = double.IsNaN(DesiredSize.Width) || DesiredSize.Width <= 0
            ? ActualWidth
            : DesiredSize.Width;
        var height = double.IsNaN(DesiredSize.Height) || DesiredSize.Height <= 0
            ? ActualHeight
            : DesiredSize.Height;
        if (width <= 0) width = Width;
        if (height <= 0) height = Height;
        if (width <= 0 || height <= 0) return;

        var point = _placement.ComputeAnchoredPosition(new Size(width, height));
        // Defer the actual Left/Top assignment to the next
        // dispatcher pass: a WPF window's Left/Top can only
        // be set after SourceInitialized has fired. SourceInitialized
        // is also wired to fire before Loaded on a fresh Show(),
        // so Left/Top set inside Show() lands correctly.
        Left = point.X;
        Top = point.Y;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void SeekSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Slider slider) return;
        if (_viewModel?.SeekCommand is not { } cmd) return;
        if (cmd.CanExecute(slider.Value))
            cmd.Execute(slider.Value);
    }

    private void VolumeSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Slider slider) return;
        if (_viewModel?.SetVolumeCommand is not { } cmd) return;
        if (cmd.CanExecute(slider.Value))
            cmd.Execute(slider.Value);
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
        if (!_isShuttingDown)
        {
            e.Cancel = true;
            HidePopover();
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        // Extend the DWM glass frame to fill the entire window so the
        // background renders transparent via hardware (DWM composition)
        // rather than software rendering (AllowsTransparency). This is
        // the recommended approach for frameless WPF windows on Win10/11.
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        if (helper.Handle == IntPtr.Zero) return;
        try
        {
            var margins = new NativeMethods.MARGINS { cxLeftWidth = -1 };
            NativeMethods.DwmExtendFrameIntoClientArea(helper.Handle, ref margins);
        }
        catch
        {
            // DWM unavailable (e.g. running in a VM without Aero); fall back gracefully.
        }
    }
}
