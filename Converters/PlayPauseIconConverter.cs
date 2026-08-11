using System;
using System.Globalization;
using System.Windows.Data;

namespace TrackDot.Converters;

/// <summary>
/// Converts a boolean <c>IsPlaying</c> state to a play/pause Segoe font glyph
/// (<c>\uE769</c> for pause, <c>\uE768</c> for play).
/// </summary>
[ValueConversion(typeof(bool), typeof(string))]
public sealed class PlayPauseIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isPlaying && isPlaying)
        {
            return "\uE769"; // Pause
        }
        return "\uE768"; // Play
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
