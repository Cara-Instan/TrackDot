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
public sealed record LyricLine(
    int Index,
    TimeSpan Timestamp,
    string Text,
    string RomajiText,
    IReadOnlyList<FuriganaSegment> Segments);
