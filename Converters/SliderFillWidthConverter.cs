using System;
using System.Globalization;
using System.Windows.Data;

namespace TrackDot.Converters;

/// <summary>
/// Multi-value converter used by the custom Slider template to compute the
/// pixel width of the "filled" portion of the seek track.
/// Bindings in order: <c>Value</c>, <c>Maximum</c>, <c>ActualWidth</c>.
/// Returns 0 when Maximum is zero (no media loaded).
/// </summary>
[ValueConversion(typeof(double[]), typeof(double))]
public sealed class SliderFillWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3
            || values[0] is not double val
            || values[1] is not double max
            || values[2] is not double totalWidth)
        {
            return 0d;
        }

        if (max <= 0 || totalWidth <= 0) return 0d;

        var ratio = Math.Clamp(val / max, 0d, 1d);
        return ratio * totalWidth;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
