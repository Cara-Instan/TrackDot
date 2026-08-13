using System;
using System.Runtime.InteropServices;

namespace TrackDot;

/// <summary>
/// Internal P/Invoke declarations for Windows native APIs used by TrackDot.
/// </summary>
internal static class NativeMethods
{
    /// <summary>DWM margin structure. Pass <c>-1</c> for all four fields to extend the frame into the entire client area.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    /// <summary>
    /// Extends the DWM glass frame into the client area of a window.
    /// Passing <see cref="MARGINS"/> with all fields set to <c>-1</c>
    /// makes the entire client area glassy / transparent via hardware
    /// composition, replacing the software-rendering path used by
    /// <c>AllowsTransparency</c>.
    /// </summary>
    [DllImport("dwmapi.dll", PreserveSig = true)]
    internal static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    internal const int WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>
    /// Sets the value of a Desktop Window Manager (DWM) non-client attribute
    /// for a window. Used here for <c>DWMWA_WINDOW_CORNER_PREFERENCE</c> to
    /// request rounded OS-frame corners on Windows 11 22H2+.
    /// </summary>
    [DllImport("dwmapi.dll", PreserveSig = true)]
    internal static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int pvAttribute,
        int cbAttribute);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct OSVERSIONINFOEXW
    {
        public int dwOSVersionInfoSize;
        public int dwMajorVersion;
        public int dwMinorVersion;
        public int dwBuildNumber;
        public int dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;
        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public ushort wSuiteMask;
        public byte wProductType;
        public byte wReserved;
    }

    [DllImport("ntdll.dll", PreserveSig = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RtlGetVersion(ref OSVERSIONINFOEXW versionInfo);
}
