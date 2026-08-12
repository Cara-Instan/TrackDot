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
    bool IPopoverHost.IsPopoverVisible => IsVisible;

    /// <summary>
    /// Shows the popover, positions it above the system tray on
    /// the taskbar monitor, and tells the view-model it's
    /// visible (so the timer starts). Calling twice is a no-op.
    /// On a fresh show the window is also raised to the foreground
    /// and Topmost is re-asserted so a window that had focus before
    /// the tray click is reliably pushed below the popover.
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
            // After a Show()-from-hidden, Windows does not always
            // raise the window above fullscreen/Snapped apps or
            // apps that were already in the foreground. Activate
            // pushes it to the foreground; the false→true toggle
            // on Topmost defeats any Z-order caching the shell
            // did while the window was hidden.
            Activate();
            if (IsPinned) Topmost = true;   // pinned: stay topmost
            else { Topmost = false; Topmost = true; }
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


    private Action? _onOpenSettings;
    private Action? _onOpenAbout;
    private Action? _onOpenHotkeys;
    private IWindowSettingsService? _windowSettingsService;

    /// <summary>
    /// Gets whether the popover window is pinned to stay open on focus loss.
    /// </summary>
    public bool IsPinned { get; private set; }

    /// <summary>
    /// Wires header action handlers (Settings, About, Hotkeys) and window settings service.
    /// </summary>
    public void SetHeaderActions(
        Action? onOpenSettings,
        Action? onOpenAbout,
        Action? onOpenHotkeys,
        IWindowSettingsService? windowSettingsService = null)
    {
        _onOpenSettings = onOpenSettings;
        _onOpenAbout = onOpenAbout;
        _onOpenHotkeys = onOpenHotkeys;

        if (_windowSettingsService != null)
        {
            _windowSettingsService.SettingsChanged -= OnWindowSettingsChanged;
        }

        _windowSettingsService = windowSettingsService;

        if (_windowSettingsService != null)
        {
            _windowSettingsService.SettingsChanged += OnWindowSettingsChanged;
            ApplyWindowSettings();
        }
    }

    private void OnWindowSettingsChanged(object? sender, EventArgs e)
    {
        ApplyWindowSettings();
    }

    private void ApplyWindowSettings()
    {
        if (_windowSettingsService == null) return;
        IsPinned = _windowSettingsService.IsPinned;
        Opacity = _windowSettingsService.WindowOpacity;
        UpdatePinVisualState();
    }

    private void OnPinClicked(object sender, RoutedEventArgs e)
    {
        IsPinned = !IsPinned;
        if (_windowSettingsService != null)
        {
            _windowSettingsService.IsPinned = IsPinned;
        }
        UpdatePinVisualState();
    }

    private void UpdatePinVisualState()
    {
        Topmost = IsPinned;
        if (PinIconPath != null)
        {
            PinIconPath.Fill = IsPinned
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : (System.Windows.Media.Brush)FindResource("MutedBrush");
        }
    }

    private void OnHotkeysClicked(object sender, RoutedEventArgs e)
    {
        _onOpenHotkeys?.Invoke();
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        _onOpenSettings?.Invoke();
    }

    private void OnAboutClicked(object sender, RoutedEventArgs e)
    {
        _onOpenAbout?.Invoke();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                if (FindParent<System.Windows.Controls.Primitives.ButtonBase>(dep) is not null ||
                    FindParent<System.Windows.Controls.Slider>(dep) is not null ||
                    FindParent<System.Windows.Controls.Primitives.Thumb>(dep) is not null)
                {
                    return;
                }
            }
            DragMove();
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);
        if (parentObject is null) return null;
        if (parentObject is T parent) return parent;
        return FindParent<T>(parentObject);
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (IsPinned) return;
        if (!IsActive) HidePopover();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel == null) return;

        switch (e.Key)
        {
            case Key.Escape:
                HidePopover();
                e.Handled = true;
                break;

            case Key.Space:
            case Key.K:
            case Key.MediaPlayPause:
                if (_viewModel.TogglePlayPauseCommand.CanExecute(null))
                    _viewModel.TogglePlayPauseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Right:
            case Key.L:
            case Key.MediaNextTrack:
                if (_viewModel.NextCommand.CanExecute(null))
                    _viewModel.NextCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Left:
            case Key.J:
            case Key.MediaPreviousTrack:
                if (_viewModel.PreviousCommand.CanExecute(null))
                    _viewModel.PreviousCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.S:
            case Key.MediaStop:
                if (_viewModel.StopCommand.CanExecute(null))
                    _viewModel.StopCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.O:
            case Key.OemComma:
                _onOpenSettings?.Invoke();
                e.Handled = true;
                break;

            case Key.P:
                OnPinClicked(this, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.M:
                if (_viewModel.ToggleMuteCommand.CanExecute(null))
                    _viewModel.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Up:
                AdjustVolume(5.0);
                e.Handled = true;
                break;

            case Key.Down:
                AdjustVolume(-5.0);
                e.Handled = true;
                break;
        }
    }

    private void AdjustVolume(double deltaPercent)
    {
        if (_viewModel == null) return;
        double current = _viewModel.VolumePercent;
        double newVol = Math.Clamp(current + deltaPercent, 0.0, 100.0);
        if (_viewModel.SetVolumeCommand.CanExecute(newVol))
        {
            _viewModel.SetVolumeCommand.Execute(newVol);
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
}
