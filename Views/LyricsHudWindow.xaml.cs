using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TrackDot.Services;
using TrackDot.ViewModels;

namespace TrackDot.Views;

/// <summary>
/// Mini floating desktop subtitle HUD overlay window with click-through support.
/// </summary>
public partial class LyricsHudWindow : Window
{
    private LyricsHudViewModel? _viewModel;
    private IWindowSettingsService? _settingsService;
    private Action? _onOpenFullLyrics;
    private LyricsViewModel? _fullLyricsViewModel;
    private bool _isShuttingDown;
    private bool _isLoaded;

    public LyricsHudWindow()
    {
        InitializeComponent();
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
    }

    public void SetViewModel(
        LyricsHudViewModel viewModel,
        LyricsViewModel fullLyricsViewModel,
        IWindowSettingsService settingsService,
        Action onOpenFullLyrics)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _fullLyricsViewModel = fullLyricsViewModel ?? throw new ArgumentNullException(nameof(fullLyricsViewModel));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _onOpenFullLyrics = onOpenFullLyrics ?? throw new ArgumentNullException(nameof(onOpenFullLyrics));

        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Restore window geometry
        if (_settingsService.LyricsHudWidth >= 300) Width = _settingsService.LyricsHudWidth;
        if (_settingsService.LyricsHudHeight >= 60) Height = _settingsService.LyricsHudHeight;

        if (_settingsService.LyricsHudLeft >= 0 && _settingsService.LyricsHudTop >= 0)
        {
            Left = _settingsService.LyricsHudLeft;
            Top = _settingsService.LyricsHudTop;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        else
        {
            // Default: bottom center of screen
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    public void BeginShutdown() => _isShuttingDown = true;

    public void ShowHud()
    {
        if (!IsVisible)
        {
            Show();
        }
        if (_settingsService != null)
        {
            _settingsService.LyricsHudVisible = true;
        }
        UpdateClickThroughStyle();
    }

    public void HideHud()
    {
        if (IsVisible)
        {
            Hide();
        }
        if (_settingsService != null)
        {
            _settingsService.LyricsHudVisible = false;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _isLoaded = true;
        UpdateClickThroughStyle();
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (_viewModel != null && !_viewModel.IsLocked)
        {
            HoverToolbar.Opacity = 1.0;
            HoverToolbar.IsHitTestVisible = true;
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        HoverToolbar.Opacity = 0.0;
        HoverToolbar.IsHitTestVisible = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LyricsHudViewModel.IsLocked))
        {
            UpdateClickThroughStyle();
        }
    }

    private void UpdateClickThroughStyle()
    {
        if (!_isLoaded) return;

        var helper = new WindowInteropHelper(this);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero) return;

        var currentExStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();

        if (_viewModel?.IsLocked == true)
        {
            // Enable click-through
            long newExStyle = currentExStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(newExStyle));
            HoverToolbar.Opacity = 0.0;
            HoverToolbar.IsHitTestVisible = false;
        }
        else
        {
            // Disable click-through (allow mouse interaction and dragging)
            long newExStyle = (currentExStyle & ~NativeMethods.WS_EX_TRANSPARENT) | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(newExStyle));
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel?.IsLocked == true) return;

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
                // Ignore if mouse was released quickly
            }
        }
    }

    private static bool IsInteractiveControl(DependencyObject source)
    {
        return FindAncestorOrSelf<System.Windows.Controls.Primitives.ButtonBase>(source) != null ||
               FindAncestorOrSelf<System.Windows.Controls.TextBox>(source) != null;
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
            _settingsService.LyricsHudLeft = Left;
            _settingsService.LyricsHudTop = Top;
            _settingsService.LyricsHudWidth = Width;
            _settingsService.LyricsHudHeight = Height;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                string filePath = files[0];
                if (File.Exists(filePath))
                {
                    try
                    {
                        string content = File.ReadAllText(filePath);
                        _ = _fullLyricsViewModel?.LoadCustomLyricsAsync(content);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LyricsHudWindow] Drop file error: {ex.Message}");
                    }
                }
            }
        }
    }

    private void OnOpenFullLyricsClicked(object sender, RoutedEventArgs e)
    {
        _onOpenFullLyrics?.Invoke();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        HideHud();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isShuttingDown)
        {
            e.Cancel = true;
            HideHud();
        }
    }
}

