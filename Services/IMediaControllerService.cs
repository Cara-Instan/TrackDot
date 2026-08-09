using System;
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
}
