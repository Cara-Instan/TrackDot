using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrackDot.Models;

namespace TrackDot.Services;

/// <summary>
/// UI-facing facade for SMTC. The implementation owns the WinRT
/// session manager, event subscriptions, and snapshot publishing; the
/// view model listens via <see cref="SnapshotChanged"/> and reads
/// <see cref="Current"/>.
/// </summary>
/// <remarks>
/// All members are safe to call from the WPF UI thread. The service
/// marshals WinRT callbacks to the UI thread internally before raising
/// <see cref="SnapshotChanged"/>.
/// </remarks>
public interface IMediaControllerService : IAsyncDisposable
{
    /// <summary>
    /// The most recently published snapshot. Always non-null; defaults
    /// to <see cref="MediaSessionSnapshot.Empty"/> until
    /// <see cref="InitializeAsync"/> has run.
    /// </summary>
    MediaSessionSnapshot Current { get; }

    /// <summary>
    /// Raised whenever a new authoritative snapshot is available.
    /// Subscribers run on the WPF dispatcher thread.
    /// </summary>
    event EventHandler<MediaSessionSnapshot>? SnapshotChanged;

    // ── Feature 9 — Multi-Session Picker ────────────────────────────────

    /// <summary>
    /// The live list of all SMTC sessions known to the system, in the
    /// order returned by <c>GetSessions()</c>. Always non-null;
    /// defaults to an empty list until <see cref="InitializeAsync"/>
    /// has run. The item whose <c>IsCurrent</c> flag is true is the
    /// session that produced <see cref="Current"/>.
    /// </summary>
    IReadOnlyList<MediaSessionInfo> AvailableSessions { get; }

    /// <summary>
    /// Raised on the WPF dispatcher thread whenever the set of
    /// available sessions changes (sources opened, closed, or
    /// reordered). Subscribers should re-read
    /// <see cref="AvailableSessions"/> in response.
    /// </summary>
    event EventHandler? SessionListChanged;

    /// <summary>
    /// Pins the session identified by <paramref name="sourceAppUserModelId"/>
    /// as the active session. The popover will show that session's
    /// metadata until the user picks another or the session closes.
    /// Returns immediately if no matching session is found.
    /// </summary>
    Task SelectSessionAsync(string sourceAppUserModelId);

    // ── Feature 10 — Volume / Mute Controls ─────────────────────────────

    /// <summary>
    /// Sets the master volume of the audio session that belongs to the
    /// current SMTC source application. <paramref name="volume"/> must
    /// be in [0.0, 1.0]. No-op when no CoreAudio session can be
    /// matched, or when there is no active SMTC session.
    /// </summary>
    Task SetVolumeAsync(double volume);

    /// <summary>
    /// Toggles the mute state of the audio session that belongs to the
    /// current SMTC source application. No-op when no CoreAudio
    /// session can be matched.
    /// </summary>
    Task ToggleMuteAsync();

    /// <summary>
    /// Refreshes the volume and mute state for the active session from CoreAudio
    /// and publishes an updated snapshot.
    /// </summary>
    Task RefreshVolumeAsync();

    // ── Transport ────────────────────────────────────────────────────────

    /// <summary>
    /// Asynchronously acquires the system media session manager and
    /// subscribes to the current session. Subsequent calls are no-ops
    /// while the service is still initialized.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends play or pause to the active session, choosing the right
    /// command based on the current playback state. Returns
    /// immediately if there is no session or the capability is
    /// unsupported.
    /// </summary>
    Task TogglePlayPauseAsync();

    /// <summary>
    /// Sends "previous track" to the active session. Returns
    /// immediately if unsupported.
    /// </summary>
    Task PreviousAsync();

    /// <summary>
    /// Sends "stop" to the active session. Returns immediately if
    /// unsupported.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Sends "next track" to the active session. Returns immediately
    /// if unsupported.
    /// </summary>
    Task NextAsync();

    /// <summary>
    /// Seeks to <paramref name="positionSeconds"/> in the active session.
    /// Returns immediately if there is no session or seeking is not supported.
    /// </summary>
    /// <param name="positionSeconds">Target playback position in seconds.</param>
    Task SeekAsync(double positionSeconds);
}
