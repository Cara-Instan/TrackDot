using System;
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
/// This class is intentionally framework-only — no logic that
/// needs unit testing lives here. The service-level logic
/// (mutex, tray, view-model) is exercised in
/// <c>TrackDot.Tests</c>; the only WPF code in this file is the
/// menu-item wiring.
/// </remarks>
public partial class App : Application
{
    // Per-user, per-session. Local\ is required for session-scoped
    // mutexes; without it the mutex is global and a second user on
    // the same machine would be blocked from running TrackDot.
    private const string SingleInstanceMutexName = @"Local\TrackDot.SingleInstance.v1";

    private SingleInstanceGuard? _singleInstance;
    private TrayIconService? _tray;
    private ITrayIconHandle? _trayHandle;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private DispatcherUiTicker? _ticker;
    private MediaControllerService? _mediaService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        // Step 3: tray icon. Look up the TaskbarIcon XAML resource
        // by key. The handle is the seam between the WPF icon and
        // the testable tray service.
        var taskbarIcon = (TaskbarIcon)Resources["TrayIcon"];
        WireTrayMenu(taskbarIcon, (ContextMenu)Resources["TrayContextMenu"]);
        _trayHandle = new TrayIconHandle(taskbarIcon);
        _tray = new TrayIconService(_trayHandle, _mainWindow);
        _tray.ShutdownRequested += OnTrayShutdownRequested;

        // Step 4: kick off SMTC discovery asynchronously so a
        // failure during initialization does not block the tray
        // icon from appearing.
        _ = InitializeMediaAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Set the shutdown flag so any in-flight MainWindow
                // Closing events are not turned into hides.
                TrackDot.MainWindow.IsShuttingDown = true;

        // Tear down in reverse-construction order. Each step is
        // idempotent / null-safe so a half-constructed OnStartup
        // (e.g. single-instance failed) does not throw here.
        try { _tray?.Dispose(); } catch { /* swallow — best effort */ }
        _tray = null;
        _trayHandle = null;

        try { _mainWindow?.Close(); } catch { /* swallow */ }
        _mainWindow = null;

        try { _viewModel?.Dispose(); } catch { /* swallow */ }
        _viewModel = null;

        try { _ticker?.Stop(); } catch { /* swallow */ }
                _ticker = null;

        try { _mediaService?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch { /* swallow */ }
        _mediaService = null;

        _singleInstance?.Dispose();
        _singleInstance = null;

        base.OnExit(e);
    }

    private async System.Threading.Tasks.Task InitializeMediaAsync()
    {
        try
        {
            await _mediaService!.InitializeAsync();
        }
        catch (Exception ex)
        {
            // SMTC initialization failures are recoverable: the
            // tray remains usable and the popover shows the empty
            // state. Log to debug output (Task 9 will own a real
            // logger).
            System.Diagnostics.Debug.WriteLine(
                $"[TrackDot] SMTC init failed: {ex.GetType().Name}: {ex.Message}");
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
        // Settings window is owned by Task 10. For now, surface
        // a debug message so the menu item is wired and visible.
        System.Diagnostics.Debug.WriteLine(
            "[TrackDot] Settings menu clicked — Settings window not yet implemented (Task 10).");
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