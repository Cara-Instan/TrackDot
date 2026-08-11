using System;
using System.IO;
using System.Text;
using System.Threading;

namespace TrackDot.Services;

/// <summary>
/// File-backed <see cref="IUnhandledExceptionSink"/>. Appends a
/// timestamped line to <c>%LocalAppData%\\TrackDot\\crash.log</c>
/// on every <see cref="WriteLine"/>. The directory is created on
/// first write; if creation fails, every subsequent write is a
/// silent no-op (the sink reports <see cref="IsAvailable"/> =
/// <c>false</c>).
/// </summary>
/// <remarks>
/// <para>
/// Writes are serialized with a per-instance lock so concurrent
/// unhandled-exception handlers (the three events the logger
/// subscribes to can fire on different threads) do not interleave
/// in the log. Each line is opened-flushed-closed rather than
/// holding the file handle open so an application crash does not
/// leave a stale lock.
/// </para>
/// <para>
/// The sink never throws. An exception during the write is
/// swallowed silently and <see cref="IsAvailable"/> flips to
/// <c>false</c> for the remainder of the process lifetime — the
/// logger is the first thing a crashed process touches, and
/// making it crash on its own log path would be a bug.
/// </para>
/// </remarks>
public sealed class FileUnhandledExceptionSink : IUnhandledExceptionSink, IDisposable
{
    private readonly string _logPath;
    private readonly object _gate = new();
    private int _disabled; // 0 = enabled, 1 = permanently disabled (init or write failed)

    /// <summary>Full path of the log file the sink appends to.</summary>
    public string LogPath => _logPath;

    /// <summary>
    /// True while the sink is still attempting writes. False
    /// after the first failed write — every subsequent
    /// <see cref="WriteLine"/> becomes a no-op.
    /// </summary>
    public bool IsAvailable => Volatile.Read(ref _disabled) == 0;

    /// <summary>
    /// Creates the sink with the default log path
    /// <c>%LocalAppData%\\TrackDot\\crash.log</c>. Use the other
    /// ctor in tests to redirect to a temp file.
    /// </summary>
    public FileUnhandledExceptionSink()
        : this(DefaultLogPath())
    {
    }

    /// <summary>
    /// Creates the sink with an explicit log path. Tests pass a
    /// path under <c>%TEMP%</c> so the suite can be re-run
    /// without polluting the user's local app data.
    /// </summary>
    public FileUnhandledExceptionSink(string logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath))
            throw new ArgumentException("Log path must be non-empty.", nameof(logPath));
        _logPath = logPath;
    }

    /// <inheritdoc/>
    public void WriteLine(string line)
    {
        if (!IsAvailable) return;
        if (line is null) return;

        // The path is finalised on first write so the
        // constructor stays cheap and never touches the
        // filesystem. If the directory cannot be created we
        // permanently disable the sink — the process is dying
        // anyway and we do not want a tight log loop on shutdown.
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Lock so two simultaneous event handlers (Dispatcher
            // + AppDomain, for example) do not interleave.
            lock (_gate)
            {
                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Best-effort: disable silently so a subsequent
            // exception does not throw inside the exception
            // handler (which is undefined behaviour).
            Volatile.Write(ref _disabled, 1);
        }
    }

    private static string DefaultLogPath()
    {
        if (PortableMode.IsPortable)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return System.IO.Path.Combine(baseDir, "logs", "crash.log");
        }

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        return System.IO.Path.Combine(localAppData, "TrackDot", "crash.log");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The sink holds no persistent file handle (every write
    /// opens/closes the file), so Dispose is a no-op marker
    /// that allows the logger composition root to dispose
    /// the sink alongside the rest of the services.
    /// </remarks>
    public void Dispose()
    {
        // No-op. The IsAvailable flag is intentionally left
        // intact so subsequent writes remain observable; the
        // production code path does not write to the sink
        // after dispose.
    }
}
