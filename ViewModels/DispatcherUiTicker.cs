using System;
using System.Windows.Threading;

namespace TrackDot.ViewModels;

/// <summary>
/// Production <see cref="IUiTicker"/> backed by
/// <see cref="DispatcherTimer"/>. Fires every 250 ms on the
/// dispatcher thread the view-model is constructed on. The
/// popover (Task 7) constructs the view-model on the UI thread;
/// the tick therefore runs on the UI thread and is safe to update
/// bindable properties directly.
/// </summary>
public sealed class DispatcherUiTicker : IUiTicker
{
    private readonly DispatcherTimer _timer;

    /// <summary>
    /// Creates a 250 ms <see cref="DispatcherTimer"/> associated
    /// with the current dispatcher. Constructed in
    /// <see cref="DispatcherPriority.Background"/> so UI rendering
    /// is not starved by interpolation work.
    /// </summary>
    public DispatcherUiTicker()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
    }

    /// <inheritdoc/>
    public void Start(Action onTick)
    {
        ArgumentNullException.ThrowIfNull(onTick);
        // Replace the previous callback. DispatcherTimer fires
        // Tick on each interval regardless of reassignment, so
        // swapping the handler at Start() time is the right
        // pattern.
        _timer.Tick -= FireIfRunning;
        _timer.Tick += FireIfRunning;
        _currentCallback = onTick;
        _timer.Start();
    }

    /// <inheritdoc/>
    public void Stop()
    {
        _timer.Stop();
        _timer.Tick -= FireIfRunning;
        _currentCallback = null;
    }

    private Action? _currentCallback;
    private void FireIfRunning(object? sender, EventArgs e)
        => _currentCallback?.Invoke();
}
