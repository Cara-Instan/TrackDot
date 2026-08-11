using System;
using System.IO;

namespace TrackDot.Services;

/// <summary>
/// Provides detection for Portable Mode. TrackDot runs in Portable Mode
/// when a <c>portable.dat</c> marker file exists in the application directory.
/// </summary>
public static class PortableMode
{
    private static bool? _isPortable;

    /// <summary>
    /// Gets a value indicating whether TrackDot is running in Portable Mode.
    /// </summary>
    public static bool IsPortable
    {
        get
        {
            if (!_isPortable.HasValue)
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                _isPortable = File.Exists(Path.Combine(baseDir, "portable.dat"));
            }
            return _isPortable.Value;
        }
        internal set => _isPortable = value;
    }
}
