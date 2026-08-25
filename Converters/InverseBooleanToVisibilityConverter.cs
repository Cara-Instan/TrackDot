using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TrackDot.Converters;

/// <summary>
/// Converts a boolean value to a <see cref="Visibility"/> value, where true maps to Collapsed and false maps to Visible.
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        return false;
    }
}

