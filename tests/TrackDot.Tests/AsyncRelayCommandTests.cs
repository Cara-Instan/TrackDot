using System;
using System.Threading.Tasks;
using System.Windows.Input;
using TrackDot.Commands;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Tests for <see cref="AsyncRelayCommand"/>. The command is a pure
/// mapping from delegate to <see cref="ICommand"/>, so every code path is
/// exercisable in-process without a WPF dispatcher.
///
/// <para>
/// We deliberately do not rely on <c>CommandManager.RequerySuggested</c>
/// in any test — the handoff spec calls out that this command raises
/// <c>CanExecuteChanged</c> only when the view-model asks, and we want a
/// failing test to surface a regression of that contract.
/// </para>
/// </summary>
public sealed class AsyncRelayCommandTests
{
    // ---- Construction ----

    [Fact]
    public void Ctor_throws_when_parameterless_execute_delegate_is_null()
    {
        // Defensive: a null execute would NRE inside Execute(). The
        // command should refuse construction instead of leaving the
        // caller with a latent NRE-on-click. Tested separately from
        // the parameterized case because the parameterless ctor
        // wraps the delegate in a lambda before forwarding - the
        // null check must happen before that wrap, otherwise the
        // first sign of trouble is at click time.
        Assert.Throws<ArgumentNullException>(
            () => new AsyncRelayCommand((Func<Task>)null!));
    }

    [Fact]
    public void Ctor_throws_when_parameterized_execute_delegate_is_null()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AsyncRelayCommand((Func<object?, Task>)null!));
    }

    // ---- Parameterless ctor ----

    [Fact]
    public async Task Execute_with_parameterless_ctor_invokes_delegate_without_parameter()
    {
        var invocations = 0;
        var sut = new AsyncRelayCommand(() =>
        {
            invocations++;
            return Task.CompletedTask;
        });

        sut.Execute(null);
        await Task.Yield(); // let the async void drain
        await Task.Yield();

        Assert.Equal(1, invocations);
    }

    // ---- Parameterized ctor ----

    [Fact]
    public async Task Execute_with_parameterized_ctor_forwards_parameter()
    {
        object? captured = "sentinel-default";
        var sut = new AsyncRelayCommand(p =>
        {
            captured = p;
            return Task.CompletedTask;
        });

        sut.Execute("hello");
        await Task.Yield();
        await Task.Yield();

        Assert.Equal("hello", captured);
    }

    // ---- CanExecute ----

    [Fact]
    public void CanExecute_returns_true_when_no_canExecute_delegate_was_supplied()
    {
        var sut = new AsyncRelayCommand(() => Task.CompletedTask);

        // The default is "always enabled" — matches the common case
        // where gating is done at the service layer or via the
        // parameterized canExecute in the view-model.
        Assert.True(sut.CanExecute(null));
    }

    [Fact]
    public void CanExecute_reflects_parameterless_delegate_output()
    {
        var sut = new AsyncRelayCommand(
            execute: () => Task.CompletedTask,
            canExecute: () => false);

        Assert.False(sut.CanExecute(null));
    }

    [Fact]
    public void CanExecute_reflects_parameterized_delegate_output()
    {
        var sut = new AsyncRelayCommand(
            execute: _ => Task.CompletedTask,
            canExecute: p => p is int i && i > 0);

        Assert.False(sut.CanExecute(0));
        Assert.False(sut.CanExecute(null));
        Assert.True(sut.CanExecute(1));
    }

    // ---- CanExecuteChanged fires after Execute ----

    [Fact]
    public async Task CanExecuteChanged_fires_after_execute_completes()
    {
        var sut = new AsyncRelayCommand(() => Task.CompletedTask);

        var raised = 0;
        sut.CanExecuteChanged += (_, _) => raised++;

        sut.Execute(null);
        await Task.Yield();
        await Task.Yield();

        // Exactly one raise per Execute — the view-model binds to
        // this to swap Play <-> Pause after the click completes.
        Assert.Equal(1, raised);
    }

    // ---- Exception is swallowed ----

    [Fact]
    public async Task Execute_swallows_exception_from_execute_delegate()
    {
        // The delegate throws synchronously. The try/catch around the
        // await must catch before ConfigureAwait(true) leaves the
        // synchronous portion of the method. We use the parameterized
        // ctor because the test only needs *some* execute delegate
        // that throws - the parameter is irrelevant.
        var sut = new AsyncRelayCommand(_ =>
        {
            throw new InvalidOperationException("boom");
        });

        // Must not throw out of the async void Execute.
        sut.Execute(null);

        await Task.Yield();
        await Task.Yield();

        // If the catch were missing, this would surface as an unobserved
        // task and (in a debug build) as a fail-fast. We can only assert
        // "we got here without an unhandled exception"; the
        // absence of crash is the contract.
    }

    [Fact]
    public async Task Execute_swallows_exception_thrown_from_inside_the_task()
    {
        // The delegate returns a faulted task rather than throwing
        // synchronously. Both code paths must be caught.
        var sut = new AsyncRelayCommand(() => Task.FromException(new InvalidOperationException("boom")));

        sut.Execute(null);

        await Task.Yield();
        await Task.Yield();

        // Same as above: arrival here is the assertion. The await in
        // Execute unwraps the task and would rethrow if the try/catch
        // were missing.
    }

    // ---- RaiseCanExecuteChanged without CommandManager hook ----

    [Fact]
    public void RaiseCanExecuteChanged_fires_event_without_CommandManager_hook()
    {
        var sut = new AsyncRelayCommand(() => Task.CompletedTask);

        var raised = 0;
        sut.CanExecuteChanged += (_, _) => raised++;

        sut.RaiseCanExecuteChanged();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void RaiseCanExecuteChanged_with_no_subscribers_is_a_noop()
    {
        // Defensive: an unsubscribed event must not NRE. This is
        // what makes the command safe to call from arbitrary
        // dispatcher-pump paths.
        var sut = new AsyncRelayCommand(() => Task.CompletedTask);

        sut.RaiseCanExecuteChanged();
    }

    // ---- ICommand surface ----

    [Fact]
    public void Command_is_ICommand_compatible()
    {
        // Sanity: view-models will bind through the ICommand surface,
        // not the concrete type. Verify the interface is implemented
        // and the standard surface (CanExecute + CanExecuteChanged) is
        // accessible via it. RaiseCanExecuteChanged is concrete-type-only
        // by design (see CommandManager-free contract).
        ICommand sut = new AsyncRelayCommand(() => Task.CompletedTask);

        Assert.True(sut.CanExecute(null));

        var raised = 0;
        sut.CanExecuteChanged += (_, _) => raised++;

        // Triggering CanExecuteChanged through the concrete type still
        // surfaces on the ICommand subscription - the event is the same
        // backing field.
        ((AsyncRelayCommand)sut).RaiseCanExecuteChanged();

        Assert.Equal(1, raised);
    }
}