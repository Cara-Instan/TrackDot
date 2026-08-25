using System;
using System.Threading.Tasks;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using TrackDot.Services;
using TrackDot.ViewModels;
using TrackDot.Views;

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
    private AboutWindow? _aboutWindow;
    private HotkeysWindow? _hotkeysWindow;
    private HotkeysViewModel? _hotkeysViewModel;
    private WindowSettingsService? _windowSettingsService;
    private IStartupService? _startupService;
    private IThemeService? _themeService;
    private WpfThemePaletteApplier? _themePaletteApplier;
    private IWindowPlacementService? _placement;
    private DispatcherUiTicker? _ticker;
    private MediaControllerService? _mediaService;
    private GlobalHotkeyService? _globalHotkeyService;
    private LyricsService? _lyricsService;
    private DispatcherUiTicker? _lyricsTicker;
    private LyricsViewModel? _lyricsViewModel;
    private LyricsWindow? _lyricsWindow;
    private LyricsHudViewModel? _lyricsHudViewModel;
    private LyricsHudWindow? _lyricsHudWindow;

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
        _themePaletteApplier = new WpfThemePaletteApplier(_themeService);
        _themePaletteApplier.ApplyInitial();

        // Step 2: build window settings & secondary windows (About, Hotkeys, Settings).
        _windowSettingsService = new WindowSettingsService();
        _startupService = new StartupService(new RegistryKeyFactory());
        _settingsViewModel = new SettingsViewModel(_startupService, _themeService, _windowSettingsService);
        _settingsWindow = new SettingsWindow();
        _settingsWindow.SetViewModel(_settingsViewModel, _themeService);

        _aboutWindow = new AboutWindow();
        _aboutWindow.SetThemeService(_themeService);

        _hotkeysViewModel = new HotkeysViewModel(_windowSettingsService);
        _hotkeysWindow = new HotkeysWindow();
        _hotkeysWindow.SetViewModel(_hotkeysViewModel, _themeService);

        // Step 3: build media-control service, view-model, popover window, lyrics window and HUD.
        _mediaService = new MediaControllerService();
        _ticker = new DispatcherUiTicker();
        _viewModel = new MainViewModel(_mediaService, _ticker, windowSettingsService: _windowSettingsService);

        _lyricsService = new LyricsService();
        _lyricsTicker = new DispatcherUiTicker();
        _lyricsViewModel = new LyricsViewModel(_mediaService, _lyricsService, _lyricsTicker, _windowSettingsService);
        _lyricsWindow = new LyricsWindow();
        _lyricsWindow.SetViewModel(_lyricsViewModel, _windowSettingsService, onToggleHud: () => ToggleLyricsHud());

        _lyricsHudViewModel = new LyricsHudViewModel(_lyricsViewModel, _mediaService, _windowSettingsService);
        _lyricsHudWindow = new LyricsHudWindow();
        _lyricsHudWindow.SetViewModel(_lyricsHudViewModel, _lyricsViewModel, _windowSettingsService, onOpenFullLyrics: () => _lyricsWindow?.ShowLyrics());

        _mainWindow = new MainWindow { DataContext = _viewModel };
        _mainWindow.SetViewModel(_viewModel);
        _mainWindow.SetHeaderActions(
            onOpenSettings: () => _settingsWindow?.ShowSettings(),
            onOpenAbout: () => _aboutWindow?.ShowAbout(),
            onOpenHotkeys: () => _hotkeysWindow?.ShowHotkeys(),
            onOpenLyrics: () => _lyricsWindow?.ShowLyrics(),
            onOpenLyricsHud: () => ToggleLyricsHud(),
            windowSettingsService: _windowSettingsService);

        if (_windowSettingsService.LyricsWindowVisible)
        {
            _lyricsWindow.ShowLyrics();
        }

        if (_windowSettingsService.LyricsHudVisible)
        {
            _lyricsHudWindow.ShowHud();
        }

        // Step 3b: global hotkey service.
        _globalHotkeyService = new GlobalHotkeyService(
            _viewModel,
            _windowSettingsService,
            onToggleWindow: () => _tray?.TogglePopover(),
            onOpenSettings: () => _settingsWindow?.ShowSettings(),
            onToggleLyrics: () => ToggleLyrics(),
            onToggleLyricsHud: () => ToggleLyricsHud());
        _mainWindow.SourceInitialized += (s, ev) => UpdateGlobalHotkeysRegistration();
        _windowSettingsService.SettingsChanged += (s, ev) => UpdateGlobalHotkeysRegistration();
        UpdateGlobalHotkeysRegistration();

        // Step 4: window-placement service.
        _placement = new WindowPlacementService();
        _mainWindow.SetPlacement(_placement);

        // Step 5: tray icon.
        var taskbarIcon = (TaskbarIcon)Resources["TrayIcon"];
        _trayHandle = new TrayIconHandle(taskbarIcon);
        _trayHandle.SetToolTipText(ToolTipTextHealthy);
        _tray = new TrayIconService(_trayHandle, _mainWindow);
        _tray.ShutdownRequested += OnTrayShutdownRequested;

        // Step 6: async SMTC discovery.
        _ = InitializeMediaAsync();
    }

    private void ToggleLyrics()
    {
        if (_lyricsWindow == null) return;
        if (_lyricsWindow.IsVisible) _lyricsWindow.HideLyrics();
        else _lyricsWindow.ShowLyrics();
    }

    private void ToggleLyricsHud()
    {
        if (_lyricsHudWindow == null) return;
        if (_lyricsHudWindow.IsVisible) _lyricsHudWindow.HideHud();
        else _lyricsHudWindow.ShowHud();
    }

    private void UpdateGlobalHotkeysRegistration()
    {
        if (_globalHotkeyService == null || _mainWindow == null || _windowSettingsService == null) return;
        var helper = new System.Windows.Interop.WindowInteropHelper(_mainWindow);
        if (helper.Handle == IntPtr.Zero)
        {
            helper.EnsureHandle();
        }
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero) return;

        if (_windowSettingsService.EnableGlobalHotkeys)
        {
            if (!_globalHotkeyService.IsRegistered)
                _globalHotkeyService.Register(hwnd);
            else
                _globalHotkeyService.Reregister();
        }
        else
        {
            if (_globalHotkeyService.IsRegistered)
                _globalHotkeyService.Unregister();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainWindow?.BeginShutdown();
        _lyricsWindow?.BeginShutdown();
        _lyricsHudWindow?.BeginShutdown();
        _settingsWindow?.BeginShutdown();
        _aboutWindow?.BeginShutdown();
        _hotkeysWindow?.BeginShutdown();

        try { _globalHotkeyService?.Dispose(); } catch { /* swallow */ }
        _globalHotkeyService = null;

        try { _tray?.Dispose(); } catch { /* swallow — best effort */ }
        _tray = null;
        _trayHandle = null;

        try { _lyricsHudWindow?.Close(); } catch { /* swallow */ }
        _lyricsHudWindow = null;
        try { _lyricsHudViewModel?.Dispose(); } catch { /* swallow */ }
        _lyricsHudViewModel = null;

        try { _lyricsWindow?.Close(); } catch { /* swallow */ }
        _lyricsWindow = null;
        try { _lyricsViewModel?.Dispose(); } catch { /* swallow */ }
        _lyricsViewModel = null;
        try { _lyricsTicker?.Stop(); } catch { /* swallow */ }
        _lyricsTicker = null;
        _lyricsService = null;

        try { _hotkeysWindow?.Close(); } catch { /* swallow */ }
        _hotkeysWindow = null;
        try { _hotkeysViewModel?.Dispose(); } catch { /* swallow */ }
        _hotkeysViewModel = null;
        try { _aboutWindow?.Close(); } catch { /* swallow */ }
        _aboutWindow = null;

        try { _settingsWindow?.Close(); } catch { /* swallow */ }
        _settingsWindow = null;
        try { _settingsViewModel?.Dispose(); } catch { /* swallow */ }
        _settingsViewModel = null;
        _startupService = null;
        _windowSettingsService = null;

        try { _themePaletteApplier?.Dispose(); } catch { /* swallow */ }
        _themePaletteApplier = null;
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

    private void OnOpenHotkeysClicked(object sender, RoutedEventArgs e)
    {
        _hotkeysWindow?.ShowHotkeys();
    }

    private void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        _settingsWindow?.ShowSettings();
    }

    private void OnOpenAboutClicked(object sender, RoutedEventArgs e)
    {
        _aboutWindow?.ShowAbout();
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
