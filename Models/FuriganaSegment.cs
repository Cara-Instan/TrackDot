namespace TrackDot.Models;

/// <summary>
/// Represents a single word or character segment with its original surface text
/// and optional Furigana reading (e.g. Romaji or Hiragana reading).
/// </summary>
/// <param name="Surface">Original text segment (e.g. "音楽" or "Hello").</param>
/// <param name="Reading">Furigana reading (e.g. "ongaku"), or empty if no reading/Latin.</param>
public sealed record FuriganaSegment(string Surface, string Reading)
{
    /// <summary>
    /// <see langword="true"/> when this segment has a non-empty reading distinct from the surface.
    /// </summary>
    public bool HasReading => !string.IsNullOrWhiteSpace(Reading) &&
                              !string.Equals(Surface.Trim(), Reading.Trim(), System.StringComparison.OrdinalIgnoreCase);
}
