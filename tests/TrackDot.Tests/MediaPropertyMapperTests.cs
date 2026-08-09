using System;
using TrackDot.Models;
using TrackDot.Services;
using Windows.Media.Control;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Tests for <see cref="MediaPropertyMapper"/>. The mapper is a pure
/// static class so every test runs without a WPF dispatcher and
/// without an actual media session.
/// </summary>
/// <remarks>
/// The mapper consumes small data shapes (records) instead of WinRT
/// runtime classes because the SMTC playback-controls object has no
/// public constructor and read-only properties - it cannot be
/// substituted. The <c>MediaControllerService</c> is responsible for
/// projecting SMTC objects into these shapes before calling the
/// mapper.
/// </remarks>
public sealed class MediaPropertyMapperTests
{
    // ---- MapPlaybackStatus ----

    [Fact]
    public void MapPlaybackStatus_returns_a_defined_state_for_every_smts_value()
    {
        // Defensive: SMTC may add new enum values before we update
        // the mapper. Every defined value must map to a defined
        // MediaPlaybackState (the unknown case collapses to None).
        foreach (GlobalSystemMediaTransportControlsSessionPlaybackStatus value
                 in Enum.GetValues<GlobalSystemMediaTransportControlsSessionPlaybackStatus>())
        {
            var mapped = MediaPropertyMapper.MapPlaybackStatus(value);

            Assert.True(Enum.IsDefined(typeof(MediaPlaybackState), mapped),
                $"Mapped state {mapped} should be a defined MediaPlaybackState.");
        }
    }

    [Theory]
    [InlineData(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed,   MediaPlaybackState.Closed)]
    [InlineData(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened,   MediaPlaybackState.Opened)]
    [InlineData(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing, MediaPlaybackState.Changing)]
    [InlineData(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped,  MediaPlaybackState.Stopped)]
    [InlineData(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,  MediaPlaybackState.Playing)]
    [InlineData(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused,   MediaPlaybackState.Paused)]
    public void MapPlaybackStatus_maps_every_smts_status_to_our_state(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus input,
        MediaPlaybackState expected)
    {
        Assert.Equal(expected, MediaPropertyMapper.MapPlaybackStatus(input));
    }

    // ---- MapPlaybackControls ----

    [Fact]
    public void MapPlaybackControls_returns_None_when_controls_is_null()
    {
        // SMTC may report null controls for sessions that don't
        // support transport commands. Must NOT throw.
        var caps = MediaPropertyMapper.MapPlaybackControls(null);

        Assert.Equal(TransportCapabilities.None, caps);
    }

    [Fact]
    public void MapPlaybackControls_copies_every_capability_flag()
    {
        var shape = new MediaPropertyMapper.ControlsShape(
            CanPlay: true,
            CanPause: true,
            CanStop: true,
            CanGoPrevious: true,
            CanGoNext: true);

        var caps = MediaPropertyMapper.MapPlaybackControls(shape);

        Assert.True(caps.CanPlay);
        Assert.True(caps.CanPause);
        Assert.True(caps.CanStop);
        Assert.True(caps.CanGoPrevious);
        Assert.True(caps.CanGoNext);
    }

    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true,  false, false, false, false)]
    [InlineData(false, true,  false, false, false)]
    [InlineData(true,  true,  true,  true,  true)]
    public void MapPlaybackControls_propagates_each_flag_independently(
        bool play, bool pause, bool stop, bool prev, bool next)
    {
        var shape = new MediaPropertyMapper.ControlsShape(
            CanPlay: play,
            CanPause: pause,
            CanStop: stop,
            CanGoPrevious: prev,
            CanGoNext: next);

        var caps = MediaPropertyMapper.MapPlaybackControls(shape);

        Assert.Equal(play,  caps.CanPlay);
        Assert.Equal(pause, caps.CanPause);
        Assert.Equal(stop,  caps.CanStop);
        Assert.Equal(prev,  caps.CanGoPrevious);
        Assert.Equal(next,  caps.CanGoNext);
    }

    // ---- BuildSnapshot ----

    [Fact]
    public void BuildSnapshot_returns_Empty_when_session_is_null()
    {
        // The most important safety guarantee: a null session must
        // never produce a snapshot with null strings. Empty is the
        // single safe default the view model binds against.
        var snapshot = MediaPropertyMapper.BuildSnapshot(
            sessionShape: null,
            mediaProperties: null,
            playbackInfo: null,
            timeline: null,
            artwork: null,
            capturedAt: DateTimeOffset.UtcNow);

        Assert.Equal(MediaSessionSnapshot.Empty, snapshot);
    }

    [Fact]
    public void BuildSnapshot_uses_neutral_strings_when_media_properties_are_null()
    {
        // Session exists but SMTC returned null media properties.
        // Title/artist/album must remain empty strings (never null).
        var session = new MediaPropertyMapper.SessionShape("Spotify.exe");

        var snapshot = MediaPropertyMapper.BuildSnapshot(
            sessionShape: session,
            mediaProperties: null,
            playbackInfo: null,
            timeline: null,
            artwork: null,
            capturedAt: DateTimeOffset.UtcNow);

        Assert.Equal("Spotify.exe", snapshot.SourceAppUserModelId);
        Assert.Equal(string.Empty, snapshot.Title);
        Assert.Equal(string.Empty, snapshot.Artist);
        Assert.Equal(string.Empty, snapshot.AlbumTitle);
    }

    [Fact]
    public void BuildSnapshot_maps_title_artist_album_from_media_properties()
    {
        var session = new MediaPropertyMapper.SessionShape("Spotify.exe");
        var mediaProperties = new MediaPropertyMapper.MediaPropertiesShape(
            Title: "Bohemian Rhapsody",
            Artist: "Queen",
            AlbumTitle: "A Night at the Opera");

        var snapshot = MediaPropertyMapper.BuildSnapshot(
            sessionShape: session,
            mediaProperties: mediaProperties,
            playbackInfo: null,
            timeline: null,
            artwork: null,
            capturedAt: DateTimeOffset.UtcNow);

        Assert.Equal("Bohemian Rhapsody", snapshot.Title);
        Assert.Equal("Queen", snapshot.Artist);
        Assert.Equal("A Night at the Opera", snapshot.AlbumTitle);
        Assert.Equal("Spotify.exe", snapshot.SourceAppUserModelId);
    }

    [Fact]
    public void BuildSnapshot_uses_Empty_when_playback_info_is_null()
    {
        var session = new MediaPropertyMapper.SessionShape("Spotify.exe");
        var mediaProperties = new MediaPropertyMapper.MediaPropertiesShape("T", "A", "Al");

        var snapshot = MediaPropertyMapper.BuildSnapshot(
            sessionShape: session,
            mediaProperties: mediaProperties,
            playbackInfo: null,
            timeline: null,
            artwork: null,
            capturedAt: DateTimeOffset.UtcNow);

        Assert.Equal(PlaybackSnapshot.Empty, snapshot.Playback);
    }

    [Fact]
    public void BuildSnapshot_combines_playback_info_and_timeline_into_PlaybackSnapshot()
    {
        var session = new MediaPropertyMapper.SessionShape("Spotify.exe");
        var mediaProperties = new MediaPropertyMapper.MediaPropertiesShape("T", "A", "Al");
        var playbackInfo = new MediaPropertyMapper.PlaybackInfoShape(
            Status: GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            Controls: new MediaPropertyMapper.ControlsShape(
                CanPlay: false,
                CanPause: true,
                CanStop: true,
                CanGoPrevious: true,
                CanGoNext: true));
        var lastUpdated = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var timeline = new MediaPropertyMapper.TimelineShape(
            Position: TimeSpan.FromSeconds(42),
            StartTime: TimeSpan.Zero,
            EndTime: TimeSpan.FromMinutes(5),
            LastUpdated: lastUpdated);

        var snapshot = MediaPropertyMapper.BuildSnapshot(
            sessionShape: session,
            mediaProperties: mediaProperties,
            playbackInfo: playbackInfo,
            timeline: timeline,
            artwork: null,
            capturedAt: DateTimeOffset.UtcNow);

        Assert.Equal(MediaPlaybackState.Playing, snapshot.Playback.State);
        Assert.Equal(TimeSpan.FromSeconds(42), snapshot.Playback.Position);
        Assert.Equal(TimeSpan.Zero, snapshot.Playback.StartTime);
        Assert.Equal(TimeSpan.FromMinutes(5), snapshot.Playback.EndTime);
        Assert.Equal(lastUpdated, snapshot.Playback.TimelineUpdatedAt);
        Assert.False(snapshot.Playback.Capabilities.CanPlay);
        Assert.True(snapshot.Playback.Capabilities.CanPause);
        Assert.True(snapshot.Playback.Capabilities.CanStop);
        Assert.True(snapshot.Playback.Capabilities.CanGoPrevious);
        Assert.True(snapshot.Playback.Capabilities.CanGoNext);
    }

    [Fact]
    public void BuildSnapshot_uses_capturedAt_when_timeline_is_null_but_playback_exists()
    {
        // The playback info may arrive before the timeline. Use the
        // capturedAt timestamp so the progress interpolator has a
        // valid baseline.
        var session = new MediaPropertyMapper.SessionShape("Spotify.exe");
        var mediaProperties = new MediaPropertyMapper.MediaPropertiesShape("T", "A", "Al");
        var playbackInfo = new MediaPropertyMapper.PlaybackInfoShape(
            Status: GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused,
            Controls: new MediaPropertyMapper.ControlsShape(false, false, false, false, false));
        var capturedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        var snapshot = MediaPropertyMapper.BuildSnapshot(
            sessionShape: session,
            mediaProperties: mediaProperties,
            playbackInfo: playbackInfo,
            timeline: null,
            artwork: null,
            capturedAt: capturedAt);

        Assert.Equal(capturedAt, snapshot.Playback.TimelineUpdatedAt);
        Assert.Equal(MediaPlaybackState.Paused, snapshot.Playback.State);
    }

    [Fact]
    public void BuildSnapshot_drops_controls_to_None_when_session_exposes_no_controls()
    {
        // Some sessions report a playback status but no controls
        // (rare, but observed). The resulting PlaybackSnapshot must
        // still have capabilities = None rather than throwing.
        var session = new MediaPropertyMapper.SessionShape("Spotify.exe");
        var mediaProperties = new MediaPropertyMapper.MediaPropertiesShape("T", "A", "Al");
        var playbackInfo = new MediaPropertyMapper.PlaybackInfoShape(
            Status: GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            Controls: null);

        var snapshot = MediaPropertyMapper.BuildSnapshot(
            sessionShape: session,
            mediaProperties: mediaProperties,
            playbackInfo: playbackInfo,
            timeline: null,
            artwork: null,
            capturedAt: DateTimeOffset.UtcNow);

        Assert.Equal(MediaPlaybackState.Playing, snapshot.Playback.State);
        Assert.Equal(TransportCapabilities.None, snapshot.Playback.Capabilities);
    }
}
