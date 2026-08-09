using System;
using System.Threading;
using System.Threading.Tasks;
using TrackDot.Models;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Tests for the guard logic added in Task 5b. The service's command
/// methods run three guards in order:
/// <list type="number">
///   <item>no-session short-circuit</item>
///   <item>capability short-circuit (read from the cached snapshot)</item>
///   <item>failed-try refresh (false return or thrown exception)</item>
/// </list>
/// The WinRT runtime classes cannot be substituted, so the pure
/// guard logic lives in the internal
/// <c>MediaControllerService.DispatchGuardedCommandAsync</c> method.
/// These tests exercise that method with delegate-based fakes for
/// the command and refresh callbacks.
/// </summary>
public sealed class MediaControllerCommandTests
{
    private static readonly TransportCapabilities AllEnabled = new(
        CanPlay: true,
        CanPause: true,
        CanStop: true,
        CanGoPrevious: true,
        CanGoNext: true);

    private static readonly TransportCapabilities OnlyPlayPause = new(
        CanPlay: true,
        CanPause: true,
        CanStop: false,
        CanGoPrevious: false,
        CanGoNext: false);

    private static MediaControllerService BuildService()
    {
        // The dispatcher context is captured at construction time.
        // Tests run on the xUnit thread which has no SynchronizationContext,
        // so we pass one explicitly to satisfy the ctor's null check.
        var ctx = new SynchronizationContext();
        return new MediaControllerService(ctx);
    }

    // -------------------------------------------------------------------
    // Capability short-circuit
    // -------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_returns_immediately_when_capability_is_false()
    {
        var service = BuildService();
        service.SetCapabilitiesForTest(OnlyPlayPause);

        var tryInvoked = false;
        var refreshInvoked = false;

        await service.DispatchGuardedCommandAsync(
            capability: caps => caps.CanGoPrevious,
            tryCommand: () =>
            {
                tryInvoked = true;
                return Task.FromResult(true);
            },
            refresh: () => refreshInvoked = true);

        Assert.False(tryInvoked, "tryCommand should not be invoked when the capability gate is closed.");
        Assert.False(refreshInvoked, "refresh should not be invoked when the capability gate is closed.");
    }

    [Fact]
    public async Task Dispatch_invokes_tryCommand_when_capability_is_true()
    {
        var service = BuildService();
        service.SetCapabilitiesForTest(AllEnabled);

        var tryInvoked = false;
        var refreshInvoked = false;

        await service.DispatchGuardedCommandAsync(
            capability: caps => caps.CanGoNext,
            tryCommand: () =>
            {
                tryInvoked = true;
                return Task.FromResult(true);
            },
            refresh: () => refreshInvoked = true);

        Assert.True(tryInvoked, "tryCommand should be invoked when the capability gate is open.");
        Assert.False(refreshInvoked, "refresh should not be invoked when the command succeeded.");
    }

    [Fact]
    public async Task Dispatch_TogglePlayPause_capability_is_true_when_either_flag_is_set()
    {
        // The production TogglePlayPause capability delegate returns
        // CanPlay || CanPause. Verify the gate honours both.
        var service = BuildService();
        service.SetCapabilitiesForTest(new TransportCapabilities(
            CanPlay: false, CanPause: true, CanStop: false, CanGoPrevious: false, CanGoNext: false));

        var tryInvoked = false;

        await service.DispatchGuardedCommandAsync(
            capability: caps => caps.CanPlay || caps.CanPause,
            tryCommand: () =>
            {
                tryInvoked = true;
                return Task.FromResult(true);
            },
            refresh: () => { });

        Assert.True(tryInvoked, "TogglePlayPause should be enabled when CanPause is true.");
    }

    [Fact]
    public async Task Dispatch_TogglePlayPause_capability_is_false_when_neither_flag_is_set()
    {
        var service = BuildService();
        service.SetCapabilitiesForTest(TransportCapabilities.None);

        var tryInvoked = false;

        await service.DispatchGuardedCommandAsync(
            capability: caps => caps.CanPlay || caps.CanPause,
            tryCommand: () =>
            {
                tryInvoked = true;
                return Task.FromResult(true);
            },
            refresh: () => { });

        Assert.False(tryInvoked, "TogglePlayPause should be disabled when both CanPlay and CanPause are false.");
    }

    // -------------------------------------------------------------------
    // Failed-try refresh path
    // -------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_invokes_refresh_when_tryCommand_returns_false()
    {
        var service = BuildService();
        service.SetCapabilitiesForTest(AllEnabled);

        var refreshInvoked = false;

        await service.DispatchGuardedCommandAsync(
            capability: _ => true,
            tryCommand: () => Task.FromResult(false),
            refresh: () => refreshInvoked = true);

        Assert.True(refreshInvoked, "refresh must run when the session refuses the command.");
    }

    [Fact]
    public async Task Dispatch_invokes_refresh_when_tryCommand_throws()
    {
        var service = BuildService();
        service.SetCapabilitiesForTest(AllEnabled);

        var refreshInvoked = false;

        await service.DispatchGuardedCommandAsync(
            capability: _ => true,
            tryCommand: () => throw new InvalidOperationException("session closing"),
            refresh: () => refreshInvoked = true);

        Assert.True(refreshInvoked, "refresh must run when the command throws.");
    }

    [Fact]
    public async Task Dispatch_does_not_invoke_refresh_when_tryCommand_returns_true()
    {
        var service = BuildService();
        service.SetCapabilitiesForTest(AllEnabled);

        var refreshInvoked = false;

        await service.DispatchGuardedCommandAsync(
            capability: _ => true,
            tryCommand: () => Task.FromResult(true),
            refresh: () => refreshInvoked = true);

        Assert.False(refreshInvoked, "refresh must NOT run when the command succeeded.");
    }

    [Fact]
    public async Task Dispatch_swallows_thrown_exception_so_caller_does_not_observe_it()
    {
        var service = BuildService();
        service.SetCapabilitiesForTest(AllEnabled);

        // The command methods must not propagate session-side
        // exceptions. The dispatch helper is the last chance to
        // swallow; if it throws, the caller's Task faults.
        await service.DispatchGuardedCommandAsync(
            capability: _ => true,
            tryCommand: () => throw new InvalidOperationException("source-app refused"),
            refresh: () => { });
    }

    // -------------------------------------------------------------------
    // No-session short-circuit via the public surface
    // -------------------------------------------------------------------

    [Fact]
    public async Task PreviousAsync_returns_when_no_session_is_active()
    {
        var service = BuildService();
        service.ClearSessionForTest();

        // Should return immediately, without throwing. The
        // production code paths through DispatchGuardedCommandAsync
        // which then calls tryCommand; tryCommand sees the null
        // session and returns Task.FromResult(false). Either way the
        // public method completes normally.
        await service.PreviousAsync();
    }

    [Fact]
    public async Task TogglePlayPauseAsync_returns_when_no_session_is_active()
    {
        var service = BuildService();
        service.ClearSessionForTest();

        await service.TogglePlayPauseAsync();
    }

    [Fact]
    public async Task StopAsync_returns_when_no_session_is_active()
    {
        var service = BuildService();
        service.ClearSessionForTest();

        await service.StopAsync();
    }

    [Fact]
    public async Task NextAsync_returns_when_no_session_is_active()
    {
        var service = BuildService();
        service.ClearSessionForTest();

        await service.NextAsync();
    }

    // -------------------------------------------------------------------
    // Argument validation
    // -------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_throws_when_capability_is_null()
    {
        var service = BuildService();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.DispatchGuardedCommandAsync(
                capability: null!,
                tryCommand: () => Task.FromResult(true),
                refresh: () => { }));
    }

    [Fact]
    public async Task Dispatch_throws_when_tryCommand_is_null()
    {
        var service = BuildService();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.DispatchGuardedCommandAsync(
                capability: _ => true,
                tryCommand: null!,
                refresh: () => { }));
    }

    [Fact]
    public async Task Dispatch_throws_when_refresh_is_null()
    {
        var service = BuildService();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.DispatchGuardedCommandAsync(
                capability: _ => true,
                tryCommand: () => Task.FromResult(true),
                refresh: null!));
    }
}
