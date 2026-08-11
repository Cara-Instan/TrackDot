using System;

namespace TrackDot.Services;

/// <summary>
/// Receives a single line of text for each unhandled exception
/// the logger routes. The production sink writes to
/// <c>%LocalAppData%\\TrackDot\\crash.log</c>; tests substitute
/// a recording fake so the logger can be exercised without
/// touching the filesystem.
/// </summary>
public interface IUnhandledExceptionSink
{
    /// <summary>
    /// Append a single line to the sink. The line does not need
    /// to include a trailing newline — the sink adds it. Throwing
    /// from this method must not propagate out of the logger; the
    /// logger treats the sink as best-effort and swallows sink
    /// failures silently.
    /// </summary>
    void WriteLine(string line);
}
