using System;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Tests for the single-instance mutex guard. Each test uses a
/// fresh, uniquely named mutex so the suite can run repeatedly
/// without interfering with itself or a real TrackDot instance.
/// </summary>
public sealed class SingleInstanceGuardTests
{
    private static string UniqueName(string label)
        => $@"Local\TrackDot.Tests.{label}.{Guid.NewGuid():N}";

    [Fact]
    public void Ctor_acquires_mutex_when_no_other_instance_holds_it()
    {
        using var guard = new SingleInstanceGuard(UniqueName("first"));

        Assert.True(guard.IsAcquired);
    }

    [Fact]
    public void Second_constructor_with_same_name_does_not_acquire()
    {
        var name = UniqueName("dup");

        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
    }

    [Fact]
    public void Disposing_first_guard_lets_a_new_guard_acquire()
    {
        var name = UniqueName("reacquire");

        var first = new SingleInstanceGuard(name);
        Assert.True(first.IsAcquired);
        first.Dispose();

        using var second = new SingleInstanceGuard(name);
        Assert.True(second.IsAcquired);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var guard = new SingleInstanceGuard(UniqueName("idempotent"));
        Assert.True(guard.IsAcquired);

        guard.Dispose();
        guard.Dispose();

        // A third acquire must still succeed: the mutex was released
        // exactly once, regardless of how many Dispose() calls ran.
        using var replacement = new SingleInstanceGuard(UniqueName("idempotent"));
        Assert.True(replacement.IsAcquired);
    }

    [Fact]
    public void Ctor_throws_on_null_or_whitespace_name()
    {
        Assert.Throws<ArgumentException>(() => new SingleInstanceGuard(null!));
        Assert.Throws<ArgumentException>(() => new SingleInstanceGuard(""));
        Assert.Throws<ArgumentException>(() => new SingleInstanceGuard("   "));
    }

    [Fact]
    public void Disposed_guard_reports_IsAcquired_false()
    {
        var guard = new SingleInstanceGuard(UniqueName("disposed-flag"));
        Assert.True(guard.IsAcquired);

        guard.Dispose();

        Assert.False(guard.IsAcquired);
    }
}
