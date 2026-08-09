using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace TrackDot.Commands;

/// <summary>
/// An <see cref="ICommand"/> whose <see cref="Execute(object?)"/> runs an
/// asynchronous delegate. Intended for binding transport buttons (play/pause,
/// previous, stop, next) to <see cref="TrackDot.Services.IMediaControllerService"/>
/// from XAML.
///
/// <para>
/// Two design points worth calling out:
/// </para>
/// <list type="bullet">
///   <item>
///     <see cref="Execute(object?)"/> is <c>async void</c> because
///     <see cref="ICommand"/> demands a <c>void</c> signature, but the body
///     wraps the delegate in <c>try/catch</c> so a faulted command never
///     tears down the dispatcher. The SMTC service also swallows internally
///     (see <c>MediaControllerService.InvokeOnSessionAsync</c>), so this is
///     belt-and-suspenders.
///   </item>
///   <item>
///     <see cref="RaiseCanExecuteChanged"/> fires the event directly. We do
///     <em>not</em> hook <c>CommandManager.RequerySuggested</c> on purpose:
///     the view-model layer (Task 6) will call <see cref="RaiseCanExecuteChanged"/>
///     when <c>TransportCapabilities</c> changes, which keeps this class
///     deterministic and unit-testable without a running WPF dispatcher.
///   </item>
/// </list>
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>
    /// Creates a command that runs <paramref name="execute"/> with no
    /// parameter. The most common case for buttons that don't bind a
    /// <c>CommandParameter</c>.
    /// </summary>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        // The null check has to happen here, before forwarding to the
        // parameterized ctor via a lambda — wrapping first and checking
        // second would only surface the null when the delegate runs.
        ArgumentNullException.ThrowIfNull(execute);
        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    /// <summary>
    /// Creates a command that runs <paramref name="execute"/> with the
    /// XAML-bound parameter forwarded. Use this when a single command
    /// instance needs to act on a per-invocation value.
    /// </summary>
    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <inheritdoc/>
    public bool CanExecute(object? parameter)
        => _canExecute is null || _canExecute(parameter);

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="ICommand.Execute(object?)"/> returns <c>void</c>, so we
    /// cannot make this method <c>async Task</c>. The internal
    /// <c>try/catch</c> is required: an unobserved exception from an
    /// <c>async void</c> method on the dispatcher would crash the UI
    /// thread.
    /// </remarks>
    public async void Execute(object? parameter)
    {
        try
        {
            await _execute(parameter).ConfigureAwait(true);
        }
        catch
        {
            // Swallowed: the controller service already swallows on its
            // end, but a stray exception here would surface as an
            // unobserved task and (worse) propagate out of an async void
            // method into the dispatcher. Keep the contract strict.
        }
        finally
        {
            // Re-evaluate so the UI toggles Play <-> Pause without
            // waiting for the next CapabilityChanged event.
            RaiseCanExecuteChanged();
        }
    }

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Raises <see cref="CanExecuteChanged"/> manually. The view-model
    /// layer calls this after <c>TransportCapabilities</c> updates so
    /// XAML-bound buttons re-evaluate their enabled state.
    /// </summary>
    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}