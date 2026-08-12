using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using TrackDot.Views;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Pins down the show-raise contract of <c>MainWindow</c>: after
/// <c>ShowPopover</c> the window must be visible and Topmost,
/// including after an external hide (the integration version of
/// the <c>TrayIconServiceTests.Tray_click_after_external_hide_shows_on_first_click</c>
/// regression).
/// </summary>
/// <remarks>
/// Each test runs on a dedicated STA thread that hosts its own
/// <see cref="Dispatcher"/>. After <c>ShowPopover</c> returns we
/// pump the dispatcher briefly so the Win32 subclass callbacks
/// that <see cref="Window.Activate"/> schedules have a chance to
/// land before the STA thread exits — otherwise the callback
/// races with the dispatcher's teardown and the test host crashes
/// with <c>NullReferenceException</c> inside
/// <c>HwndSubclass.SubclassWndProc</c>.
/// </remarks>
[Collection("WPF")]
public sealed class MainWindowShowPopoverTests
{
    private static readonly TimeSpan PumpTimeout = TimeSpan.FromMilliseconds(200);

    [Fact]
    public void ShowPopover_makes_window_visible_and_topmost()
    {
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                var window = new MainWindow();
                RunOnDispatcher(window.ShowPopover);
                PumpDispatcher();
                Assert.True(window.IsVisible, "expected IsVisible after ShowPopover");
                Assert.True(window.Topmost, "expected Topmost=true after ShowPopover");
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                // Shut the dispatcher down cleanly so any queued
                // callbacks bail out before the STA thread exits.
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null) throw exception;
    }

    [Fact]
    public void ShowPopover_does_not_change_Topmost_when_not_visible_already()
    {
        // Regression guard: the show-and-raise path must be
        // idempotent. A second ShowPopover after a hide must still
        // produce a visible + topmost window.
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                var window = new MainWindow();
                // Call twice: the second call should still drive the
                // topmost re-assert, since the public contract is
                // "every show must raise".
                RunOnDispatcher(window.ShowPopover);
                PumpDispatcher();
                RunOnDispatcher(window.HidePopover);
                PumpDispatcher();
                RunOnDispatcher(window.ShowPopover);
                PumpDispatcher();
                Assert.True(window.IsVisible);
                Assert.True(window.Topmost);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null) throw exception;
    }

    [Fact]
    public void After_external_hide_next_show_raises_window()
    {
        // Integration version of TrayIconServiceTests.
        // Tray_click_after_external_hide_shows_on_first_click, but
        // driven against the real MainWindow STA-hosted, no
        // FakePopoverHost and no service layer.
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                var window = new MainWindow();
                RunOnDispatcher(window.ShowPopover);
                PumpDispatcher();
                Assert.True(window.IsVisible);

                // Simulate the Window_Deactivated side-channel:
                // the window is hidden by code that doesn't go
                // through the tray service.
                RunOnDispatcher(window.HidePopover);
                PumpDispatcher();
                Assert.False(window.IsVisible);

                // Tray-click equivalent: a single ShowPopover call.
                // The user-visible contract is "one click brings
                // it back, topmost".
                RunOnDispatcher(window.ShowPopover);
                PumpDispatcher();
                Assert.True(window.IsVisible, "expected IsVisible after single show call");
                Assert.True(window.Topmost, "expected Topmost=true after single show call");
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null) throw exception;
    }

    private static void EnsureApplication()
        {
            // ResourceAssembly must be set BEFORE the Application is
            // created so that App.xaml's LoadComponent call (which uses
            // a relative "/TrackDot;component/app.xaml" URI) resolves to
            // the production assembly.
            if (System.Windows.Application.ResourceAssembly == null)
            {
                System.Windows.Application.ResourceAssembly =
                    typeof(MainWindow).Assembly;
            }
            // App.xaml's <Application.Resources> are loaded only when
            // an App instance is constructed and InitializeComponent is
            // called explicitly (production does this from App.Main;
            // App.xaml.cs's own ctor does NOT chain it). MainWindow.xaml
            // references those resources (brushes, the
            // HeaderActionButton style) and fails to parse without them.
            //
            // WPF refuses to create more than one Application instance
            // per AppDomain, and the assembly-level
            // [assembly: CollectionBehavior(DisableTestParallelization = true)]
            // in WpfTestAssemblyInit.cs ensures this is the only path
            // that creates it. (AboutWindowTests would otherwise
            // construct a barebones Application and lock the singleton
            // before we get a chance to install App.xaml's resources.)
            if (System.Windows.Application.Current is not App)
            {
                var app = new App();
                app.InitializeComponent();
            }
        }

    private static void RunOnDispatcher(Action action)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    private static void PumpDispatcher()
    {
        // Process whatever the dispatcher has queued so far (most
        // importantly the WM_ACTIVATEAPP subclass callback that
        // Window.Activate schedules). Without this the queued
        // callback fires after the STA thread has begun teardown
        // and crashes the test host.
        var frame = new DispatcherFrame();
        var until = DateTime.UtcNow + PumpTimeout;
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                while (frame.Continue && DateTime.UtcNow < until)
                {
                    // Yield to other queued callbacks once, then
                    // break out of the frame.
                }
                frame.Continue = false;
            }));
        Dispatcher.PushFrame(frame);
    }
}
