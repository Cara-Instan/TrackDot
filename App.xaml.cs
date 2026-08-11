using System;
using System.Threading.Tasks;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using TrackDot.Services;
using TrackDot.ViewModels;

namespace TrackDot;

/// <summary>
/// Composition root. Owns the lifetime of every long-lived
/// service in the application. <see cref="OnStartup"/> builds the
/// dependency graph in dependency order; <see cref="OnExit"/>
/// tears it down in reverse.
/// </summary>
public partial class App : Application
{
    private const string ToolTipTextHealthy = "TrackDot";
    private const string ToolTipTextMediaUnavailable = "TrackDot (media unavailable)";
    private const string SingleInstanceMutexName = @"Local\TrackDot.SingleInstance.v1";

    private UnhandledExceptionLogger? _exceptionLogger;
    private IUnhandledExceptionSink? _exceptionSink;
    private SingleInstanceGuard? _singleInstance;
    private TrayIconService? _tray;
    private ITrayIconHandle? _trayHandle;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private SettingsViewModel? _settingsViewModel;
    private SettingsWindow? _settingsWindow;
    private IStartupService? _startupService;
    private IThemeService? _themeService;
    private IWindowPlacementService? _placement;
    private DispatcherUiTicker? _ticker;
    private MediaControllerService? _mediaService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Step 0: install global exception logger.
        _exceptionSink = new FileUnhandledExceptionSink();
        _exceptionLogger = new UnhandledExceptionLogger(this, _exceptionSink);

        // Step 1: single-instance gate.
        _singleInstance = new SingleInstanceGuard(SingleInstanceMutexName);
        if (!_singleInstance.IsAcquired)
        {
            Shutdown((int)ExitCode.SingleInstanceAlreadyRunning);
            return;
        }

        // Step 1b: theme service composition and initialization.
        _themeService = new ThemeService();
        _themeService.ApplyTheme(_themeService.SelectedTheme);

        // Step 2: build media-control service, view-model, popover window.
        _mediaService = new MediaControllerService();
        _ticker = new DispatcherUiTicker();
        _viewModel = new MainViewModel(_mediaService, _ticker);
        _mainWindow = new MainWindow { DataContext = _viewModel };
        _mainWindow.SetViewModel(_viewModel);

        // Step 3: window-placement service.
        _placement = new WindowPlacementService();
        _mainWindow.SetPlacement(_placement);

        // Step 4: tray icon.
        var taskbarIcon = (TaskbarIcon)Resources["TrayIcon"];
        _trayHandle = new TrayIconHandle(taskbarIcon);
        _trayHandle.SetToolTipText(ToolTipTextHealthy);
        _tray = new TrayIconService(_trayHandle, _mainWindow);
        _tray.ShutdownRequested += OnTrayShutdownRequested;

        // Step 4b: settings UI & theme integration.
        _startupService = new StartupService(new RegistryKeyFactory());
        _settingsViewModel = new SettingsViewModel(_startupService, _themeService);
        _settingsWindow = new SettingsWindow();
        _settingsWindow.SetViewModel(_settingsViewModel, _themeService);

        // Step 5: async SMTC discovery.
        _ = InitializeMediaAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainWindow?.BeginShutdown();
        _settingsWindow?.BeginShutdown();

        try { _tray?.Dispose(); } catch { /* swallow — best effort */ }
        _tray = null;
        _trayHandle = null;

        try { _settingsWindow?.Close(); } catch { /* swallow */ }
        _settingsWindow = null;
        try { _settingsViewModel?.Dispose(); } catch { /* swallow */ }
        _settingsViewModel = null;
        _startupService = null;

        try { _themeService?.Dispose(); } catch { /* swallow */ }
        _themeService = null;

        try { _mainWindow?.Close(); } catch { /* swallow */ }
        _mainWindow = null;
        _placement = null;

        try { _viewModel?.Dispose(); } catch { /* swallow */ }
        _viewModel = null;

        try { _ticker?.Stop(); } catch { /* swallow */ }
        _ticker = null;

        try { _mediaService?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch { /* swallow */ }
        _mediaService = null;

        _singleInstance?.Dispose();
        _singleInstance = null;

        try { _exceptionLogger?.Dispose(); } catch { /* swallow */ }
        _exceptionLogger = null;
        _exceptionSink = null;

        base.OnExit(e);
    }

    private async Task InitializeMediaAsync()
    {
        try
        {
            await _mediaService!.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[TrackDot] SMTC init failed: {ex.GetType().Name}: {ex.Message}");
            try { _trayHandle?.SetToolTipText(ToolTipTextMediaUnavailable); }
            catch { /* swallow — tooltip failure is not fatal */ }
        }
    }


    private void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        _settingsWindow?.ShowSettings();
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        _tray?.RequestShutdown();
    }

    private void OnTrayShutdownRequested(object? sender, EventArgs e)
    {
        Shutdown();
    }
}

public enum ExitCode
{
    SingleInstanceAlreadyRunning = 1,
}