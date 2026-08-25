using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TrackDot.Services;
using TrackDot.ViewModels;

namespace TrackDot.Views;

/// <summary>
/// The settings window. A normal top-level WPF window (not a
/// popover) so the user can move, resize, and dismiss it
/// independently of the popover. Closing the window is
/// intercepted and turned into a <see cref="Hide"/> while the
/// application is still running.
/// </summary>
public partial class SettingsWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    /// <summary>
    /// Signals that the application is shutting down.
    /// Call <see cref="BeginShutdown"/> from the composition root
    /// instead of setting a static property.
    /// </summary>
    private bool _isShuttingDown;

    /// <summary>Called by the composition root before closing the window.</summary>
    public void BeginShutdown() => _isShuttingDown = true;

    private SettingsViewModel? _viewModel;
    private IThemeService? _themeService;

    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += Window_SourceInitialized;
    }

    /// <summary>
    /// Wires the window to its view-model and theme service.
    /// </summary>
    public void SetViewModel(SettingsViewModel viewModel, IThemeService? themeService = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        _viewModel = viewModel;

        if (_themeService != null)
        {
            _themeService.EffectiveThemeChanged -= OnEffectiveThemeChanged;
        }

        _themeService = themeService;
        if (_themeService != null)
        {
            _themeService.EffectiveThemeChanged += OnEffectiveThemeChanged;
            UpdateTitleBarTheme(_themeService.IsEffectiveDark);
        }
    }

    public void ShowSettings()
    {
        if (!IsVisible)
        {
            Show();
        }
        else
        {
            Activate();
        }

        if (_themeService != null)
        {
            UpdateTitleBarTheme(_themeService.IsEffectiveDark);
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (_themeService != null)
        {
            UpdateTitleBarTheme(_themeService.IsEffectiveDark);
        }
    }

    private void OnEffectiveThemeChanged(object? sender, bool isDark)
    {
        Dispatcher.Invoke(() => UpdateTitleBarTheme(isDark));
    }

    private void UpdateTitleBarTheme(bool isDark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int darkMode = isDark ? 1 : 0;
            try
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }
            catch
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
            }
        }
        catch
        {
            // Non-fatal if OS/DWM API call is unsupported
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isShuttingDown)
        {
            e.Cancel = true;
            CancelAnyRecording();
            Hide();
        }
    }

    private void CancelAnyRecording()
    {
        if (_viewModel != null)
        {
            foreach (var item in _viewModel.HotkeyItems)
            {
                item.IsRecording = false;
            }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel == null) return;

        var recordingItem = System.Linq.Enumerable.FirstOrDefault(_viewModel.HotkeyItems, i => i.IsRecording);
        if (recordingItem != null)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                recordingItem.IsRecording = false;
                e.Handled = true;
                return;
            }

            // Ignore bare modifier presses
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                return;
            }

            var modifiers = Keyboard.Modifiers;
            recordingItem.Commit(modifiers, key);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void OnRecordHotkeyClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is HotkeySettingItemViewModel item)
        {
            if (_viewModel != null)
            {
                foreach (var other in _viewModel.HotkeyItems)
                {
                    if (other != item) other.IsRecording = false;
                }
            }
            item.IsRecording = !item.IsRecording;
        }
    }

    private void OnResetHotkeysClicked(object sender, RoutedEventArgs e)
    {
        _viewModel?.ResetHotkeysToDefault();
    }

    private void OnClearSourceAppsClicked(object sender, RoutedEventArgs e)
    {
        _viewModel?.ClearSourceApps();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        CancelAnyRecording();
        Hide();
    }
}
