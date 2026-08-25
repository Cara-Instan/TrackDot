using System.Windows.Input;
using TrackDot.Models;
using TrackDot.Services;
using TrackDot.ViewModels;
using Xunit;

namespace TrackDot.Tests;

[Collection("PortableMode")]
public class HotkeysViewModelTests
{
    [Fact]
    public void Gestures_ReflectDefaultBindingsWhenUnset()
    {
        using var sut = new HotkeysViewModel();

        Assert.Equal("Alt+Shift+T", sut.ToggleWindowGesture);
        Assert.Equal("Ctrl+Alt+Space", sut.PlayPauseGesture);
        Assert.Equal("Ctrl+Alt+Right", sut.NextTrackGesture);
        Assert.Equal("Ctrl+Alt+Left", sut.PreviousTrackGesture);
    }

    [Fact]
    public void Gestures_UpdateWhenWindowSettingsChanged()
    {
        var windowSettings = new WindowSettingsService(initialHotkeys: HotkeyBinding.GetDefaults());
        using var sut = new HotkeysViewModel(windowSettings);

        Assert.Equal("Ctrl+Alt+Space", sut.PlayPauseGesture);

        // Rebind PlayPause to Ctrl+Shift+P
        windowSettings.SetHotkeyBinding(HotkeyAction.PlayPause, ModifierKeys.Control | ModifierKeys.Shift, Key.P);

        Assert.Equal("Ctrl+Shift+P", sut.PlayPauseGesture);
    }
}

