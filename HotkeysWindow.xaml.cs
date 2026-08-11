using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TrackDot.Services;

namespace TrackDot;

/// <summary>
/// The Keyboard Shortcuts reference window displaying hotkeys and media keys.
/// Follows the single-instance window lifecycle pattern.
/// </summary>
public partial class HotkeysWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    private bool _isShuttingDown;
    private IThemeService? _themeService;

    public void BeginShutdown() => _isShuttingDown = true;

    public HotkeysWindow()
    {
        InitializeComponent();
        SourceInitialized += Window_SourceInitialized;
    }

    public void SetThemeService(IThemeService? themeService)
    {
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

    public void ShowHotkeys()
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
        Hide();
    }
}
