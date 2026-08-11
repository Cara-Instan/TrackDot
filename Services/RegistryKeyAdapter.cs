using System;
using Microsoft.Win32;

namespace TrackDot.Services;

/// <summary>
/// Production <see cref="IRegistryKeyFactory"/>. Opens the
/// per-user <c>...\Run</c> key on demand.
/// </summary>
/// <remarks>
/// <para>
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> is
/// the canonical Windows "launch at sign-in" location. Writing
/// here requires no elevation (it's a per-user hive).
/// </para>
/// </remarks>
public sealed class RegistryKeyFactory : IRegistryKeyFactory
{
    /// <summary>
    /// Canonical Windows "launch at sign-in" key path. Same
    /// across Windows 10 / 11. Public so tests can assert the
    /// factory points at the right location.
    /// </summary>
    public const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The value name used for the TrackDot entry.</summary>
    public const string ValueName = "TrackDot";

    /// <inheritdoc/>
    public IRegistryKey OpenRunKey()
    {
        // CurrentUser is always writable without elevation.
        // The "Run" sub-key is created on demand by CreateSubKey
        // — it exists on every modern Windows install but the
        // OpenSubKey path would throw on a fresh user profile
        // in unusual configurations. CreateSubKey is the safe
        // choice.
        var opened = Registry.CurrentUser.CreateSubKey(
            RunKeyPath,
            RegistryKeyPermissionCheck.ReadWriteSubTree);
        if (opened is null)
        {
            throw new InvalidOperationException(
                $"Failed to open or create registry key HKCU\\{RunKeyPath}.");
        }
        return new RegistryKeyAdapter(opened);
    }
}

/// <summary>
/// Production <see cref="IRegistryKey"/> wrapping a live
/// <see cref="RegistryKey"/> handle. Disposed when the adapter
/// is disposed.
/// </summary>
public sealed class RegistryKeyAdapter : IRegistryKey
{
    private RegistryKey? _key;

    internal RegistryKeyAdapter(RegistryKey key)
    {
        _key = key;
    }

    /// <inheritdoc/>
    public string? ReadValue(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_key is null)
        {
            throw new InvalidOperationException(
                "Registry key has been disposed.");
        }
        // GetValue returns null when the value is missing,
        // which is the documented contract on this seam.
        // Casting non-string values to InvalidOperationException
        // keeps the seam's exception surface to one type.
        var raw = _key.GetValue(name);
        return raw switch
        {
            null => null,
            string s => s,
            _ => throw new InvalidOperationException(
                $"Registry value '{name}' is not a string (actual type: {raw.GetType().Name})."),
        };
    }

    /// <inheritdoc/>
    public void WriteValue(string name, string? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_key is null)
        {
            throw new InvalidOperationException(
                "Registry key has been disposed.");
        }
        // RegistryValueKind.String is the Windows-native
        // REG_SZ — what every "Run" entry expects.
        // SetValue(name, null) deletes the value; this is the
        // documented behaviour we want.
        _key.SetValue(name, (object?)value!, RegistryValueKind.String);
    }

    /// <inheritdoc/>
    public void DeleteValue(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_key is null)
        {
            throw new InvalidOperationException(
                "Registry key has been disposed.");
        }
        // throwOnMissingValue: false makes the call a no-op
        // when the value is absent — matches the seam's
        // "idempotent deletion" contract.
        _key.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_key is not null)
        {
            try { _key.Dispose(); } catch { /* swallow — best effort */ }
        }
        _key = null;
    }
}
