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

    private bool _lyricsWindowVisible;
    private int _lyricsOpacityPercent;
    private bool _lyricsIsTopmost;
    private bool _lyricsIsFuriganaVisible;
    private double _lyricsWindowLeft;
    private double _lyricsWindowTop;
    private double _lyricsWindowWidth;
    private double _lyricsWindowHeight;

    /// <inheritdoc/>
    public bool LyricsWindowVisible
    {
        get => _lyricsWindowVisible;
        set
        {
            if (_lyricsWindowVisible == value) return;
            _lyricsWindowVisible = value;
            SaveValue("LyricsWindowVisible", value ? 1 : 0, d => d.LyricsWindowVisible = value);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public int LyricsOpacityPercent
    {
        get => _lyricsOpacityPercent;
        set
        {
            var clamped = Math.Clamp(value, 20, 100);
            if (_lyricsOpacityPercent == clamped) return;
            _lyricsOpacityPercent = clamped;
            SaveValue("LyricsOpacityPercent", clamped, d => d.LyricsOpacityPercent = clamped);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public bool LyricsIsTopmost
    {
        get => _lyricsIsTopmost;
        set
        {
            if (_lyricsIsTopmost == value) return;
            _lyricsIsTopmost = value;
            SaveValue("LyricsIsTopmost", value ? 1 : 0, d => d.LyricsIsTopmost = value);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public bool LyricsIsFuriganaVisible
    {
        get => _lyricsIsFuriganaVisible;
        set
        {
            if (_lyricsIsFuriganaVisible == value) return;
            _lyricsIsFuriganaVisible = value;
            SaveValue("LyricsIsFuriganaVisible", value ? 1 : 0, d => d.LyricsIsFuriganaVisible = value);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public double LyricsWindowLeft
    {
        get => _lyricsWindowLeft;
        set
        {
            if (Math.Abs(_lyricsWindowLeft - value) < 0.1) return;
            _lyricsWindowLeft = value;
            SaveStringValue("LyricsWindowLeft", value.ToString(System.Globalization.CultureInfo.InvariantCulture), d => d.LyricsWindowLeft = value);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public double LyricsWindowTop
    {
        get => _lyricsWindowTop;
        set
        {
            if (Math.Abs(_lyricsWindowTop - value) < 0.1) return;
            _lyricsWindowTop = value;
            SaveStringValue("LyricsWindowTop", value.ToString(System.Globalization.CultureInfo.InvariantCulture), d => d.LyricsWindowTop = value);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public double LyricsWindowWidth
    {
        get => _lyricsWindowWidth;
        set
        {
            var clamped = Math.Max(value, 200);
            if (Math.Abs(_lyricsWindowWidth - clamped) < 0.1) return;
            _lyricsWindowWidth = clamped;
            SaveStringValue("LyricsWindowWidth", clamped.ToString(System.Globalization.CultureInfo.InvariantCulture), d => d.LyricsWindowWidth = clamped);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public double LyricsWindowHeight
    {
        get => _lyricsWindowHeight;
        set
        {
            var clamped = Math.Max(value, 200);
            if (Math.Abs(_lyricsWindowHeight - clamped) < 0.1) return;
            _lyricsWindowHeight = clamped;
            SaveStringValue("LyricsWindowHeight", clamped.ToString(System.Globalization.CultureInfo.InvariantCulture), d => d.LyricsWindowHeight = clamped);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Constructs the window settings service. Reads initial values
    /// from registry when parameters are omitted.
    /// </summary>
    public WindowSettingsService(
        bool? initialPinned = null,
        int? initialOpacity = null,
        bool? initialGlobalHotkeys = null,
        int? initialLyricsOpacity = null)
    {
        _isPinned = initialPinned ?? LoadPinToTop();
        _opacityPercent = Math.Clamp(initialOpacity ?? LoadOpacityPercent(), 20, 100);
        _enableGlobalHotkeys = initialGlobalHotkeys ?? LoadEnableGlobalHotkeys();

        _lyricsWindowVisible = LoadBoolValue("LyricsWindowVisible", false, d => d.LyricsWindowVisible);
        _lyricsOpacityPercent = Math.Clamp(initialLyricsOpacity ?? LoadIntValue("LyricsOpacityPercent", 85, d => d.LyricsOpacityPercent), 20, 100);
        _lyricsIsTopmost = LoadBoolValue("LyricsIsTopmost", true, d => d.LyricsIsTopmost);
        _lyricsIsFuriganaVisible = LoadBoolValue("LyricsIsFuriganaVisible", true, d => d.LyricsIsFuriganaVisible);
        _lyricsWindowLeft = LoadDoubleValue("LyricsWindowLeft", -1.0, d => d.LyricsWindowLeft);
        _lyricsWindowTop = LoadDoubleValue("LyricsWindowTop", -1.0, d => d.LyricsWindowTop);
        _lyricsWindowWidth = LoadDoubleValue("LyricsWindowWidth", 420.0, d => d.LyricsWindowWidth);
        _lyricsWindowHeight = LoadDoubleValue("LyricsWindowHeight", 580.0, d => d.LyricsWindowHeight);
    }

    public class PortableSettingsData
    {
        public bool PinToTop { get; set; }
        public int OpacityPercent { get; set; } = 100;
        public bool EnableGlobalHotkeys { get; set; } = true;
        public bool LyricsWindowVisible { get; set; }
        public int LyricsOpacityPercent { get; set; } = 85;
        public bool LyricsIsTopmost { get; set; } = true;
        public bool LyricsIsFuriganaVisible { get; set; } = true;
        public double LyricsWindowLeft { get; set; } = -1.0;
        public double LyricsWindowTop { get; set; } = -1.0;
        public double LyricsWindowWidth { get; set; } = 420.0;
        public double LyricsWindowHeight { get; set; } = 580.0;
    }

    private static string PortableSettingsPath => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
    private static readonly object PortableLock = new();

    private static PortableSettingsData LoadPortableSettings()
    {
        lock (PortableLock)
        {
            try
            {
                var path = PortableSettingsPath;
                if (System.IO.File.Exists(path))
                {
                    var json = System.IO.File.ReadAllText(path);
                    var data = System.Text.Json.JsonSerializer.Deserialize<PortableSettingsData>(json);
                    if (data != null) return data;
                }
            }
            catch { }
            return new PortableSettingsData();
        }
    }

    private static void SavePortableSettings(Action<PortableSettingsData> updateAction)
    {
        lock (PortableLock)
        {
            try
            {
                var data = LoadPortableSettings();
                updateAction(data);
                var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(PortableSettingsPath, json);
            }
            catch { }
        }
    }

    private static bool LoadBoolValue(string name, bool defaultValue, Func<PortableSettingsData, bool> portableSelector)
    {
        if (PortableMode.IsPortable) return portableSelector(LoadPortableSettings());
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TrackDotKeyPath);
            if (key?.GetValue(name) is int val) return val != 0;
        }
        catch { }
        return defaultValue;
    }

    private static int LoadIntValue(string name, int defaultValue, Func<PortableSettingsData, int> portableSelector)
    {
        if (PortableMode.IsPortable) return portableSelector(LoadPortableSettings());
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TrackDotKeyPath);
            if (key?.GetValue(name) is int val) return val;
        }
        catch { }
        return defaultValue;
    }

    private static double LoadDoubleValue(string name, double defaultValue, Func<PortableSettingsData, double> portableSelector)
    {
        if (PortableMode.IsPortable) return portableSelector(LoadPortableSettings());
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TrackDotKeyPath);
            if (key?.GetValue(name) is string valStr && double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dVal))
                return dVal;
        }
        catch { }
        return defaultValue;
    }

    private static void SaveValue(string name, int value, Action<PortableSettingsData> portableAction)
    {
        if (PortableMode.IsPortable)
        {
            SavePortableSettings(portableAction);
            return;
        }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(TrackDotKeyPath);
            key?.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static void SaveStringValue(string name, string value, Action<PortableSettingsData> portableAction)
    {
        if (PortableMode.IsPortable)
        {
            SavePortableSettings(portableAction);
            return;
        }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(TrackDotKeyPath);
            key?.SetValue(name, value, RegistryValueKind.String);
        }
        catch { }
    }

    private static bool LoadPinToTop() => LoadBoolValue(PinToTopValueName, false, d => d.PinToTop);
    private static void SavePinToTop(bool value) => SaveValue(PinToTopValueName, value ? 1 : 0, d => d.PinToTop = value);

    private static int LoadOpacityPercent() => LoadIntValue(OpacityValueName, 100, d => d.OpacityPercent);
    private static void SaveOpacityPercent(int value) => SaveValue(OpacityValueName, value, d => d.OpacityPercent = value);

    private static bool LoadEnableGlobalHotkeys() => LoadBoolValue(GlobalHotkeysValueName, true, d => d.EnableGlobalHotkeys);
    private static void SaveEnableGlobalHotkeys(bool value) => SaveValue(GlobalHotkeysValueName, value ? 1 : 0, d => d.EnableGlobalHotkeys = value);
}
