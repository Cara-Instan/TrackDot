using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;
using TrackDot.Models;
using TrackDot.ViewModels;

namespace TrackDot.Services;

/// <summary>
/// Default implementation of <see cref="IGlobalHotkeyService"/>. Registers system-wide
/// hotkeys dynamically and hooks them to the main view-model commands.
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int BaseHotkeyId = 9000;

    private readonly MainViewModel _viewModel;
    private readonly IWindowSettingsService? _windowSettings;
    private readonly Action? _onToggleWindow;
    private readonly Action? _onOpenSettings;
    private readonly HashSet<int> _registeredIds = new();
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _isRegistered;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsRegistered => _isRegistered;

    public GlobalHotkeyService(
        MainViewModel viewModel,
        IWindowSettingsService? windowSettings = null,
        Action? onToggleWindow = null,
        Action? onOpenSettings = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        _windowSettings = windowSettings;
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

        RegisterConfiguredHotkeys();

        _isRegistered = true;
    }

    /// <inheritdoc/>
    public void Reregister()
    {
        if (_disposed || _hwnd == IntPtr.Zero) return;
        UnregisterHotkeysOnly();
        RegisterConfiguredHotkeys();
    }

    private void RegisterConfiguredHotkeys()
    {
        if (_hwnd == IntPtr.Zero) return;

        var actions = (HotkeyAction[])Enum.GetValues(typeof(HotkeyAction));
        foreach (var action in actions)
        {
            var binding = _windowSettings?.GetHotkeyBinding(action)
                          ?? HotkeyBinding.GetDefaults().FirstOrDefault(d => d.Action == action);

            if (binding == null || binding.Key == Key.None) continue;

            uint win32Modifiers = ToWin32Modifiers(binding.Modifiers);
            int vk = KeyInterop.VirtualKeyFromKey(binding.Key);
            if (vk <= 0) continue;

            int hotkeyId = BaseHotkeyId + (int)action;
            if (NativeMethods.RegisterHotKey(_hwnd, hotkeyId, win32Modifiers, (uint)vk))
            {
                _registeredIds.Add(hotkeyId);
            }
        }
    }

    private static uint ToWin32Modifiers(ModifierKeys modifiers)
    {
        uint flags = NativeMethods.MOD_NOREPEAT;
        if (modifiers.HasFlag(ModifierKeys.Control)) flags |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Alt)) flags |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Shift)) flags |= NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) flags |= NativeMethods.MOD_WIN;
        return flags;
    }

    private void UnregisterHotkeysOnly()
    {
        if (_hwnd == IntPtr.Zero) return;
        foreach (var id in _registeredIds)
        {
            NativeMethods.UnregisterHotKey(_hwnd, id);
        }
        _registeredIds.Clear();
    }

    /// <inheritdoc/>
    public void Unregister()
    {
        if (!_isRegistered || _hwnd == IntPtr.Zero) return;

        UnregisterHotkeysOnly();

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
            int actionOffset = id - BaseHotkeyId;
            if (Enum.IsDefined(typeof(HotkeyAction), actionOffset))
            {
                var action = (HotkeyAction)actionOffset;
                ExecuteAction(action);
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void ExecuteAction(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.PlayPause:
                if (_viewModel.TogglePlayPauseCommand.CanExecute(null))
                    _viewModel.TogglePlayPauseCommand.Execute(null);
                break;

            case HotkeyAction.NextTrack:
                if (_viewModel.NextCommand.CanExecute(null))
                    _viewModel.NextCommand.Execute(null);
                break;

            case HotkeyAction.PreviousTrack:
                if (_viewModel.PreviousCommand.CanExecute(null))
                    _viewModel.PreviousCommand.Execute(null);
                break;

            case HotkeyAction.StopTrack:
                if (_viewModel.StopCommand.CanExecute(null))
                    _viewModel.StopCommand.Execute(null);
                break;

            case HotkeyAction.OpenSettings:
                _onOpenSettings?.Invoke();
                break;

            case HotkeyAction.ToggleMute:
                if (_viewModel.ToggleMuteCommand.CanExecute(null))
                    _viewModel.ToggleMuteCommand.Execute(null);
                break;

            case HotkeyAction.VolumeUp:
                AdjustVolume(5.0);
                break;

            case HotkeyAction.VolumeDown:
                AdjustVolume(-5.0);
                break;

            case HotkeyAction.ToggleWindow:
                _onToggleWindow?.Invoke();
                break;
        }
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
