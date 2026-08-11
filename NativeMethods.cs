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
}
