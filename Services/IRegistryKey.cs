using System;

namespace TrackDot.Services;

/// <summary>
/// Tiny key/value adapter over the per-user registry. The
/// production <see cref="RegistryKeyAdapter"/> wraps
/// <c>Microsoft.Win32.Registry.CurrentUser</c>; tests inject
/// an in-memory fake. Every method returns a value or throws
/// — never a sentinel — so the implementation can rely on the
/// caller handling failures.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReadValue"/> returns <c>null</c> when the value
/// does not exist (the registry's own convention). It throws
/// <see cref="InvalidOperationException"/> for other I/O
/// failures (the registry is locked, access is denied, etc.)
/// so the production wrapper can choose to swallow at the
/// <see cref="StartupService"/> layer.
/// </para>
/// <para>
/// The adapter is one-shot: it owns the open registry key
/// handle and disposes it in <see cref="IDisposable.Dispose"/>.
/// Callers do not need to close the underlying handle
/// themselves.
/// </para>
/// </remarks>
public interface IRegistryKey : IDisposable
{
    /// <summary>
    /// Reads the named value as a string. Returns <c>null</c>
    /// if the value does not exist.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The key is closed, the value exists but cannot be
    /// converted to a string, or the registry call failed.
    /// </exception>
    string? ReadValue(string name);

    /// <summary>
    /// Writes (or overwrites) the named value with the
    /// supplied string. Passing <c>null</c> deletes the value
    /// (same as <see cref="DeleteValue"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The key is closed or the registry call failed.
    /// </exception>
    void WriteValue(string name, string? value);

    /// <summary>
    /// Deletes the named value. Idempotent: deleting a
    /// non-existent value is a no-op (does not throw).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The key is closed or the registry call failed for a
    /// reason other than the value being missing.
    /// </exception>
    void DeleteValue(string name);
}

/// <summary>
/// Opens the per-user <c>...\Run</c> key. The factory owns
/// the key-name constant so <see cref="StartupService"/> does
/// not need to know the registry path.
/// </summary>
public interface IRegistryKeyFactory
{
    /// <summary>
    /// Opens (or creates) the Run key under
    /// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>
    /// in write mode. The returned key is owned by the caller
    /// and must be disposed.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The registry call failed.
    /// </exception>
    IRegistryKey OpenRunKey();
}
