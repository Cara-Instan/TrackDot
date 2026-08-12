using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Navigation;
using TrackDot.Services;

namespace TrackDot.Views;

/// <summary>
/// The About window displaying application details, repository links, author details,
/// and AI benchmark notes. Follows the single-instance window lifecycle pattern.
/// </summary>
public partial class AboutWindow : Window
{
    private const string DefaultRepoUrl = "https://github.com/Cara-Instan/TrackDot";
    private const string DefaultAuthorUrl = "https://github.com/herlandroando";

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    private bool _isShuttingDown;
    private IThemeService? _themeService;

    public void BeginShutdown() => _isShuttingDown = true;

    public AboutWindow()
    {
        InitializeComponent();
        SourceInitialized += Window_SourceInitialized;
        SetVersionText();
    }

    private void SetVersionText()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(infoVersion))
            {
                VersionTextBlock.Text = infoVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? infoVersion
                    : $"v{infoVersion}";
                return;
            }

            var version = assembly.GetName().Version;
            if (version != null)
            {
                VersionTextBlock.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
                return;
            }
        }
        catch
        {
            // Fallback default
        }

        VersionTextBlock.Text = "v0.1.0-beta";
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

    public void ShowAbout()
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

    private void OnOpenRepoClicked(object sender, RoutedEventArgs e)
    {
        OpenUrl(DefaultRepoUrl);
    }

    private void OnOpenAuthorClicked(object sender, RoutedEventArgs e)
    {
        OpenUrl(DefaultAuthorUrl);
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Swallow if default browser failed to launch
        }
    }
}
