using System;
using System.IO;

namespace TrackDot.Services;

/// <summary>
/// Per-user "launch at sign-in" implementation. Stores a
/// quoted executable path under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>
/// with the value name <c>TrackDot</c>.
/// </summary>
/// <remarks>
/// <para>
/// The executable path is resolved at construction time via
/// <see cref="Environment.ProcessPath"/>. This is the .NET 6+
/// replacement for <c>Process.MainModule.FileName</c> and
/// avoids the COM activation cost (and exception surface)
/// of touching <c>System.Diagnostics.Process</c> for the
/// current process.
/// </para>
/// <para>
/// The path is stored quoted (<c>"...path..."</c>) so a path
/// that contains spaces — which every per-user install path
/// on Windows does (<c>%LocalAppData%\Programs\...</c>) — is
/// parsed correctly by Windows' Run-key parser. The detection
/// path (<see cref="IsEnabled"/>) compares against the
/// unquoted source path so the two are kept in sync.
/// </para>
/// <para>
/// <see cref="IsEnabled"/> is the source of truth. If the
/// stored value matches the current executable, the entry is
/// considered "ours" and toggle-on does a no-op. If the
/// stored value points at a different executable (manually
/// overridden), <see cref="Enable"/> overwrites it; the user
/// is expected to use this app to manage the entry.
/// </para>
/// </remarks>
public sealed class StartupService : IStartupService
{
    private readonly IRegistryKeyFactory _registryFactory;

    /// <summary>
    /// The current executable path, quoted for storage. Set at
    /// construction; <c>null</c> when <see cref="Environment.ProcessPath"/>
    /// returns <c>null</c> (a corner case on unusual hosts).
    /// </summary>
    private readonly string? _quotedExecutablePath;

    /// <summary>
    /// The current executable path, unquoted. Used to compare
    /// against the stored value when deciding whether the
    /// entry is "ours" or foreign.
    /// </summary>
    private readonly string? _executablePath;

    /// <summary>
    /// Production constructor. Resolves the current
    /// executable path and stores it for later enable calls.
    /// </summary>
    public StartupService(IRegistryKeyFactory registryFactory)
    {
        ArgumentNullException.ThrowIfNull(registryFactory);
        _registryFactory = registryFactory;

        // Environment.ProcessPath can be null when the
        // process was launched in a way that does not expose
        // it (rare — mostly unit-test hosts). Capture the
        // null so Enable() can refuse with a clear message
        // instead of NRE'ing later.
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
        {
            _executablePath = path;
            _quotedExecutablePath = QuotePath(path);
        }
    }

    /// <summary>
    /// Test seam. Constructs a service bound to a specific
    /// executable path so the unit tests can exercise
    /// enable/disable round-trips without depending on the
    /// test runner's host path.
    /// </summary>
    internal StartupService(IRegistryKeyFactory registryFactory, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(registryFactory);
        ArgumentNullException.ThrowIfNull(executablePath);
        _registryFactory = registryFactory;
        _executablePath = executablePath;
        _quotedExecutablePath = QuotePath(executablePath);
    }

    /// <summary>
    /// Test seam for the "executable path could not be
    /// resolved" branch. Constructs a service whose path
    /// fields remain null — the same state the production
    /// ctor leaves the service in when
    /// <see cref="Environment.ProcessPath"/> returns null.
    /// </summary>
    internal StartupService(IRegistryKeyFactory registryFactory, bool unresolvedPath)
    {
        ArgumentNullException.ThrowIfNull(registryFactory);
        _ = unresolvedPath; // marker overload — distinguishes from the path-taking ctor
        _registryFactory = registryFactory;
        // Both fields intentionally left null.
    }

    /// <inheritdoc/>
    public bool IsEnabled
    {
        get
        {
            if (_executablePath is null) return false;

            using var key = _registryFactory.OpenRunKey();
            var stored = key.ReadValue(RegistryKeyFactory.ValueName);
            if (stored is null) return false;

            // Compare the unquoted stored value against the
            // current unquoted path. StoredQuoted may include
            // quotes; UnquotePath normalises both sides.
            return PathsEqual(UnquotePath(stored), _executablePath);
        }
    }

    /// <inheritdoc/>
    public void Enable()
    {
        if (string.IsNullOrWhiteSpace(_quotedExecutablePath)
            || string.IsNullOrWhiteSpace(_executablePath))
        {
            throw new InvalidOperationException(
                "Cannot enable launch-at-sign-in: the current executable path could not be resolved.");
        }
        if (IsEnabled) return;

        using var key = _registryFactory.OpenRunKey();
        key.WriteValue(RegistryKeyFactory.ValueName, _quotedExecutablePath);
    }

    /// <inheritdoc/>
    public void Disable()
    {
        if (!IsEnabled) return;

        using var key = _registryFactory.OpenRunKey();
        key.DeleteValue(RegistryKeyFactory.ValueName);
    }

    /// <summary>
    /// Wraps <paramref name="path"/> in double quotes. Empty
    /// or whitespace-only input is returned unchanged so a
    /// subsequent call to <see cref="Enable"/> throws the
    /// "could not be resolved" message instead of writing a
    /// bogus empty string.
    /// </summary>
    private static string QuotePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        return "\"" + path + "\"";
    }

    /// <summary>
    /// Strips a single pair of surrounding double quotes.
    /// Returns the input unchanged if it is not wrapped in
    /// quotes — the Windows Run-key parser also accepts
    /// unquoted paths so the detection path must too.
    /// </summary>
    private static string UnquotePath(string path)
    {
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
        {
            return path.Substring(1, path.Length - 2);
        }
        return path;
    }

    /// <summary>
    /// Compares two paths for equality ignoring case and
    /// trailing directory separators. Windows file paths are
    /// case-insensitive (<c>C:\Program Files</c> ==
    /// <c>c:\PROGRAM FILES</c>); directory separators are
    /// normalised so <c>C:\App\</c> matches <c>C:\App</c>.
    /// </summary>
    private static bool PathsEqual(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        var normA = TrimTrailingSeparators(a);
        var normB = TrimTrailingSeparators(b);
        return string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimTrailingSeparators(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? path : trimmed;
    }
}