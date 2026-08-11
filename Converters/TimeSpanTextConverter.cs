using System;
using System.Globalization;
using System.Windows.Data;
using TrackDot.ViewModels; // MainViewModelHelpers lives here now

namespace TrackDot.Converters;

// Convenience alias so existing callers in this namespace resolve without changes.
using MainViewModelHelpers = TrackDot.ViewModels.MainViewModelHelpers;

/// <summary>
/// Formats a <see cref="TimeSpan"/> as elapsed/duration text for
/// the popover (e.g. "1:23" or "1:02:03" for &gt; 1 hour). Returns
/// <see cref="string.Empty"/> for null/non-time inputs so the
/// binding never throws.
/// </summary>
/// <remarks>
/// The format is:
/// <list type="bullet">
///   <item>&lt; 1 hour — <c>m:ss</c> (e.g. "0:05", "12:34")</item>
///   <item>&gt;= 1 hour — <c>h:mm:ss</c> (e.g. "1:00:00", "1:23:45")</item>
/// </list>
/// The hours digit is unconstrained above 9 — this matches the
/// style of typical media players (Spotify, Foobar2000, etc.).
/// </remarks>
[ValueConversion(typeof(TimeSpan), typeof(string))]
public sealed class TimeSpanTextConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
        {
            return MainViewModelHelpers.FormatTime(ts);
        }
        return string.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Not used by the popover. The view-model exposes
    /// <c>ElapsedTimeText</c> as a derived property; the converter
    /// is a one-way formatting helper for any XAML binding that
    /// needs to bypass the view-model (e.g. accessibility labels
    /// built from raw snapshots).
    /// </remarks>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

