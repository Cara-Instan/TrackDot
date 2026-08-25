using System;
using System.Collections.Generic;

namespace TrackDot.Models;

/// <summary>
/// Immutable record representing a single timed line of lyrics.
/// </summary>
/// <param name="Index">0-based line index in the track.</param>
/// <param name="Timestamp">Position timestamp when this line begins.</param>
/// <param name="Text">Original lyric line text.</param>
/// <param name="RomajiText">Converted Romaji line text (if applicable).</param>
/// <param name="Segments">Word or Kanji-reading segments for Furigana display.</param>
/// <param name="Translation">Optional translated or secondary text line.</param>
public sealed record LyricLine(
    int Index,
    TimeSpan Timestamp,
    string Text,
    string RomajiText,
    IReadOnlyList<FuriganaSegment> Segments,
    string? Translation = null)
{
    /// <summary>
    /// Gets the secondary display text: explicit Translation if available, otherwise RomajiText if different from original text.
    /// </summary>
    public string? SecondaryText => !string.IsNullOrWhiteSpace(Translation)
        ? Translation
        : (!string.IsNullOrWhiteSpace(RomajiText) && !string.Equals(RomajiText, Text, StringComparison.OrdinalIgnoreCase) ? RomajiText : null);

    /// <summary>
    /// True if there is secondary text (translation or romaji) to display.
    /// </summary>
    public bool HasSecondaryText => !string.IsNullOrWhiteSpace(SecondaryText);
}
