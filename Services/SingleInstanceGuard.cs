using System;
using System.Threading;

namespace TrackDot.Services;

/// <summary>
/// Ensures only one TrackDot process runs at a time. The first
/// instance to construct the guard acquires a named local mutex;
/// subsequent instances see <see cref="IsAcquired"/> return
/// <c>false</c> and should exit cleanly.
/// </summary>
/// <remarks>
/// <para>
/// The guard wraps a <see cref="Mutex"/> created with the supplied
/// name. <see cref="IsAcquired"/> reflects whether
/// <see cref="Mutex.WaitOne(TimeSpan)"/> returned <c>true</c> during
/// construction — it does not re-poll the kernel afterwards.
/// </para>
/// <para>
/// Tests construct guards with unique names per test so the suite
/// can run repeatedly without cross-talk.
/// </para>
/// </remarks>
public sealed class SingleInstanceGuard : IDisposable
{
    private Mutex? _mutex;
    private bool _disposed;

    /// <summary>
    /// Creates the guard. If no other process holds the named
    /// mutex, this instance acquires it and <see cref="IsAcquired"/>
    /// is <c>true</c>. Otherwise the mutex is created but not
    /// owned and <see cref="IsAcquired"/> is <c>false</c>.
    /// </summary>
    /// <param name="name">
    /// A unique mutex name. Local-namespace (<c>Local\&hellip;</c>)
    /// names are recommended so the guard is per-session, not
    /// global.
    /// </param>
    public SingleInstanceGuard(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Mutex name must be non-empty.", nameof(name));

        _mutex = new Mutex(initiallyOwned: false, name, out var createdNew);

        // The named mutex either already exists (createdNew == false,
        // meaning another TrackDot instance owns it) or we just
        // created it. If we just created it, we own it by
        // definition — no second instance can have it yet. If it
        // already existed, another instance holds it and we must
        // NOT take it; doing so would deadlock the other instance.
        IsAcquired = createdNew;

        // If the named mutex already existed, dispose our handle so
        // we don't leak. The original owner still owns the kernel
        // object.
        if (!IsAcquired)
        {
            _mutex.Dispose();
            _mutex = null;
        }
    }

    /// <summary>
    /// True when this instance acquired the named mutex. False if
    /// another instance already owns it, or after
    /// <see cref="Dispose"/>.
    /// </summary>
    public bool IsAcquired { get; private set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_mutex is null) return;

        try
        {
            if (IsAcquired)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch (ApplicationException)
        {
            // The current thread does not own the mutex — only
            // possible if Dispose races with construction. The
            // handle will be released below.
        }
        finally
        {
            _mutex.Dispose();
            _mutex = null;
            IsAcquired = false;
        }
    }
}
