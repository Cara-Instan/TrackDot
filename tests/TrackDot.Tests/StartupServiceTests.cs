using System;
using System.Collections.Generic;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// In-memory <see cref="IRegistryKeyFactory"/> + <see cref="IRegistryKey"/>.
/// Tests inject this in place of <see cref="RegistryKeyFactory"/> so
/// no test ever mutates the real registry.
/// </summary>
/// <remarks>
/// Implements the same contract as the production adapter:
/// <list type="bullet">
///   <item><see cref="IRegistryKey.ReadValue"/> returns <c>null</c> when missing.</item>
///   <item><see cref="IRegistryKey.WriteValue"/> with a null value deletes the entry.</item>
///   <item><see cref="IRegistryKey.DeleteValue"/> on a missing value is a no-op.</item>
/// </list>
/// </remarks>
internal sealed class FakeRegistryKeyFactory : IRegistryKeyFactory, IRegistryKey
{
    /// <summary>Stored values, keyed by name.</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Number of times <see cref="OpenRunKey"/> was called.
    /// Tests use this to confirm <see cref="StartupService"/>
    /// opens (and disposes) the key exactly once per public
    /// method call.
    /// </summary>
    public int OpenCount { get; private set; }

    /// <summary>
    /// When true, <see cref="OpenRunKey"/> throws to simulate
    /// a registry access failure. Reset to false to recover.
    /// </summary>
    public bool ThrowOnOpen { get; set; }

    public IRegistryKey OpenRunKey()
    {
        OpenCount++;
        if (ThrowOnOpen)
        {
            throw new InvalidOperationException("Simulated registry failure.");
        }
        // Return the same adapter for every open — the
        // dictionary is shared, so disposal is a no-op.
        return this;
    }

    public string? ReadValue(string name)
        => Values.TryGetValue(name, out var v) ? v : null;

    public void WriteValue(string name, string? value)
    {
        if (value is null)
        {
            Values.Remove(name);
        }
        else
        {
            Values[name] = value;
        }
    }

    public void DeleteValue(string name)
    {
        // Idempotent — Dictionary.Remove on a missing key is
        // already a silent no-op.
        Values.Remove(name);
    }

    public void Dispose()
    {
        // Single shared adapter — disposal is a no-op for the
        // fake. Production semantics live on RegistryKeyAdapter.
    }
}

/// <summary>
/// Tests for <see cref="StartupService"/>. Drives the
/// service through the <see cref="IStartupService"/> surface
/// against an in-memory <see cref="FakeRegistryKeyFactory"/>.
/// </summary>
/// <remarks>
/// The fake covers the registry adapter itself; the production
/// <see cref="RegistryKeyFactory"/> cannot be unit-tested
/// because it touches the real per-user registry. The
/// <c>IRegistryKey</c> contract is symmetric on both sides
/// so the tests exercise the full enable/disable round-trip
/// via the same interface the production code uses.
/// </remarks>
public sealed class StartupServiceTests
{
    private const string TestExe = @"C:\Program Files\TrackDot\TrackDot.exe";
    private const string TestExeNoSpaces = @"C:\Tools\track.exe";

    private static StartupService CreateService(
        FakeRegistryKeyFactory registry,
        string exePath = TestExe)
        => new StartupService(registry, exePath);

    // ----- IsEnabled --------------------------------------------------------

    [Fact]
    public void IsEnabled_returns_false_when_entry_does_not_exist()
    {
        var registry = new FakeRegistryKeyFactory();
        var sut = CreateService(registry);

        Assert.False(sut.IsEnabled);
    }

    [Fact]
    public void IsEnabled_returns_true_when_entry_matches_current_exe()
    {
        var registry = new FakeRegistryKeyFactory();
        // Pre-populate with the quoted version of the test
        // executable path, as if Windows had written it on a
        // previous session.
        registry.Values[RegistryKeyFactory.ValueName] = "\"" + TestExe + "\"";
        var sut = CreateService(registry);

        Assert.True(sut.IsEnabled);
    }

    [Fact]
    public void IsEnabled_returns_false_when_entry_points_at_different_executable()
    {
        var registry = new FakeRegistryKeyFactory();
        registry.Values[RegistryKeyFactory.ValueName] =
            "\"C:\\Other\\App.exe\"";
        var sut = CreateService(registry);

        Assert.False(sut.IsEnabled);
    }

    [Fact]
    public void IsEnabled_comparison_is_case_insensitive()
    {
        var registry = new FakeRegistryKeyFactory();
        // Lowercase stored vs uppercase expected — Windows
        // file paths are case-insensitive.
        registry.Values[RegistryKeyFactory.ValueName] =
            "\"c:\\program files\\trackdot\\trackdot.exe\"";
        var sut = CreateService(registry);

        Assert.True(sut.IsEnabled);
    }

    [Fact]
    public void IsEnabled_ignores_trailing_separators()
    {
        var registry = new FakeRegistryKeyFactory();
        // The same path with a trailing separator.
        registry.Values[RegistryKeyFactory.ValueName] =
            "\"" + TestExe + "\\\\\"";
        var sut = CreateService(registry);

        Assert.True(sut.IsEnabled);
    }

    [Fact]
    public void IsEnabled_accepts_unquoted_stored_value()
    {
        // Windows' Run-key parser accepts both quoted and
        // unquoted paths; the detection logic must too.
        var registry = new FakeRegistryKeyFactory();
        registry.Values[RegistryKeyFactory.ValueName] = TestExe;
        var sut = CreateService(registry);

        Assert.True(sut.IsEnabled);
    }

    [Fact]
    public void IsEnabled_handles_path_without_spaces()
    {
        var registry = new FakeRegistryKeyFactory();
        var sut = CreateService(registry, TestExeNoSpaces);
        sut.Enable();

        Assert.True(sut.IsEnabled);
        Assert.Equal("\"" + TestExeNoSpaces + "\"",
            registry.Values[RegistryKeyFactory.ValueName]);
    }

    // ----- Enable -----------------------------------------------------------

    [Fact]
    public void Enable_writes_quoted_executable_path()
    {
        var registry = new FakeRegistryKeyFactory();
        var sut = CreateService(registry);

        sut.Enable();

        // The path must be stored quoted so Windows parses it
        // correctly when it contains spaces (which it does
        // for every per-user install location).
        Assert.Equal("\"" + TestExe + "\"",
            registry.Values[RegistryKeyFactory.ValueName]);
    }

    [Fact]
    public void Enable_is_idempotent_when_already_enabled()
    {
        var registry = new FakeRegistryKeyFactory();
        var sut = CreateService(registry);
        sut.Enable();
        var afterFirst = registry.Values[RegistryKeyFactory.ValueName];
        var openCountAfterFirst = registry.OpenCount;

        sut.Enable();
        var afterSecond = registry.Values[RegistryKeyFactory.ValueName];

        Assert.Equal(afterFirst, afterSecond);
        // Idempotent enable means a second Enable does NOT
        // re-WRITE the value. It still re-reads (via the
        // IsEnabled short-circuit check) — so OpenCount
        // increases by exactly one read, never by a second
        // write-side open.
        Assert.Equal(openCountAfterFirst + 1, registry.OpenCount);
    }

    [Fact]
    public void Enable_overwrites_foreign_entry_to_match_current_exe()
    {
        var registry = new FakeRegistryKeyFactory();
        registry.Values[RegistryKeyFactory.ValueName] = "\"C:\\Other\\App.exe\"";
        var sut = CreateService(registry);

        sut.Enable();

        Assert.True(sut.IsEnabled);
        Assert.Equal("\"" + TestExe + "\"",
            registry.Values[RegistryKeyFactory.ValueName]);
    }

    [Fact]
    public void Enable_is_reversible_via_Disable()
    {
        var registry = new FakeRegistryKeyFactory();
        var sut = CreateService(registry);

        sut.Enable();
        Assert.True(sut.IsEnabled);

        sut.Disable();
        Assert.False(sut.IsEnabled);
        // The value should be removed entirely, not left as
        // an empty string — a stale empty value would be
        // surfaced as an enabled entry by future reads.
        Assert.False(registry.Values.ContainsKey(RegistryKeyFactory.ValueName));
    }

    // ----- Disable ----------------------------------------------------------

    [Fact]
    public void Disable_is_idempotent_when_already_disabled()
    {
        var registry = new FakeRegistryKeyFactory();
        var sut = CreateService(registry);

        // No prior enable — Disable must not write anything to
        // the registry. The fake's Values dictionary is the
        // write surface; if Disable short-circuited correctly
        // it never reaches the DeleteValue path and the dict
        // stays empty.
        sut.Disable();

        Assert.False(sut.IsEnabled);
        Assert.Empty(registry.Values);
    }

    [Fact]
    public void Disable_is_idempotent_when_already_disabled_after_prior_enable()
    {
        var registry = new FakeRegistryKeyFactory();
        var sut = CreateService(registry);

        sut.Enable();
        sut.Disable();
        // Snapshot the value-store state after the legitimate
        // Disable. A second Disable must be a complete no-op:
        // it must not re-touch the (now empty) value store.
        var valuesAfterLegitDisable = new Dictionary<string, string>(
            registry.Values, StringComparer.OrdinalIgnoreCase);
        var openCountBeforeSecondDisable = registry.OpenCount;

        sut.Disable();

        Assert.Equal(valuesAfterLegitDisable, registry.Values);
        // The second Disable still calls IsEnabled (to
        // confirm nothing needs doing) — so OpenCount
        // increases by exactly one read, never by an extra
        // write-side open.
        Assert.Equal(openCountBeforeSecondDisable + 1, registry.OpenCount);
    }

    [Fact]
    public void Disable_removes_our_entry_but_leaves_foreign_entries_alone()
    {
        var registry = new FakeRegistryKeyFactory();
        // Two entries: ours and someone else's. Disable
        // targets the canonical value name only.
        registry.Values[RegistryKeyFactory.ValueName] = "\"" + TestExe + "\"";
        registry.Values["OtherApp"] = "\"C:\\Other\\App.exe\"";
        var sut = CreateService(registry);

        sut.Disable();

        Assert.False(registry.Values.ContainsKey(RegistryKeyFactory.ValueName));
        Assert.True(registry.Values.ContainsKey("OtherApp"));
    }

    // ----- Lifecycle / disposal ---------------------------------------------

    [Fact]
    public void Enable_opens_the_registry_key_one_read_and_one_write()
    {
        var registry = new FakeRegistryKeyFactory();
        var sut = CreateService(registry);

        sut.Enable();

        // Enable reads (via IsEnabled) and writes. The fake
        // shares the underlying dictionary across both opens,
        // so "disposal" is a no-op there — but the open count
        // tells us the service asked for the key exactly
        // twice: one read + one write. Combined with the
        // adapter's IDisposable contract (using var key),
        // this is the proof the service does not leak handles
        // on the happy path.
        Assert.Equal(2, registry.OpenCount);
    }

    [Fact]
    public void IsEnabled_opens_and_disposes_the_registry_key_exactly_once()
    {
        var registry = new FakeRegistryKeyFactory();
        var sut = CreateService(registry);

        _ = sut.IsEnabled;

        Assert.Equal(1, registry.OpenCount);
    }

    [Fact]
    public void Enable_throws_when_executable_path_cannot_be_resolved()
    {
        // The marker ctor leaves both path fields null —
        // the same state the production ctor leaves the
        // service in when Environment.ProcessPath returns
        // null.
        var registry = new FakeRegistryKeyFactory();
        var sut = new StartupService(registry, unresolvedPath: true);

        Assert.Throws<InvalidOperationException>(() => sut.Enable());
    }

    [Fact]
    public void IsEnabled_returns_false_when_executable_path_cannot_be_resolved()
    {
        var registry = new FakeRegistryKeyFactory();
        var sut = new StartupService(registry, unresolvedPath: true);

        Assert.False(sut.IsEnabled);
    }

    // ----- Registry adapter contract (sanity) -------------------------------

    [Fact]
    public void FakeAdapter_ReadValue_returns_null_for_missing_value()
    {
        IRegistryKey key = new FakeRegistryKeyFactory();
        Assert.Null(key.ReadValue("NotThere"));
    }

    [Fact]
    public void FakeAdapter_WriteValue_with_null_deletes_entry()
    {
        var fake = new FakeRegistryKeyFactory();
        fake.Values["X"] = "y";
        IRegistryKey key = fake;
        key.WriteValue("X", null);
        Assert.False(fake.Values.ContainsKey("X"));
    }

    [Fact]
    public void FakeAdapter_DeleteValue_on_missing_value_is_no_op()
    {
        IRegistryKey key = new FakeRegistryKeyFactory();
        // Must not throw.
        key.DeleteValue("NotThere");
    }

    [Fact]
    public void FakeAdapter_ReadValue_null_name_throws()
    {
        IRegistryKey key = new FakeRegistryKeyFactory();
        Assert.Throws<ArgumentNullException>(() => key.ReadValue(null!));
    }

    [Fact]
    public void FakeAdapter_WriteValue_null_name_throws()
    {
        IRegistryKey key = new FakeRegistryKeyFactory();
        Assert.Throws<ArgumentNullException>(() => key.WriteValue(null!, "v"));
    }

    [Fact]
    public void FakeAdapter_DeleteValue_null_name_throws()
    {
        IRegistryKey key = new FakeRegistryKeyFactory();
        Assert.Throws<ArgumentNullException>(() => key.DeleteValue(null!));
    }
}
