using System;
using System.Globalization;
using System.Windows.Data;

namespace TrackDot.Converters;

/// <summary>
/// Compares multiple bound values for equality. Returns true if all values are equal.
/// </summary>
public sealed class EqualityToBooleanConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 2) return false;
        var first = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (first == null || values[i] == null)
            {
                if (first != values[i]) return false;
            }
            else if (!Equals(first, values[i]))
            {
                if (System.Convert.ToString(first, culture) != System.Convert.ToString(values[i], culture))
                {
                    return false;
                }
            }
        }
        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
