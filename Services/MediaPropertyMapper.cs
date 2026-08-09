using System;
using System.Windows.Media;
using TrackDot.Models;
using Windows.Media.Control;

namespace TrackDot.Services;

/// <summary>
/// Pure mappings from SMTC platform types into the UI-facing
/// <see cref="MediaSessionSnapshot"/> contract. Kept static and
/// side-effect free so the unit tests can exercise every code path
/// without a WPF dispatcher or a live SMTC session.
/// </summary>
/// <remarks>
/// The mapper intentionally takes small data shapes (records) rather
/// than the live WinRT runtime classes. The runtime classes
/// (<c>GlobalSystemMediaTransportControlsSessionPlaybackControls</c>,
/// <c>GlobalSystemMediaTransportControlsSessionMediaProperties</c>,
/// etc.) have no public constructors and read-only properties, so
/// they cannot be substituted in tests. The service projects SMTC
/// objects into these shapes inside the generation-guarded closure
/// before calling the mapper.
/// </remarks>
public static class MediaPropertyMapper
{
    /// <summary>
    /// Source-app identity. Only the AUMID is captured here; media
    /// properties live on <see cref="MediaPropertiesShape"/> and are
    /// fetched independently because they require an async call.
    /// </summary>
    public sealed record SessionShape(string SourceAppUserModelId);

    /// <summary>
    /// Title/artist/album as reported by SMTC's media properties.
    /// </summary>
    public sealed record MediaPropertiesShape(string Title, string Artist, string AlbumTitle);

    /// <summary>
    /// Coarse-grained playback status plus the transport controls
    /// the session currently exposes. Combines the SMTC playback
    /// info and its <c>Controls</c> member into a single snapshot.
    /// </summary>
    public sealed record PlaybackInfoShape(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus Status,
        ControlsShape? Controls);

    /// <summary>
    /// Five transport-relevant flags from SMTC's playback controls,
    /// flattened into a record so the mapper and its tests never
    /// touch the WinRT runtime class.
    /// </summary>
    public sealed record ControlsShape(
        bool CanPlay,
        bool CanPause,
        bool CanStop,
        bool CanGoPrevious,
        bool CanGoNext);

    /// <summary>
    /// Timeline projection. All three TimeSpans plus a LastUpdated
    /// timestamp so the progress interpolator can baseline against
    /// when SMTC last reported.
    /// </summary>
    public sealed record TimelineShape(
        TimeSpan Position,
        TimeSpan StartTime,
        TimeSpan EndTime,
        DateTimeOffset LastUpdated);

    /// <summary>
    /// Maps an SMTC playback status into our coarser
    /// <see cref="MediaPlaybackState"/>. One-to-one for every
    /// currently-defined SMTC value; unknown values collapse to
    /// <see cref="MediaPlaybackState.None"/>.
    /// </summary>
    public static MediaPlaybackState MapPlaybackStatus(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
        => status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed   => MediaPlaybackState.Closed,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened   => MediaPlaybackState.Opened,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackState.Changing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped  => MediaPlaybackState.Stopped,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing  => MediaPlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused   => MediaPlaybackState.Paused,
            _ => MediaPlaybackState.None,
        };

    /// <summary>
    /// Copies the five transport-relevant flags from the controls
    /// shape into our <see cref="TransportCapabilities"/> record.
    /// </summary>
    /// <remarks>
    /// SMTC may report a null controls object for sessions that do
    /// not support transport commands (rare, but possible). In that
    /// case we return <see cref="TransportCapabilities.None"/>
    /// rather than throwing.
    /// </remarks>
    public static TransportCapabilities MapPlaybackControls(ControlsShape? controls)
    {
        if (controls is null)
        {
            return TransportCapabilities.None;
        }

        return new TransportCapabilities(
            CanPlay:       controls.CanPlay,
            CanPause:      controls.CanPause,
            CanStop:       controls.CanStop,
            CanGoPrevious: controls.CanGoPrevious,
            CanGoNext:     controls.CanGoNext);
    }

    /// <summary>
    /// Assembles a <see cref="MediaSessionSnapshot"/> from the SMTC
    /// pieces captured during a refresh. Every parameter except
    /// <paramref name="capturedAt"/> may be null; a null session
    /// short-circuits to <see cref="MediaSessionSnapshot.Empty"/>.
    /// </summary>
    /// <param name="sessionShape">
    /// Source-app identity. Null => no active session => Empty.
    /// </param>
    /// <param name="mediaProperties">
    /// Title/artist/album. Null => empty strings, not null references.
    /// </param>
    /// <param name="playbackInfo">
    /// Playback status + controls. Null => Empty playback snapshot.
    /// </param>
    /// <param name="timeline">
    /// Timeline. Null => zero durations but <paramref name="capturedAt"/>
    /// is still used as the timeline baseline.
    /// </param>
    /// <param name="artwork">
    /// Already-decoded, frozen <see cref="ImageSource"/> or null.
    /// Task 4 (<c>ThumbnailDecoder</c>) owns the decode pipeline.
    /// </param>
    /// <param name="capturedAt">
    /// Monotonic timestamp recorded when this set of properties was
    /// read. Used as <see cref="PlaybackSnapshot.TimelineUpdatedAt"/>.
    /// </param>
    public static MediaSessionSnapshot BuildSnapshot(
        SessionShape? sessionShape,
        MediaPropertiesShape? mediaProperties,
        PlaybackInfoShape? playbackInfo,
        TimelineShape? timeline,
        ImageSource? artwork,
        DateTimeOffset capturedAt)
    {
        if (sessionShape is null)
        {
            return MediaSessionSnapshot.Empty;
        }

        var props = mediaProperties
            ?? new MediaPropertiesShape(string.Empty, string.Empty, string.Empty);

        var playback = BuildPlaybackSnapshot(playbackInfo, timeline, capturedAt);

        return new MediaSessionSnapshot(
            SourceAppUserModelId: sessionShape.SourceAppUserModelId,
            Title:                props.Title,
            Artist:               props.Artist,
            AlbumTitle:           props.AlbumTitle,
            Artwork:              artwork,
            Playback:             playback);
    }

    /// <summary>
    /// Builds the playback half of a snapshot from the playback and
    /// timeline shapes. Exposed (rather than kept private) so
    /// <c>MediaControllerService</c> can rebuild the playback side
    /// when only one of the two pieces has changed (e.g. a
    /// <c>PlaybackInfoChanged</c> arrives without a fresh timeline).
    /// </summary>
    public static PlaybackSnapshot BuildPlaybackSnapshot(
        PlaybackInfoShape? playbackInfo,
        TimelineShape? timeline,
        DateTimeOffset capturedAt)
    {
        if (playbackInfo is null)
        {
            return PlaybackSnapshot.Empty;
        }

        var position  = timeline?.Position  ?? TimeSpan.Zero;
        var startTime = timeline?.StartTime ?? TimeSpan.Zero;
        var endTime   = timeline?.EndTime   ?? TimeSpan.Zero;

        // Prefer the timeline's own LastUpdated when we have it; the
        // progress interpolator (Task 6) needs the most accurate
        // baseline possible. Fall back to the caller's capturedAt
        // when timeline is null (status arrived before timeline).
        var timelineUpdatedAt = timeline?.LastUpdated is { } ts && ts != default
            ? ts
            : capturedAt;

        return new PlaybackSnapshot(
            State:              MapPlaybackStatus(playbackInfo.Status),
            Position:           position,
            StartTime:          startTime,
            EndTime:            endTime,
            TimelineUpdatedAt:  timelineUpdatedAt,
            Capabilities:       MapPlaybackControls(playbackInfo.Controls));
    }
}
