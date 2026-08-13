using System;
using System.Threading.Tasks;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

public class LyricsServiceTests
{
    [Fact]
    public async Task FetchLyricsAsync_EmptyTitle_ReturnsEmptyList()
    {
        var service = new LyricsService();
        var lyrics = await service.FetchLyricsAsync(title: "", artist: "Test Artist");
        Assert.Empty(lyrics);
    }

    [Fact]
    public void SelectBestLyricsMatch_PrefersJapaneseSyncedLyricsOverEnglishPlain()
    {
        var candidates = new[]
        {
            new LyricsService.LrclibResponseDto(
                SyncedLyrics: null,
                PlainLyrics: "Just a gentle breeze carrying memories...",
                TrackName: "Yoru ni Kakeru (English)",
                ArtistName: "YOASOBI",
                Duration: 261.0),

            new LyricsService.LrclibResponseDto(
                SyncedLyrics: "[00:10.00]沈むように溶けてゆくように\n[00:15.00]二人だけの空が広がる夜に",
                PlainLyrics: null,
                TrackName: "夜に駆ける",
                ArtistName: "YOASOBI",
                Duration: 261.0)
        };

        var best = LyricsService.SelectBestLyricsMatch(
            candidates,
            queryTitle: "夜に駆ける",
            queryArtist: "YOASOBI",
            targetDuration: TimeSpan.FromSeconds(261));

        Assert.NotNull(best);
        Assert.Equal("夜に駆ける", best.TrackName);
        Assert.NotNull(best.SyncedLyrics);
    }
}
