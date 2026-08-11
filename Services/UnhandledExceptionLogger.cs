using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TrackDot.Services;

/// <summary>
/// Wires the three unhandled-exception channels
/// (<c>DispatcherUnhandledException</c>,
/// <c>AppDomain.UnhandledException</c>,
/// <c>TaskScheduler.UnobservedTaskException</c>) into a single
/// <see cref="IUnhandledExceptionSink"/>. The logger is a
/// composition-time helper: construct it once at app startup,
/// dispose it once at app exit, and the three subscriptions
/// are torn down together.
/// </summary>
/// <remarks>
/// <para>
/// Behaviour, per channel:
/// </para>
/// <list type="bullet">
/// <item>
/// <c>DispatcherUnhandledException</c>: logs the exception,
/// marks the event <c>Handled = true</c> so WPF does not
/// crash the process. This is the recoverable path — a single
/// failed binding evaluation should not kill the tray.
/// </item>
/// <item>
/// <c>AppDomain.UnhandledException</c>: logs the exception.
/// The CLR is already terminating; <c>Handled</c> is not
/// available. The log line is the only post-mortem evidence
/// we can leave.
/// </item>
/// <item>
/// <c>TaskScheduler.UnobservedTaskException</c>: logs the
/// exception, marks the event <c>Observed = true</c> so the
/// CLR's escalation policy does not fire. The popover's
/// <c>MediaControllerService</c> swallows internally so this
/// is a belt-and-suspenders logger.
/// </item>
/// </list>
/// <para>
/// Every code path is null- and exception-safe. The logger
/// itself must never throw inside an exception handler.
/// </para>
/// </remarks>
public sealed class UnhandledExceptionLogger : IDisposable
{
    private readonly IUnhandledExceptionSink _sink;
    private readonly Application? _application;
    private readonly EventHandler<UnobservedTaskExceptionEventArgs> _taskHandler;
    private bool _disposed;

    /// <summary>
    /// Creates the logger and subscribes to the three events.
    /// Pass <c>Application.Current</c> from <c>App.OnStartup</c>
    /// after the WPF application object has been initialised.
    /// Passing <c>null</c> disables the WPF-specific
    /// subscription (the other two remain) and keeps the
    /// logger usable in unit tests.
    /// </summary>
    public UnhandledExceptionLogger(Application? application, IUnhandledExceptionSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
        _application = application;

        // Stash the TaskScheduler handler in a field so Dispose
        // can symmetrically unsubscribe. The compile-time type
        // is the generic EventHandler<T> — the runtime event is
        // typed as such.
        _taskHandler = OnTaskSchedulerUnobservedTaskException;

        if (application is not null)
        {
            application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        }
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += _taskHandler;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            _sink.WriteLine(Format("Dispatcher", e.Exception));
        }
        finally
        {
            // Mark Handled so WPF does not crash the process on
            // recoverable UI errors. If the exception is true
            // state corruption, the log line is the post-mortem
            // evidence — re-throwing inside the handler makes
            // the process less debuggable, not more.
            e.Handled = true;
        }
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // No way to set Handled on AppDomain — the process is
        // already terminating. Write and return.
        if (e.ExceptionObject is Exception ex)
        {
            TryWrite(Format("AppDomain", ex));
        }
        else
        {
            TryWrite($"{DateTimeOffset.Now:O} [AppDomain] Non-CLR exception: {e.ExceptionObject}");
        }
    }

    private void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            _sink.WriteLine(Format("TaskScheduler", e.Exception));
        }
        finally
        {
            e.SetObserved();
        }
    }

    private void TryWrite(string line)
    {
        try
        {
            _sink.WriteLine(line);
        }
        catch
        {
            // Sink failures are silent. The logger must never
            // throw from a handler.
        }
    }

    /// <summary>
    /// Formats an exception with timestamp and channel tag.
    /// Pure function — exposed as <c>internal</c> so tests can
    /// assert the on-the-wire format without spinning up a
    /// real WPF <c>Application</c>.
    /// </summary>
    internal static string Format(string channel, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(ex);
        return $"{DateTimeOffset.Now:O} [{channel}] {ex}";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unsubscribe from the process-wide events. The
        // WPF-specific subscription is bound to the
        // Application that was passed in. If the WPF
        // application was not supplied (test path) the
        // field is null and the WPF unsubscribe is a
        // no-op.
        if (_application is not null)
        {
            _application.DispatcherUnhandledException -= OnDispatcherUnhandledException;
        }
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= _taskHandler;
    }
}
