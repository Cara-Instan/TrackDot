using System;
using System.Windows.Input;
using System.Windows.Interop;
using TrackDot.ViewModels;

namespace TrackDot.Services;

/// <summary>
/// Default implementation of <see cref="IGlobalHotkeyService"/>. Registers system-wide
/// hotkeys (`Ctrl+Alt+Space`, `Ctrl+Alt+Right`, `Ctrl+Alt+Left`, `Ctrl+Alt+S`, `Ctrl+Alt+M`,
/// `Ctrl+Alt+Up`, `Ctrl+Alt+Down`, `Alt+Shift+T`) and hooks them to the main view-model commands.
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int HotkeyIdPlayPause = 9001;
    private const int HotkeyIdNext = 9002;
    private const int HotkeyIdPrevious = 9003;
    private const int HotkeyIdSettings = 9004;
    private const int HotkeyIdMute = 9005;
    private const int HotkeyIdVolumeUp = 9006;
    private const int HotkeyIdVolumeDown = 9007;
    private const int HotkeyIdToggleWindow = 9008;
    private const int HotkeyIdStop = 9009;

    private const uint VK_SPACE = 0x20;
    private const uint VK_LEFT = 0x25;
    private const uint VK_UP = 0x26;
    private const uint VK_RIGHT = 0x27;
    private const uint VK_DOWN = 0x28;
    private const uint VK_S = 0x53;
    private const uint VK_M = 0x4D;
    private const uint VK_T = 0x54;
    private const uint VK_PERIOD = 0xBE;

    private readonly MainViewModel _viewModel;
    private readonly Action? _onToggleWindow;
    private readonly Action? _onOpenSettings;
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _isRegistered;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsRegistered => _isRegistered;

    public GlobalHotkeyService(
        MainViewModel viewModel,
        Action? onToggleWindow = null,
        Action? onOpenSettings = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        _onToggleWindow = onToggleWindow;
        _onOpenSettings = onOpenSettings;
    }

    /// <inheritdoc/>
    public void Register(IntPtr windowHandle)
    {
        if (_disposed || _isRegistered) return;
        if (windowHandle == IntPtr.Zero) return;

        _hwnd = windowHandle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(HwndHook);

        uint ctrlAlt = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;
        uint altShift = NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;

        // Register hotkeys — failure on individual keys (e.g. key used by OS) is non-fatal.
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdPlayPause, ctrlAlt, VK_SPACE);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdNext, ctrlAlt, VK_RIGHT);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdPrevious, ctrlAlt, VK_LEFT);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdStop, ctrlAlt, VK_PERIOD);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdSettings, ctrlAlt, VK_S);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdMute, ctrlAlt, VK_M);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdVolumeUp, ctrlAlt, VK_UP);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdVolumeDown, ctrlAlt, VK_DOWN);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdToggleWindow, altShift, VK_T);

        _isRegistered = true;
    }

    /// <inheritdoc/>
    public void Unregister()
    {
        if (!_isRegistered || _hwnd == IntPtr.Zero) return;

        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdPlayPause);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdNext);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdPrevious);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdStop);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdSettings);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdMute);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdVolumeUp);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdVolumeDown);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdToggleWindow);

        _hwndSource?.RemoveHook(HwndHook);
        _hwndSource = null;
        _hwnd = IntPtr.Zero;
        _isRegistered = false;
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            switch (id)
            {
                case HotkeyIdPlayPause:
                    if (_viewModel.TogglePlayPauseCommand.CanExecute(null))
                        _viewModel.TogglePlayPauseCommand.Execute(null);
                    handled = true;
                    break;

                case HotkeyIdNext:
                    if (_viewModel.NextCommand.CanExecute(null))
                        _viewModel.NextCommand.Execute(null);
                    handled = true;
                    break;

                case HotkeyIdPrevious:
                    if (_viewModel.PreviousCommand.CanExecute(null))
                        _viewModel.PreviousCommand.Execute(null);
                    handled = true;
                    break;

                case HotkeyIdStop:
                    if (_viewModel.StopCommand.CanExecute(null))
                        _viewModel.StopCommand.Execute(null);
                    handled = true;
                    break;

                case HotkeyIdSettings:
                    _onOpenSettings?.Invoke();
                    handled = true;
                    break;

                case HotkeyIdMute:
                    if (_viewModel.ToggleMuteCommand.CanExecute(null))
                        _viewModel.ToggleMuteCommand.Execute(null);
                    handled = true;
                    break;

                case HotkeyIdVolumeUp:
                    AdjustVolume(5.0);
                    handled = true;
                    break;

                case HotkeyIdVolumeDown:
                    AdjustVolume(-5.0);
                    handled = true;
                    break;

                case HotkeyIdToggleWindow:
                    _onToggleWindow?.Invoke();
                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    private void AdjustVolume(double deltaPercent)
    {
        double current = _viewModel.VolumePercent;
        double newVol = Math.Clamp(current + deltaPercent, 0.0, 100.0);
        if (_viewModel.SetVolumeCommand.CanExecute(newVol))
        {
            _viewModel.SetVolumeCommand.Execute(newVol);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }
}
