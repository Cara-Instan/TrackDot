using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
/// <remarks>
/// <para>
/// The composition root is intentionally framework-only — the
/// service-level logic (mutex, tray, view-model, placement,
/// exception logger) is exercised in <c>TrackDot.Tests</c>; the
/// only WPF code in this file is the menu-item wiring and the
/// resource lookups that must happen on the UI thread.
/// </para>
/// <para>
/// The exception logger is constructed first so a failure during
/// any subsequent step (mutex, service, view-model, window,
/// tray) is captured in <c>%LocalAppData%\TrackDot\crash.log</c>
/// rather than only on the visual studio debug stream.
/// </para>
/// </remarks>
public partial class App : Application
{
    /// <summary>Tooltip text shown while SMTC is initializing or healthy.</summary>
    private const string ToolTipTextHealthy = "TrackDot";

    /// <summary>Tooltip text shown after SMTC initialization fails.</summary>
    private const string ToolTipTextMediaUnavailable = "TrackDot (media unavailable)";

    // Per-user, per-session. Local\ is required for session-scoped
    // mutexes; without it the mutex is global and a second user on
    // the same machine would be blocked from running TrackDot.
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
    private IWindowPlacementService? _placement;
    private DispatcherUiTicker? _ticker;
    private MediaControllerService? _mediaService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Step 0: install the global exception logger. The sink
        // opens the log file lazily on first write so a failure
        // to create the log directory does not block startup.
        _exceptionSink = new FileUnhandledExceptionSink();
        _exceptionLogger = new UnhandledExceptionLogger(this, _exceptionSink);

        // Step 1: single-instance gate. If another TrackDot is
        // already running, exit cleanly without showing UI.
        _singleInstance = new SingleInstanceGuard(SingleInstanceMutexName);
        if (!_singleInstance.IsAcquired)
        {
            Shutdown((int)ExitCode.SingleInstanceAlreadyRunning);
            return;
        }

        // Step 2: build the media-control service, view-model,
        // and popover window. The view-model takes a dispatcher
        // ticker so its interpolation runs on the UI thread.
        _mediaService = new MediaControllerService();
        _ticker = new DispatcherUiTicker();
        _viewModel = new MainViewModel(_mediaService, _ticker);
        _mainWindow = new MainWindow { DataContext = _viewModel };
        _mainWindow.SetViewModel(_viewModel);

        // Step 3: window-placement service. Resolves the work
        // area of the monitor containing the taskbar and
        // computes the popover's anchored position on every
        // show.
        _placement = new WindowPlacementService();
        _mainWindow.SetPlacement(_placement);

        // Step 4: tray icon. Look up the TaskbarIcon XAML
        // resource by key. The handle is the seam between the
        // WPF icon and the testable tray service.
        var taskbarIcon = (TaskbarIcon)Resources["TrayIcon"];
        WireTrayMenu(taskbarIcon, (ContextMenu)Resources["TrayContextMenu"]);
        _trayHandle = new TrayIconHandle(taskbarIcon);
        _trayHandle.SetToolTipText(ToolTipTextHealthy);
        _tray = new TrayIconService(_trayHandle, _mainWindow);
        _tray.ShutdownRequested += OnTrayShutdownRequested;

        // Step 4b: settings UI. The startup service is backed
        // by the live per-user registry; the view-model owns a
        // single LaunchAtSignIn boolean that persists to the
        // registry immediately on every toggle. The window is
        // created hidden — the tray Settings click is what
        // shows it.
        _startupService = new StartupService(new RegistryKeyFactory());
        _settingsViewModel = new SettingsViewModel(_startupService);
        _settingsWindow = new SettingsWindow();
        _settingsWindow.SetViewModel(_settingsViewModel);

        // Step 5: kick off SMTC discovery asynchronously so a
        // failure during initialization does not block the tray
        // icon from appearing. The post-init path updates the
        // tray tooltip on failure so the user can see the
        // degraded state at a glance.
        _ = InitializeMediaAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Set the shutdown flag so any in-flight MainWindow /
        // SettingsWindow Closing events are not turned into
        // hides. The type-name is fully qualified to escape
        // the Application.MainWindow property shadow (see
        // Task 8 gotcha #1 in HANDOFF.md). The settings
        // window does not have a same-name collision but is
        // qualified for symmetry.
        TrackDot.MainWindow.IsShuttingDown = true;
        TrackDot.SettingsWindow.IsShuttingDown = true;

        // Tear down in reverse-construction order. Each step is
        // idempotent / null-safe so a half-constructed OnStartup
        // (e.g. single-instance failed) does not throw here.
        try { _tray?.Dispose(); } catch { /* swallow — best effort */ }
        _tray = null;
        _trayHandle = null;

        // Settings window and view-model live in the same
        // dependency tier as MainWindow: constructed last,
        // torn down alongside. The startup service is a plain
        // object with no native handle to release — drop the
        // reference and let GC handle it.
        try { _settingsWindow?.Close(); } catch { /* swallow */ }
        _settingsWindow = null;
        try { _settingsViewModel?.Dispose(); } catch { /* swallow */ }
        _settingsViewModel = null;
        _startupService = null;

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

        // The exception logger is the last thing disposed so
        // any exception thrown during the teardown of the
        // services above is captured before the process exits.
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
            // SMTC initialization failures are recoverable: the
            // tray remains usable and the popover shows the empty
            // state. The exception logger (registered in
            // OnStartup) writes the full exception to
            // %LocalAppData%\TrackDot\crash.log; the tooltip
            // update is the user-visible signal that something
            // went wrong so the user does not assume the app is
            // silently broken.
            System.Diagnostics.Debug.WriteLine(
                $"[TrackDot] SMTC init failed: {ex.GetType().Name}: {ex.Message}");
            try { _trayHandle?.SetToolTipText(ToolTipTextMediaUnavailable); }
            catch { /* swallow — tooltip failure is not fatal */ }
        }
    }

    private void WireTrayMenu(TaskbarIcon icon, ContextMenu menu)
    {
        // The menu items are declared in App.xaml with
        // x:Name="OpenSettingsMenuItem" and "ExitMenuItem".
        // We resolve them by walking the menu's items.
        foreach (var item in menu.Items)
        {
            if (item is not MenuItem mi) continue;
            switch (mi.Name)
            {
                case "OpenSettingsMenuItem":
                    mi.Click += OnOpenSettingsClicked;
                    break;
                case "ExitMenuItem":
                    mi.Click += OnExitClicked;
                    break;
            }
        }
    }

    private void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        // ShowSettings is idempotent — a second tray click
        // activates the window instead of duplicating it.
        _settingsWindow?.ShowSettings();
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        _tray?.RequestShutdown();
    }

    private void OnTrayShutdownRequested(object? sender, EventArgs e)
    {
        // Quit the dispatcher loop. OnExit will run, releasing
        // every service in reverse order.
        Shutdown();
    }
}

/// <summary>Application exit codes.</summary>
public enum ExitCode
{
    /// <summary>Another TrackDot instance is already running; exit silently.</summary>
    SingleInstanceAlreadyRunning = 1,
}