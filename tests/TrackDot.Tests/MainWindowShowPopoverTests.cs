using System;
using System.Threading;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Pins down the show-raise contract of <c>MainWindow</c>: after
/// <c>ShowPopover</c> the window must be visible and Topmost,
/// including after an external hide (the integration version of
/// the <c>TrayIconServiceTests.Tray_click_after_external_hide_shows_on_first_click</c>
/// regression).
/// </summary>
public sealed class MainWindowShowPopoverTests
{
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
                window.ShowPopover();
                Assert.True(window.IsVisible, "expected IsVisible after ShowPopover");
                Assert.True(window.Topmost, "expected Topmost=true after ShowPopover");
            }
            catch (Exception ex)
            {
                exception = ex;
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
                window.ShowPopover();
                window.HidePopover();
                window.ShowPopover();
                Assert.True(window.IsVisible);
                Assert.True(window.Topmost);
            }
            catch (Exception ex)
            {
                exception = ex;
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
        if (System.Windows.Application.Current == null)
        {
            // Use App (not a barebones Application) so that App.xaml's
            // <Application.Resources> are loaded — MainWindow.xaml
            // references those resources (brushes, the
            // HeaderActionButton style) and fails to parse without
            // them. The generated App.Main calls
            // InitializeComponent explicitly after `new App()`; the
            // test path must do the same because App.xaml.cs doesn't
            // chain it from its own ctor.
            var app = new App();
            app.InitializeComponent();
        }
    }
}
