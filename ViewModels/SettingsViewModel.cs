using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrackDot.Services;

namespace TrackDot.ViewModels;

/// <summary>
/// Settings window view-model. Exposes the launch-at-sign-in
/// toggle, mirrors it to <see cref="IStartupService.IsEnabled"/>,
/// and persists immediately on every toggle.
/// </summary>
/// <remarks>
/// <para>
/// "Save immediately on toggle" is the chosen UX (Task 10 plan
/// §4 — the plan explicitly says "choose one and test it").
/// The alternative ("Apply" button) requires a separate
/// dirty-tracking path and a second source-of-truth field;
/// for a single-checkbox dialog the explicit-save model adds
/// complexity without benefit.
/// </para>
/// <para>
/// If <see cref="IStartupService.Enable"/> throws (e.g. the
/// executable path cannot be resolved), the toggle is rolled
/// back to its previous value. This keeps the view-model in
/// sync with the registry state — a stale checkbox that
/// claims "on" while the registry is off is worse than a
/// click that visibly failed.
/// </para>
/// </remarks>
public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IStartupService _startup;
    private bool _disposed;

    /// <summary>
    /// Backing field for <see cref="LaunchAtSignIn"/>. Set
    /// from <see cref="IStartupService.IsEnabled"/> at
    /// construction; mutated by the property setter which
    /// also persists to the registry.
    /// </summary>
    private bool _launchAtSignIn;

    /// <summary>
    /// Diagnostic message shown in the window footer.
    /// Non-empty only when a save attempt failed; otherwise
    /// the empty string. Surfaced so the user can see *why*
    /// their click did nothing.
    /// </summary>
    private string _statusMessage = string.Empty;

    /// <summary>
    /// True when TrackDot is registered to launch at
    /// sign-in. Toggling the property writes the change to
    /// the per-user <c>...\Run</c> registry key immediately.
    /// </summary>
    public bool LaunchAtSignIn
    {
        get => _launchAtSignIn;
        set
        {
            if (_launchAtSignIn == value) return;
            _launchAtSignIn = value;
            OnPropertyChanged();

            // Persist immediately. If Enable throws
            // (path-unresolved etc.) the registry state is
            // unchanged and we must roll back the field so
            // the checkbox matches reality.
            try
            {
                if (value) _startup.Enable();
                else _startup.Disable();
                StatusMessage = string.Empty;
            }
            catch (Exception ex)
            {
                _launchAtSignIn = !value;
                OnPropertyChanged();
                StatusMessage = ex.Message;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
    }

    /// <summary>
    /// Non-empty when the most recent toggle failed to
    /// persist to the registry. The window binds this to a
    /// footer label so the user knows their click did
    /// nothing.
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The value name used for the Run-key entry. Surfaced
    /// as a read-only display string so the user can locate
    /// the entry in regedit if they want to verify the
    /// write.
    /// </summary>
    public string RegistryValueName => RegistryKeyFactory.ValueName;

    /// <summary>
    /// The registry path used for the Run-key entry. See
    /// <see cref="RegistryValueName"/>.
    /// </summary>
    public string RegistryKeyPath => @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Constructs a view-model bound to the supplied startup service.</summary>
    public SettingsViewModel(IStartupService startup)
    {
        ArgumentNullException.ThrowIfNull(startup);
        _startup = startup;
        _launchAtSignIn = startup.IsEnabled;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // No subscriptions to release — the startup service
        // is owned by the composition root and torn down there.
    }
}