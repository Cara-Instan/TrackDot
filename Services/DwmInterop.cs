using System;
using System.Runtime.InteropServices;

namespace TrackDot.Services;

/// <summary>
/// Default <see cref="IDwmInterop"/> implementation. Uses
/// <see cref="NativeMethods.DwmSetWindowAttribute"/> with the
/// <c>DWMWA_WINDOW_CORNER_PREFERENCE = 33</c> attribute and value
/// <c>DWMWCP_ROUND = 2</c>. Detects OS support via
/// <see cref="NativeMethods.RtlGetVersion"/> rather than
/// <see cref="Environment.OSVersion"/> (which is shimmed on Win10).
/// </summary>
public sealed class DwmInterop : IDwmInterop
{
    // DWMWA_WINDOW_CORNER_PREFERENCE = 33 (Win11 22H2+)
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    // DWMWCP_ROUND = 2: ask DWM to round the OS frame.
    private const int DWMWCP_ROUND = 2;
    // DWMWCP_DONOTROUND = 1 (documented for the next reader; not used here).
    private const int DWMWCP_DONOTROUND = 1;

    public bool IsWindows11_22H2_OrLater() => IsWindows11_22H2_OrLaterInternal();

    public DwmCornerApplyResult TryApplyRoundedCorners(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return DwmCornerApplyResult.InvalidHandle;
        if (!IsWindows11_22H2_OrLaterInternal()) return DwmCornerApplyResult.NotSupportedOnThisOs;

        int preference = DWMWCP_ROUND;
        int hr = NativeMethods.DwmSetWindowAttribute(
            hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

        // DwmSetWindowAttribute returns an HRESULT. S_OK = 0.
        // E_INVALIDARG / E_ACCESSDENIED on unsupported attributes is
        // also possible; treat any non-zero as "failed but not fatal".
        return hr == 0
            ? DwmCornerApplyResult.Applied
            : DwmCornerApplyResult.DwmCallFailed;
    }

    // ----- version detector (test-friendly seam) -----

    /// <summary>
    /// Pure-function overload of <see cref="IsWindows11_22H2_OrLater()"/>:
    /// accepts the raw version triple so unit tests can pin both sides
    /// of the 22621 build boundary without touching the host OS.
    /// Windows 11 reports as major=10, minor=0, build>=22000 (yes, Win11
    /// still says major 10 — this is a deliberate Microsoft compat choice).
    /// </summary>
    internal static bool IsWindows11_22H2_OrLater(
        int majorVersion, int minorVersion, int buildNumber)
    {
        if (majorVersion != 10) return false;
        if (minorVersion != 0) return false;
        return buildNumber >= 22621;
    }

    private static bool IsWindows11_22H2_OrLaterInternal()
    {
        var v = new NativeMethods.OSVERSIONINFOEXW
        {
            dwOSVersionInfoSize = Marshal.SizeOf<NativeMethods.OSVERSIONINFOEXW>()
        };
        if (!NativeMethods.RtlGetVersion(ref v)) return false;
        return IsWindows11_22H2_OrLater(v.dwMajorVersion, v.dwMinorVersion, v.dwBuildNumber);
    }
}
