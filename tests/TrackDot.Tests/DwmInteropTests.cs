using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Pins down the DWM interop helper that drives the Win11 22H2+
/// <c>DWMWA_WINDOW_CORNER_PREFERENCE</c> migration. The version
/// detector is pure math (build-number boundary); the apply call
/// is host-conditional — older OS builds return
/// <see cref="DwmCornerApplyResult.NotSupportedOnThisOs"/> without
/// touching DWM, and Win11 22H2+ hosts return either
/// <see cref="DwmCornerApplyResult.Applied"/> or
/// <see cref="DwmCornerApplyResult.DwmCallFailed"/> depending on the
/// DWM response (which is itself driver / session dependent).
/// </summary>
public class DwmInteropTests
{
    [Fact]
    public void Win11_22H2_or_later_is_detected_for_build_22621()
    {
        Assert.True(DwmInterop.IsWindows11_22H2_OrLater(
            majorVersion: 10, minorVersion: 0, buildNumber: 22621));
    }

    [Fact]
    public void Win11_21H2_is_not_detected_as_22H2()
    {
        Assert.False(DwmInterop.IsWindows11_22H2_OrLater(
            majorVersion: 10, minorVersion: 0, buildNumber: 22000));
    }

    [Fact]
    public void Win10_22H2_is_not_detected_as_Win11_22H2()
    {
        Assert.False(DwmInterop.IsWindows11_22H2_OrLater(
            majorVersion: 10, minorVersion: 0, buildNumber: 19045));
    }

    [Fact]
    public void TryApplyRoundedCorners_returns_InvalidHandle_for_Zero()
    {
        // Pure input-validation contract: IntPtr.Zero is rejected
        // before the version check or any DWM call. Independent of
        // host OS.
        var result = new DwmInterop().TryApplyRoundedCorners(IntPtr.Zero);
        Assert.Equal(DwmCornerApplyResult.InvalidHandle, result);
    }

    [Fact]
    public void TryApplySystemBackdrop_returns_false_for_Zero()
    {
        var result = new DwmInterop().TryApplySystemBackdrop(IntPtr.Zero);
        Assert.False(result);
    }

    [Fact]
    public void TryApplyRoundedCorners_returns_one_of_valid_results_on_host()
    {
        // STA-hosted, real HWND. The DWM call's success depends on
        // host OS + driver, so we accept any of the three "valid"
        // outcomes (Applied / NotSupportedOnThisOs / DwmCallFailed).
        // We are pinning the *contract* (never throws, never returns
        // InvalidHandle when given a real HWND, never returns a value
        // outside the documented enum).
        Exception? exception = null;
        DwmCornerApplyResult result = default;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                var window = new Window
                {
                    Width = 100,
                    Height = 100,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    AllowsTransparency = false,
                };
                // SourceInitialized fires when Show() builds the HWND;
                // pump the dispatcher once so the SourceInitialized
                // callback completes before we read the handle.
                window.Show();
                PumpDispatcher();
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                result = new DwmInterop().TryApplyRoundedCorners(hwnd);
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

        Assert.NotEqual(DwmCornerApplyResult.InvalidHandle, result);
        Assert.True(
            result == DwmCornerApplyResult.Applied ||
            result == DwmCornerApplyResult.NotSupportedOnThisOs ||
            result == DwmCornerApplyResult.DwmCallFailed,
            $"unexpected result {result}");
    }

    private static void EnsureApplication()
    {
        // Match MainWindowShowPopoverTests' bootstrap pattern. The
        // test window here does not reference any App.xaml-defined
        // resources, so a barebones Application would suffice — BUT
        // MainWindowShowPopoverTests and MainWindowDwmCornerTests
        // both check `Current is not App` before installing the
        // production App, and a barebones Application we install
        // first would poison the singleton for them (Trap 2:
        // "Cannot create more than one System.Windows.Application").
        // So we let the production App own the singleton here too,
        // even though our window does not use its brushes.
        if (System.Windows.Application.ResourceAssembly == null)
        {
            System.Windows.Application.ResourceAssembly =
                typeof(App).Assembly;
        }
        if (System.Windows.Application.Current is not App)
        {
            var app = new App();
            app.InitializeComponent();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        var until = DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                while (frame.Continue && DateTime.UtcNow < until)
                {
                    // Yield to other queued callbacks once, then break.
                }
                frame.Continue = false;
            }));
        Dispatcher.PushFrame(frame);
    }
}
