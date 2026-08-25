using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace TrackDot.Models;

/// <summary>
/// Represents a configurable shortcut mapping an action to a combination of modifiers and key.
/// </summary>
public sealed record HotkeyBinding(
    HotkeyAction Action,
    ModifierKeys Modifiers,
    Key Key)
{
    /// <summary>
    /// User-friendly label for the action.
    /// </summary>
    public string DisplayName => Action switch
    {
        HotkeyAction.ToggleWindow => "Toggle Popover",
        HotkeyAction.PlayPause => "Play / Pause",
        HotkeyAction.NextTrack => "Next Track",
        HotkeyAction.PreviousTrack => "Previous Track",
        HotkeyAction.StopTrack => "Stop Track",
        HotkeyAction.ToggleMute => "Toggle Mute",
        HotkeyAction.VolumeUp => "Volume Up",
        HotkeyAction.VolumeDown => "Volume Down",
        HotkeyAction.OpenSettings => "Open Settings",
        HotkeyAction.ToggleLyrics => "Toggle Lyrics",
        HotkeyAction.ToggleLyricsHud => "Toggle Floating HUD",
        _ => Action.ToString()
    };

    /// <summary>
    /// Formatted keyboard gesture string (e.g. "Ctrl+Alt+Space").
    /// </summary>
    public string GestureText => FormatGesture(Modifiers, Key);

    public static string FormatGesture(ModifierKeys modifiers, Key key)
    {
        if (key == Key.None) return "None";

        var sb = new StringBuilder();
        if (modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        if (modifiers.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");

        string keyStr = key switch
        {
            Key.OemPeriod => ".",
            Key.OemComma => ",",
            Key.OemQuestion => "/",
            Key.Space => "Space",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            _ => key.ToString()
        };

        sb.Append(keyStr);
        return sb.ToString();
    }

    /// <summary>
    /// Serializes the binding into a compact string representation, e.g. "Control,Alt:Space".
    /// </summary>
    public string Serialize()
    {
        return $"{(int)Modifiers}:{(int)Key}";
    }

    /// <summary>
    /// Deserializes a string representation into a HotkeyBinding.
    /// </summary>
    public static HotkeyBinding? Deserialize(HotkeyAction action, string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return null;

        var parts = str.Split(':');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out int modInt) &&
            int.TryParse(parts[1], out int keyInt))
        {
            return new HotkeyBinding(action, (ModifierKeys)modInt, (Key)keyInt);
        }

        return null;
    }

    /// <summary>
    /// Returns default hotkey bindings.
    /// </summary>
    public static IReadOnlyList<HotkeyBinding> GetDefaults() => new List<HotkeyBinding>
    {
        new(HotkeyAction.ToggleWindow, ModifierKeys.Alt | ModifierKeys.Shift, Key.T),
        new(HotkeyAction.PlayPause, ModifierKeys.Control | ModifierKeys.Alt, Key.Space),
        new(HotkeyAction.NextTrack, ModifierKeys.Control | ModifierKeys.Alt, Key.Right),
        new(HotkeyAction.PreviousTrack, ModifierKeys.Control | ModifierKeys.Alt, Key.Left),
        new(HotkeyAction.StopTrack, ModifierKeys.Control | ModifierKeys.Alt, Key.OemPeriod),
        new(HotkeyAction.OpenSettings, ModifierKeys.Control | ModifierKeys.Alt, Key.S),
        new(HotkeyAction.ToggleMute, ModifierKeys.Control | ModifierKeys.Alt, Key.M),
        new(HotkeyAction.VolumeUp, ModifierKeys.Control | ModifierKeys.Alt, Key.Up),
        new(HotkeyAction.VolumeDown, ModifierKeys.Control | ModifierKeys.Alt, Key.Down),
        new(HotkeyAction.ToggleLyrics, ModifierKeys.Control | ModifierKeys.Alt, Key.L),
        new(HotkeyAction.ToggleLyricsHud, ModifierKeys.Control | ModifierKeys.Alt, Key.H),
    };
}

