using System;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

[Collection("PortableMode")]
public class WindowSettingsServiceTests
{
    [Fact]
    public void DefaultConstructor_SetsExpectedDefaults()
    {
        var sut = new WindowSettingsService(initialPinned: false, initialOpacity: 100);

        Assert.False(sut.IsPinned);
        Assert.Equal(100, sut.OpacityPercent);
        Assert.Equal(1.0, sut.WindowOpacity, precision: 2);
    }

    [Fact]
    public void IsPinned_TogglesAndFiresSettingsChanged()
    {
        var sut = new WindowSettingsService(initialPinned: false, initialOpacity: 100);
        bool fired = false;
        sut.SettingsChanged += (s, e) => fired = true;

        sut.IsPinned = true;

        Assert.True(sut.IsPinned);
        Assert.True(fired);
    }

    [Fact]
    public void OpacityPercent_ClampsValueBetween20And100()
    {
        var sut = new WindowSettingsService(initialPinned: false, initialOpacity: 100);

        sut.OpacityPercent = 10;
        Assert.Equal(20, sut.OpacityPercent);
        Assert.Equal(0.20, sut.WindowOpacity, precision: 2);

        sut.OpacityPercent = 150;
        Assert.Equal(100, sut.OpacityPercent);
        Assert.Equal(1.0, sut.WindowOpacity, precision: 2);
    }

    [Fact]
    public void WindowOpacity_UpdatesOpacityPercent()
    {
        var sut = new WindowSettingsService(initialPinned: false, initialOpacity: 100);

        sut.WindowOpacity = 0.85;

        Assert.Equal(85, sut.OpacityPercent);
        Assert.Equal(0.85, sut.WindowOpacity, precision: 2);
    }

    [Fact]
    public void EnableGlobalHotkeys_TogglesAndFiresSettingsChanged()
    {
        var sut = new WindowSettingsService(initialPinned: false, initialOpacity: 100, initialGlobalHotkeys: true);
        bool fired = false;
        sut.SettingsChanged += (s, e) => fired = true;

        Assert.True(sut.EnableGlobalHotkeys);
        sut.EnableGlobalHotkeys = false;

        Assert.False(sut.EnableGlobalHotkeys);
        Assert.True(fired);
    }

    [Fact]
    public void PortableMode_SaveAndLoadSettings_PersistsInPortableMode()
    {
        var settingsFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        try
        {
            PortableMode.IsPortable = true;
            if (System.IO.File.Exists(settingsFile))
            {
                System.IO.File.Delete(settingsFile);
            }

            var sut = new WindowSettingsService(initialPinned: false, initialOpacity: 100, initialGlobalHotkeys: true);
            sut.IsPinned = true;
            sut.OpacityPercent = 80;
            sut.EnableGlobalHotkeys = false;

            Assert.True(System.IO.File.Exists(settingsFile));

            // Create new instance without initial values to test loading from JSON file
            var sutLoaded = new WindowSettingsService();
            Assert.True(sutLoaded.IsPinned);
            Assert.Equal(80, sutLoaded.OpacityPercent);
            Assert.False(sutLoaded.EnableGlobalHotkeys);
        }
        finally
        {
            if (System.IO.File.Exists(settingsFile))
            {
                System.IO.File.Delete(settingsFile);
            }
            PortableMode.IsPortable = false;
        }
    }

    [Fact]
    public void EnableDynamicTinting_TogglesAndFiresSettingsChanged()
    {
        var sut = new WindowSettingsService(initialDynamicTinting: true);
        bool fired = false;
        sut.SettingsChanged += (s, e) => fired = true;

        Assert.True(sut.EnableDynamicTinting);
        sut.EnableDynamicTinting = false;

        Assert.False(sut.EnableDynamicTinting);
        Assert.True(fired);
    }

    [Fact]
    public void HotkeyBindings_SetAndReset_FiresSettingsChanged()
    {
        var sut = new WindowSettingsService(initialHotkeys: TrackDot.Models.HotkeyBinding.GetDefaults());
        bool fired = false;
        sut.SettingsChanged += (s, e) => fired = true;

        var initialBinding = sut.GetHotkeyBinding(TrackDot.Models.HotkeyAction.PlayPause);
        Assert.Equal("Ctrl+Alt+Space", initialBinding.GestureText);

        sut.SetHotkeyBinding(TrackDot.Models.HotkeyAction.PlayPause, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, System.Windows.Input.Key.P);

        Assert.True(fired);
        Assert.Equal("Ctrl+Shift+P", sut.GetHotkeyBinding(TrackDot.Models.HotkeyAction.PlayPause).GestureText);

        sut.ResetHotkeyBindingsToDefault();
        Assert.Equal("Ctrl+Alt+Space", sut.GetHotkeyBinding(TrackDot.Models.HotkeyAction.PlayPause).GestureText);
    }
}
