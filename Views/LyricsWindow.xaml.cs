using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TrackDot.Services;
using TrackDot.ViewModels;

namespace TrackDot.Views;

/// <summary>
/// Resizable, sticky, transparent window displaying synchronized track lyrics.
/// </summary>
public partial class LyricsWindow : Window
{
    private LyricsViewModel? _viewModel;
    private IWindowSettingsService? _settingsService;
    private bool _isShuttingDown;
    private bool _isLoaded;
    private readonly IDwmInterop _dwm = new DwmInterop();

    public LyricsWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(LyricsViewModel viewModel, IWindowSettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(settingsService);

        _viewModel = viewModel;
        _settingsService = settingsService;
        DataContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Restore window geometry
        if (_settingsService.LyricsWindowWidth >= 200) Width = _settingsService.LyricsWindowWidth;
        if (_settingsService.LyricsWindowHeight >= 200) Height = _settingsService.LyricsWindowHeight;

        if (_settingsService.LyricsWindowLeft >= 0 && _settingsService.LyricsWindowTop >= 0)
        {
            Left = _settingsService.LyricsWindowLeft;
            Top = _settingsService.LyricsWindowTop;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    public void BeginShutdown() => _isShuttingDown = true;

    public void ShowLyrics()
    {
        if (!IsVisible)
        {
            Show();
            Activate();
        }
        if (_settingsService != null)
        {
            _settingsService.LyricsWindowVisible = true;
        }
    }

    public void HideLyrics()
    {
        if (IsVisible)
        {
            Hide();
        }
        if (_settingsService != null)
        {
            _settingsService.LyricsWindowVisible = false;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _isLoaded = true;

        if (_viewModel != null)
        {
            _viewModel.UpdateWindowHeight(ActualHeight);
        }

        // Migrate from layered-alpha HWND to opaque HWND with DWM
        // rounded corners on Win11 22H2+. See MainWindow.OnSourceInitialized
        // for the full rationale.
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (_dwm.TryApplyRoundedCorners(hwnd) == DwmCornerApplyResult.Applied)
        {
            AllowsTransparency = false;
            Background = (System.Windows.Media.Brush)FindResource("PanelBrush");
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.OriginalSource is DependencyObject source && IsInteractiveControl(source))
            {
                return;
            }

            try
            {
                DragMove();
                SaveGeometry();
            }
            catch (InvalidOperationException)
            {
                // Ignore if mouse button was released before DragMove initialized
            }
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Window_MouseLeftButtonDown(sender, e);
    }

    private static bool IsInteractiveControl(DependencyObject source)
    {
        return FindAncestorOrSelf<System.Windows.Controls.Primitives.ButtonBase>(source) != null ||
               FindAncestorOrSelf<Slider>(source) != null ||
               FindAncestorOrSelf<System.Windows.Controls.Primitives.Thumb>(source) != null ||
               FindAncestorOrSelf<TextBox>(source) != null ||
               IsLyricLineItem(source);
    }

    private static bool IsLyricLineItem(DependencyObject source)
    {
        var border = FindAncestorOrSelf<Border>(source);
        return border != null && border.DataContext is TrackDot.Models.LyricLine;
    }

    private static T? FindAncestorOrSelf<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? current = child;
        while (current != null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.UpdateWindowHeight(e.NewSize.Height);
        }
        if (_isLoaded)
        {
            SaveGeometry();
        }
    }

    private void SaveGeometry()
    {
        if (_settingsService == null || !_isLoaded) return;
        if (WindowState == WindowState.Normal)
        {
            _settingsService.LyricsWindowLeft = Left;
            _settingsService.LyricsWindowTop = Top;
            _settingsService.LyricsWindowWidth = Width;
            _settingsService.LyricsWindowHeight = Height;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        HideLyrics();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LyricsViewModel.ActiveLineIndex))
        {
            Dispatcher.InvokeAsync(ScrollToActiveLine, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void ScrollToActiveLine()
    {
        if (_viewModel == null || LyricsScrollViewer == null || LyricsItemsControl == null) return;
        int index = _viewModel.ActiveLineIndex;
        if (index < 0 || index >= LyricsItemsControl.Items.Count) return;

        var container = LyricsItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
        if (container == null) return;

        // Calculate offset to center the active item inside the scroll viewer
        Point relativePoint = container.TransformToAncestor(LyricsScrollViewer).Transform(new Point(0, 0));
        double currentOffset = LyricsScrollViewer.VerticalOffset;
        double elementTop = currentOffset + relativePoint.Y;
        double elementHeight = container.ActualHeight;
        double viewportHeight = LyricsScrollViewer.ViewportHeight;

        double targetOffset = elementTop - (viewportHeight / 2.0) + (elementHeight / 2.0);
        targetOffset = Math.Clamp(targetOffset, 0, LyricsScrollViewer.ScrollableHeight);

        // Smooth scroll animation
        var animation = new DoubleAnimation
        {
            From = currentOffset,
            To = targetOffset,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var animationClock = animation.CreateClock();
        animationClock.Completed += (s, e) => LyricsScrollViewer.ScrollToVerticalOffset(targetOffset);

        // Custom animation helper for ScrollViewer.VerticalOffset
        LyricsScrollViewer.BeginAnimation(ScrollViewerBehavior.VerticalOffsetProperty, animation);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideLyrics();
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isShuttingDown)
        {
            e.Cancel = true;
            HideLyrics();
        }
    }
}

/// <summary>
/// Helper dependency property attached to ScrollViewer for smooth VerticalOffset animation.
/// </summary>
public static class ScrollViewerBehavior
{
    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.RegisterAttached(
        "VerticalOffset",
        typeof(double),
        typeof(ScrollViewerBehavior),
        new FrameworkPropertyMetadata(0.0, OnVerticalOffsetChanged));

    public static double GetVerticalOffset(ScrollViewer element) => (double)element.GetValue(VerticalOffsetProperty);
    public static void SetVerticalOffset(ScrollViewer element, double value) => element.SetValue(VerticalOffsetProperty, value);

    private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer viewer)
        {
            viewer.ScrollToVerticalOffset((double)e.NewValue);
        }
    }
}
