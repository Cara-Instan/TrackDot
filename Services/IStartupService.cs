using System;

namespace TrackDot.Services;

/// <summary>
/// Per-user toggle for "launch TrackDot at sign-in".
/// </summary>
/// <remarks>
/// <para>
/// The contract is intentionally minimal: <see cref="IsEnabled"/>
/// returns the current state, <see cref="Enable"/> /
/// <see cref="Disable"/> flip it. The implementation
/// (<see cref="StartupService"/>) writes a quoted executable
/// path under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>
/// with the value name <c>TrackDot</c>. Per-user registry
/// access never requires elevation.
/// </para>
/// <para>
/// The interface does not surface the underlying executable
/// path. <see cref="StartupService"/> resolves it from
/// <see cref="Environment.ProcessPath"/> at construction time
/// and writes it verbatim (quoted) on
/// <see cref="Enable"/>. If the path cannot be determined
/// (which the production code already guards against), the
/// service refuses to enable and <see cref="Enable"/> throws
/// <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
public interface IStartupService
{
    /// <summary>
    /// True when a TrackDot entry exists under the per-user
    /// <c>...\Run</c> key. False otherwise (missing entry,
    /// different value name, or — defensively — a malformed
    /// value).
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Writes (or overwrites) the <c>TrackDot</c> entry so
    /// Windows launches the current executable at sign-in.
    /// Idempotent: calling <see cref="Enable"/> when
    /// <see cref="IsEnabled"/> is already true is a no-op.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the current executable path cannot be
    /// resolved (e.g. launched from a process that does not
    /// expose <see cref="Environment.ProcessPath"/>).
    /// </exception>
    void Enable();

    /// <summary>
    /// Removes the <c>TrackDot</c> entry from the per-user
    /// <c>...\Run</c> key. Idempotent: calling
    /// <see cref="Disable"/> when <see cref="IsEnabled"/> is
    /// already false is a no-op.
    /// </summary>
    void Disable();
}
