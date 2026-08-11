using System;

namespace TrackDot.Services;

/// <summary>
/// Service managing system-wide (global) keyboard hotkeys for media controls.
/// Uses Win32 RegisterHotKey API via an HwndSource message hook.
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>
    /// Starts listening for global hotkeys using the specified window handle.
    /// </summary>
    void Register(IntPtr windowHandle);

    /// <summary>
    /// Stops listening and unregisters all global hotkeys.
    /// </summary>
    void Unregister();

    /// <summary>
    /// Whether global hotkeys are currently registered and active.
    /// </summary>
    bool IsRegistered { get; }
}
