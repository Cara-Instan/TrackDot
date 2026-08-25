using System;

namespace TrackDot.Services;

/// <summary>
/// Service managing persistent window settings such as Pin to Top
/// and window opacity (transparency).
/// </summary>
public interface IWindowSettingsService
{
    /// <summary>
    /// <see langword="true"/> when the main popover is pinned to top
    /// and should stay visible when focus is deactivated.
    /// </summary>
    bool IsPinned { get; set; }

    /// <summary>
    /// Window opacity in range [0.2, 1.0].
    /// </summary>
    double WindowOpacity { get; set; }

    /// <summary>
    /// Window opacity expressed as an integer percentage [20–100].
    /// </summary>
    int OpacityPercent { get; set; }

    /// <summary>
    /// <see langword="true"/> when system-wide global hotkeys are enabled.
    /// </summary>
    bool EnableGlobalHotkeys { get; set; }

    /// <summary>
    /// <see langword="true"/> when the lyrics window is visible/open.
    /// </summary>
    bool LyricsWindowVisible { get; set; }

    /// <summary>
    /// Lyrics window opacity expressed as an integer percentage [20–100].
    /// </summary>
    int LyricsOpacityPercent { get; set; }

    /// <summary>
    /// <see langword="true"/> when lyrics window is always-on-top / sticky.
    /// </summary>
    bool LyricsIsTopmost { get; set; }

    /// <summary>
    /// <see langword="true"/> when Furigana ruby reading text is shown above Japanese lyrics.
    /// </summary>
    bool LyricsIsFuriganaVisible { get; set; }

    /// <summary>
    /// <see langword="true"/> when translated or secondary lyric lines are displayed below original lyrics.
    /// </summary>
    bool LyricsShowTranslation { get; set; }

    /// <summary>
    /// Saved X position of lyrics window.
    /// </summary>
    double LyricsWindowLeft { get; set; }

    /// <summary>
    /// Saved Y position of lyrics window.
    /// </summary>
    double LyricsWindowTop { get; set; }

    /// <summary>
    /// Saved width of lyrics window.
    /// </summary>
    double LyricsWindowWidth { get; set; }

    /// <summary>
    /// Saved height of lyrics window.
    /// </summary>
    double LyricsWindowHeight { get; set; }

    /// <summary>
    /// <see langword="true"/> when the mini floating lyrics HUD overlay is visible.
    /// </summary>
    bool LyricsHudVisible { get; set; }

    /// <summary>
    /// <see langword="true"/> when the lyrics HUD is locked in click-through mode (WS_EX_TRANSPARENT).
    /// </summary>
    bool LyricsHudIsLocked { get; set; }

    /// <summary>
    /// Saved X position of lyrics HUD window.
    /// </summary>
    double LyricsHudLeft { get; set; }

    /// <summary>
    /// Saved Y position of lyrics HUD window.
    /// </summary>
    double LyricsHudTop { get; set; }

    /// <summary>
    /// Saved width of lyrics HUD window.
    /// </summary>
    double LyricsHudWidth { get; set; }

    /// <summary>
    /// Saved height of lyrics HUD window.
    /// </summary>
    double LyricsHudHeight { get; set; }

    /// <summary>
    /// Lyrics HUD opacity percentage [20-100].
    /// </summary>
    int LyricsHudOpacityPercent { get; set; }

    /// <summary>
    /// Lyrics HUD base font size in pixels [14-60].
    /// </summary>
    double LyricsHudFontSize { get; set; }

    /// <summary>
    /// <see langword="true"/> when Furigana ruby text is enabled in the floating HUD.
    /// </summary>
    bool LyricsHudShowFurigana { get; set; }

    /// <summary>
    /// <see langword="true"/> when Translation / secondary lines are enabled in the floating HUD.
    /// </summary>
    bool LyricsHudShowTranslation { get; set; }

    /// <summary>
    /// <see langword="true"/> when dynamic palette tinting from album art is enabled.
    /// </summary>
    bool EnableDynamicTinting { get; set; }

    /// <summary>
    /// Configured global system-wide hotkeys.
    /// </summary>
    IReadOnlyList<TrackDot.Models.HotkeyBinding> HotkeyBindings { get; set; }

    /// <summary>
    /// Gets the binding configured for a specific action.
    /// </summary>
    TrackDot.Models.HotkeyBinding GetHotkeyBinding(TrackDot.Models.HotkeyAction action);

    /// <summary>
    /// Updates or sets a binding for a specific action.
    /// </summary>
    void SetHotkeyBinding(TrackDot.Models.HotkeyAction action, System.Windows.Input.ModifierKeys modifiers, System.Windows.Input.Key key);

    /// <summary>
    /// Resets all hotkey bindings to their default keys.
    /// </summary>
    void ResetHotkeyBindingsToDefault();

    /// <summary>
    /// Event raised when any window setting changes.
    /// </summary>
    event EventHandler? SettingsChanged;
}
