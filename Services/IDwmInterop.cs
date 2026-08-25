using System;

namespace TrackDot.Services;

/// <summary>
/// Result of attempting to apply DWM rounded corners to a window.
/// Distinguishes "applied" from each failure mode so callers can
/// decide whether to drop the layered-alpha fallback path.
/// </summary>
public enum DwmCornerApplyResult
{
    /// <summary>The DWM call succeeded; the corner preference is now active.</summary>
    Applied,
    /// <summary>The host OS does not support <c>DWMWA_WINDOW_CORNER_PREFERENCE</c>.</summary>
    NotSupportedOnThisOs,
    /// <summary>The caller passed <see cref="IntPtr.Zero"/>.</summary>
    InvalidHandle,
    /// <summary>The DWM call returned a non-zero HRESULT.</summary>
    DwmCallFailed
}

/// <summary>
/// Applies the OS-level DWM rounded-corner preference to a window HWND
/// when supported (Windows 11 22H2+). On older builds the call is a
/// no-op and callers should keep their layered-alpha fallback path.
/// </summary>
public interface IDwmInterop
{
    /// <summary>
    /// Attempts to set <c>DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND</c>
    /// on the given window. Safe to call before the window is shown;
    /// DWM attributes apply on the next composition pass.
    /// </summary>
    DwmCornerApplyResult TryApplyRoundedCorners(IntPtr hwnd);

    /// <summary>
    /// Attempts to apply a system backdrop (such as Acrylic = 3 or Mica = 2) on Windows 11 22H2+ (build 22621+).
    /// </summary>
    bool TryApplySystemBackdrop(IntPtr hwnd, int backdropType = 3);

    /// <summary>
    /// Returns whether the host OS is Windows 11 22H2 (build 22621) or later,
    /// detected via <c>RtlGetVersion</c> rather than the shimmed
    /// <see cref="Environment.OSVersion"/>.
    /// </summary>
    bool IsWindows11_22H2_OrLater();
}
