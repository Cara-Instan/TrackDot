using System;
using Microsoft.Win32;

namespace TrackDot.Services;

/// <summary>
/// Production implementation of <see cref="IWindowSettingsService"/>.
/// Persists PinToTop and OpacityPercent under <c>HKCU\Software\TrackDot</c>.
/// </summary>
public sealed class WindowSettingsService : IWindowSettingsService
{
    private const string TrackDotKeyPath = @"Software\TrackDot";
    private const string PinToTopValueName = "PinToTop";
    private const string OpacityValueName = "OpacityPercent";
    private const string GlobalHotkeysValueName = "EnableGlobalHotkeys";

    private bool _isPinned;
    private int _opacityPercent;
    private bool _enableGlobalHotkeys;

    /// <inheritdoc/>
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned == value) return;
            _isPinned = value;
            SavePinToTop(value);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public int OpacityPercent
    {
        get => _opacityPercent;
        set
        {
            var clamped = Math.Clamp(value, 20, 100);
            if (_opacityPercent == clamped) return;
            _opacityPercent = clamped;
            SaveOpacityPercent(clamped);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public double WindowOpacity
    {
        get => _opacityPercent / 100.0;
        set => OpacityPercent = (int)Math.Round(value * 100.0);
    }

    /// <inheritdoc/>
    public bool EnableGlobalHotkeys
    {
        get => _enableGlobalHotkeys;
        set
        {
            if (_enableGlobalHotkeys == value) return;
            _enableGlobalHotkeys = value;
            SaveEnableGlobalHotkeys(value);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public event EventHandler? SettingsChanged;

    /// <summary>
    /// Constructs the window settings service. Reads initial values
    /// from registry when parameters are omitted.
    /// </summary>
    public WindowSettingsService(
        bool? initialPinned = null,
        int? initialOpacity = null,
        bool? initialGlobalHotkeys = null)
    {
        _isPinned = initialPinned ?? LoadPinToTop();
        _opacityPercent = Math.Clamp(initialOpacity ?? LoadOpacityPercent(), 20, 100);
        _enableGlobalHotkeys = initialGlobalHotkeys ?? LoadEnableGlobalHotkeys();
    }

    private static bool LoadPinToTop()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TrackDotKeyPath);
            if (key?.GetValue(PinToTopValueName) is int val)
                return val != 0;
        }
        catch
        {
            // Non-fatal fallback
        }
        return false;
    }

    private static void SavePinToTop(bool value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(TrackDotKeyPath);
            key?.SetValue(PinToTopValueName, value ? 1 : 0, RegistryValueKind.DWord);
        }
        catch
        {
            // Non-fatal if registry write fails
        }
    }

    private static int LoadOpacityPercent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TrackDotKeyPath);
            if (key?.GetValue(OpacityValueName) is int val)
                return Math.Clamp(val, 20, 100);
        }
        catch
        {
            // Non-fatal fallback
        }
        return 100;
    }

    private static void SaveOpacityPercent(int value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(TrackDotKeyPath);
            key?.SetValue(OpacityValueName, value, RegistryValueKind.DWord);
        }
        catch
        {
            // Non-fatal if registry write fails
        }
    }

    private static bool LoadEnableGlobalHotkeys()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TrackDotKeyPath);
            if (key?.GetValue(GlobalHotkeysValueName) is int val)
                return val != 0;
        }
        catch
        {
            // Non-fatal fallback
        }
        return true; // Default enabled
    }

    private static void SaveEnableGlobalHotkeys(bool value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(TrackDotKeyPath);
            key?.SetValue(GlobalHotkeysValueName, value ? 1 : 0, RegistryValueKind.DWord);
        }
        catch
        {
            // Non-fatal if registry write fails
        }
    }
}
