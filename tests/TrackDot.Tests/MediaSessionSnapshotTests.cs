using System;
using System.Collections.Generic;
using TrackDot.Models;
using Xunit;

namespace TrackDot.Tests;

public sealed class MediaSessionSnapshotTests
{
    [Fact]
    public void Empty_uses_neutral_strings()
    {
        var empty = MediaSessionSnapshot.Empty;

        Assert.Equal(string.Empty, empty.Title);
        Assert.Equal(string.Empty, empty.Artist);
        Assert.Equal(string.Empty, empty.AlbumTitle);
        Assert.Null(empty.SourceAppUserModelId);
    }

    [Fact]
    public void Empty_has_no_artwork()
    {
        var empty = MediaSessionSnapshot.Empty;

        Assert.Null(empty.Artwork);
    }

    [Fact]
    public void Empty_has_zero_timeline_in_neutral_state()
    {
        var empty = MediaSessionSnapshot.Empty;

        Assert.Equal(MediaPlaybackState.None, empty.Playback.State);
        Assert.Equal(TimeSpan.Zero, empty.Playback.Position);
        Assert.Equal(TimeSpan.Zero, empty.Playback.StartTime);
        Assert.Equal(TimeSpan.Zero, empty.Playback.EndTime);
        Assert.Equal(DateTimeOffset.MinValue, empty.Playback.TimelineUpdatedAt);
    }

    [Fact]
    public void Empty_disables_every_transport_capability()
    {
        var empty = MediaSessionSnapshot.Empty;

        Assert.False(empty.Playback.Capabilities.CanPlay);
        Assert.False(empty.Playback.Capabilities.CanPause);
        Assert.False(empty.Playback.Capabilities.CanStop);
        Assert.False(empty.Playback.Capabilities.CanGoPrevious);
        Assert.False(empty.Playback.Capabilities.CanGoNext);
    }

    [Fact]
    public void Empty_is_safe_to_construct_view_state_from()
    {
        // Empty must be usable as a single safe default without
        // null-reference checks anywhere in the view model layer.
        var empty = MediaSessionSnapshot.Empty;

        var title = empty.Title;          // not null
        var artist = empty.Artist;        // not null
        var album = empty.AlbumTitle;     // not null
        var artwork = empty.Artwork;      // null - VM decides fallback
        var playback = empty.Playback;    // not null

        Assert.NotNull(playback);
        Assert.NotNull(title);
        Assert.NotNull(artist);
        Assert.NotNull(album);
    }

    [Fact]
    public void Records_are_immutable_after_construction()
    {
        var capabilities = new TransportCapabilities(true, true, true, true, true);
        var snapshot = new MediaSessionSnapshot(
            SourceAppUserModelId: "Spotify.exe",
            Title: "Track",
            Artist: "Artist",
            AlbumTitle: "Album",
            Artwork: null,
            Playback: new PlaybackSnapshot(
                State: MediaPlaybackState.Playing,
                Position: TimeSpan.FromSeconds(15),
                StartTime: TimeSpan.Zero,
                EndTime: TimeSpan.FromMinutes(3),
                TimelineUpdatedAt: DateTimeOffset.UtcNow,
                Capabilities: capabilities));

        // Same values after construction (records are immutable).
        Assert.Equal("Spotify.exe", snapshot.SourceAppUserModelId);
        Assert.Equal("Track", snapshot.Title);
        Assert.Equal(TimeSpan.FromSeconds(15), snapshot.Playback.Position);
        Assert.True(snapshot.Playback.Capabilities.CanPlay);
    }

    [Fact]
    public void Empty_PlaybackSnapshot_matches_Empty_MediaSessionSnapshot()
    {
        var playback = PlaybackSnapshot.Empty;
        var snapshot = MediaSessionSnapshot.Empty;

        Assert.Equal(playback, snapshot.Playback);
    }

    [Theory]
    [InlineData(true,  true,  true,  true,  true)]
    [InlineData(false, false, false, false, false)]
    [InlineData(true,  false, true,  false, false)]
    public void TransportCapabilities_stores_each_capability(bool play, bool pause, bool stop, bool prev, bool next)
    {
        var caps = new TransportCapabilities(play, pause, stop, prev, next);

        Assert.Equal(play, caps.CanPlay);
        Assert.Equal(pause, caps.CanPause);
        Assert.Equal(stop, caps.CanStop);
        Assert.Equal(prev, caps.CanGoPrevious);
        Assert.Equal(next, caps.CanGoNext);
    }

    [Fact]
    public void TransportCapabilities_None_disables_every_capability()
    {
        var none = TransportCapabilities.None;

        Assert.False(none.CanPlay);
        Assert.False(none.CanPause);
        Assert.False(none.CanStop);
        Assert.False(none.CanGoPrevious);
        Assert.False(none.CanGoNext);
    }

    [Fact]
    public void Empty_is_assignable_through_default_keyword()
    {
        // Records are reference types; default(MediaSessionSnapshot) is
        // not the same as Empty (different fields), but Empty must
        // still be usable as the default in nullable code paths.
        MediaSessionSnapshot? maybe = null;
        var resolved = maybe ?? MediaSessionSnapshot.Empty;

        Assert.Equal(MediaSessionSnapshot.Empty, resolved);
    }

    [Fact]
    public void PlaybackSnapshot_Empty_has_disabled_capabilities()
    {
        var empty = PlaybackSnapshot.Empty;

        Assert.Equal(TransportCapabilities.None, empty.Capabilities);
        Assert.Equal(MediaPlaybackState.None, empty.State);
    }
}
