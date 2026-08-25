using System.Windows.Input;
using TrackDot.Models;
using Xunit;

namespace TrackDot.Tests;

public class HotkeyBindingTests
{
    [Fact]
    public void FormatGesture_FormatsCorrectly()
    {
        var text = HotkeyBinding.FormatGesture(ModifierKeys.Control | ModifierKeys.Alt, Key.Space);
        Assert.Equal("Ctrl+Alt+Space", text);

        var text2 = HotkeyBinding.FormatGesture(ModifierKeys.Alt | ModifierKeys.Shift, Key.T);
        Assert.Equal("Alt+Shift+T", text2);

        var text3 = HotkeyBinding.FormatGesture(ModifierKeys.Windows | ModifierKeys.Control, Key.Right);
        Assert.Equal("Ctrl+Win+Right", text3);

        var textNone = HotkeyBinding.FormatGesture(ModifierKeys.None, Key.None);
        Assert.Equal("None", textNone);
    }

    [Fact]
    public void Serialize_And_Deserialize_PreservesBinding()
    {
        var binding = new HotkeyBinding(HotkeyAction.PlayPause, ModifierKeys.Control | ModifierKeys.Alt, Key.Space);
        var serialized = binding.Serialize();

        var restored = HotkeyBinding.Deserialize(HotkeyAction.PlayPause, serialized);
        Assert.NotNull(restored);
        Assert.Equal(binding.Action, restored.Action);
        Assert.Equal(binding.Modifiers, restored.Modifiers);
        Assert.Equal(binding.Key, restored.Key);
        Assert.Equal(binding.GestureText, restored.GestureText);
    }

    [Fact]
    public void Deserialize_InvalidOrEmpty_ReturnsNull()
    {
        Assert.Null(HotkeyBinding.Deserialize(HotkeyAction.PlayPause, null));
        Assert.Null(HotkeyBinding.Deserialize(HotkeyAction.PlayPause, ""));
        Assert.Null(HotkeyBinding.Deserialize(HotkeyAction.PlayPause, "invalid"));
    }

    [Fact]
    public void GetDefaults_ContainsAllActions()
    {
        var defaults = HotkeyBinding.GetDefaults();
        Assert.NotEmpty(defaults);
        Assert.Contains(defaults, d => d.Action == HotkeyAction.PlayPause);
        Assert.Contains(defaults, d => d.Action == HotkeyAction.ToggleWindow);
        Assert.Contains(defaults, d => d.Action == HotkeyAction.NextTrack);
        Assert.Contains(defaults, d => d.Action == HotkeyAction.PreviousTrack);
        Assert.Contains(defaults, d => d.Action == HotkeyAction.StopTrack);
        Assert.Contains(defaults, d => d.Action == HotkeyAction.ToggleMute);
        Assert.Contains(defaults, d => d.Action == HotkeyAction.VolumeUp);
        Assert.Contains(defaults, d => d.Action == HotkeyAction.VolumeDown);
        Assert.Contains(defaults, d => d.Action == HotkeyAction.ToggleLyrics);
        Assert.Contains(defaults, d => d.Action == HotkeyAction.ToggleLyricsHud);
    }
}

