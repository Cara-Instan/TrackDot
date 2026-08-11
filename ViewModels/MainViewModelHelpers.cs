namespace TrackDot.ViewModels;

/// <summary>
/// Shared formatting helpers used by <see cref="MainViewModel"/> and
/// <see cref="TrackDot.Converters.TimeSpanTextConverter"/>. Extracted into its
/// own file so neither the converter file nor the view-model file carries
/// unrelated logic.
/// </summary>
internal static class MainViewModelHelpers
{
    /// <summary>
    /// Formats a <see cref="System.TimeSpan"/> as elapsed/duration text.
    /// Under an hour: <c>m:ss</c> (e.g. "0:05", "12:34").
    /// One hour or more: <c>h:mm:ss</c> (e.g. "1:00:00", "1:23:45").
    /// </summary>
    public static string FormatTime(System.TimeSpan ts)
    {
        if (ts < System.TimeSpan.Zero) ts = System.TimeSpan.Zero;

        var totalSeconds = (long)System.Math.Floor(ts.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;

        if (hours > 0)
        {
            return $"{hours}:{minutes:D2}:{seconds:D2}";
        }
        return $"{minutes}:{seconds:D2}";
    }

    /// <summary>
    /// Formats an SMTC Application User Model ID into a human-readable
    /// application name (e.g. "Spotify", "Google Chrome").
    /// </summary>
    public static string FormatAppName(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return string.Empty;

        var lower = aumid.ToLowerInvariant();
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("brave")) return "Brave";
        if (lower.Contains("chrome")) return "Google Chrome";
        if (lower.Contains("msedge") || lower.Contains("edge")) return "Microsoft Edge";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("opera")) return "Opera";
        if (lower.Contains("vivaldi")) return "Vivaldi";
        if (lower.Contains("youtube")) return "YouTube";
        if (lower.Contains("apple") && lower.Contains("music")) return "Apple Music";
        if (lower.Contains("vlc")) return "VLC";
        if (lower.Contains("foobar")) return "foobar2000";
        if (lower.Contains("wmplayer") || lower.Contains("mediaplayer")) return "Media Player";

        var name = aumid;
        var bangIndex = name.LastIndexOf('!');
        if (bangIndex >= 0 && bangIndex < name.Length - 1)
        {
            name = name[(bangIndex + 1)..];
        }
        else
        {
            var slashIndex = System.Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
            if (slashIndex >= 0 && slashIndex < name.Length - 1)
            {
                name = name[(slashIndex + 1)..];
            }
        }

        if (name.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        if (name.Length == 0) return string.Empty;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
