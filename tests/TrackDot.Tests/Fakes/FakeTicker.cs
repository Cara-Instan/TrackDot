using System;
using TrackDot.ViewModels;

namespace TrackDot.Tests.Fakes;

/// <summary>
/// Captures the tick callback instead of using a real
/// <c>System.Windows.Threading.DispatcherTimer</c>. Tests call
/// <see cref="Fire"/> to simulate a 250 ms elapsed tick without
/// requiring a running dispatcher.
/// </summary>
/// <remarks>
/// Lifted out of <see cref="MainViewModelTests"/>'s private nested
/// class so multiple test files (e.g. <c>ViewModelLifecycleTests</c>)
/// can share the same fake. The semantics match the production
/// <see cref="IUiTicker"/> contract exactly: <see cref="Start"/>
/// replaces the previous callback, <see cref="Stop"/> clears it,
/// <see cref="Fire"/> invokes the captured callback if any.
/// </remarks>
public sealed class FakeTicker : IUiTicker
{
    /// <summary>The currently captured tick callback, or <c>null</c> when stopped.</summary>
    public Action? Callback { get; private set; }

    /// <summary>How many times <see cref="Start"/> was called.</summary>
    public int StartCallCount { get; private set; }

    /// <summary>How many times <see cref="Stop"/> was called.</summary>
    public int StopCallCount { get; private set; }

    /// <summary>True when a callback is currently captured (i.e. after <see cref="Start"/> and before <see cref="Stop"/>).</summary>
    public bool IsRunning => Callback is not null;

    /// <inheritdoc/>
    public void Start(Action onTick)
    {
        ArgumentNullException.ThrowIfNull(onTick);
        Callback = onTick;
        StartCallCount++;
    }

    /// <inheritdoc/>
    public void Stop()
    {
        Callback = null;
        StopCallCount++;
    }

    /// <summary>
    /// Invoke the captured callback synchronously. No-op when
    /// stopped. The snapshot is taken before invocation so a
    /// callback that re-starts or stops the timer mid-fire still
    /// completes the current tick.
    /// </summary>
    public void Fire()
    {
        var cb = Callback;
        cb?.Invoke();
    }
}
