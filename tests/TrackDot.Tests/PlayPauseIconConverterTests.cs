using System;
using System.Globalization;
using TrackDot.Converters;
using Xunit;

namespace TrackDot.Tests;

public sealed class PlayPauseIconConverterTests
{
    [Theory]
    [InlineData(true, "\uE769")]  // Pause glyph
    [InlineData(false, "\uE768")] // Play glyph
    [InlineData(null, "\uE768")]  // Fallback Play
    public void Converts_IsPlaying_bool_to_correct_segoe_glyph(object? input, string expected)
    {
        var converter = new PlayPauseIconConverter();
        var result = converter.Convert(input, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }
}
