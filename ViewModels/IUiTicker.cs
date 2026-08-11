using System;

namespace TrackDot.ViewModels;

/// <summary>
/// Abstraction over the WPF <c>DispatcherTimer</c> used to drive
/// progress interpolation. The production implementation
/// (<see cref="DispatcherUiTicker"/>) wraps a real
/// <c>System.Windows.Threading.DispatcherTimer</c> at 250 ms; tests
/// substitute a fake that captures the callback so the test code
/// can fire ticks deterministically without a running dispatcher.
/// </summary>
/// <remarks>
/// The interface lives in the view-model namespace because that is
/// the only place that needs to drive UI ticks. Adding a public
/// abstraction in <c>TrackDot.Services</c> would imply the service
/// layer cares about UI timing — it does not.
/// </remarks>
public interface IUiTicker
{
    /// <summary>
    /// Begin firing <paramref name="onTick"/> on the dispatcher
    /// thread. Idempotent — calling <see cref="Start"/> while
    /// already running must replace the previous callback
    /// (the view-model uses this to reset the tick handler when a
    /// new snapshot arrives).
    /// </summary>
    void Start(Action onTick);

    /// <summary>
    /// Stop firing ticks. Idempotent — calling <see cref="Stop"/>
    /// while not running is a no-op.
    /// </summary>
    void Stop();
}
