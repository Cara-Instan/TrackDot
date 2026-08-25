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
                Id: 1,
                SyncedLyrics: null,
                PlainLyrics: "Just a gentle breeze carrying memories...",
                TrackName: "Yoru ni Kakeru (English)",
                ArtistName: "YOASOBI",
                AlbumName: null,
                Duration: 261.0),

            new LyricsService.LrclibResponseDto(
                Id: 2,
                SyncedLyrics: "[00:10.00]沈むように溶けてゆくように\n[00:15.00]二人だけの空が広がる夜に",
                PlainLyrics: null,
                TrackName: "夜に駆ける",
                ArtistName: "YOASOBI",
                AlbumName: null,
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

    [Fact]
    public void ParseTtml_ValidTtmlWithSpans_ReturnsSortedTimestampedLines()
    {
        string ttml = """
            <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata">
              <head></head>
              <body dur="3:45.000">
                <div>
                  <p begin="0:15.500" end="0:18.000" ttm:agent="v1">
                    <span begin="0:15.500" end="0:16.200">Hello</span> <span begin="0:16.200" end="0:18.000">world</span>
                  </p>
                  <p begin="0:20.100" end="0:25.000" ttm:agent="v1">
                    <span begin="0:20.100" end="0:25.000">Second line here &amp; more</span>
                  </p>
                </div>
              </body>
            </tt>
            """;

        var lines = LyricsService.ParseTtml(ttml);

        Assert.Equal(2, lines.Count);
        Assert.Equal(new TimeSpan(0, 0, 0, 15, 500), lines[0].Timestamp);
        Assert.Equal("Hello world", lines[0].Text);
        Assert.Equal(new TimeSpan(0, 0, 0, 20, 100), lines[1].Timestamp);
        Assert.Equal("Second line here & more", lines[1].Text);
    }

    [Fact]
    public void ParseLrc_InterleavedTranslationLines_MergesIntoTranslationProperty()
    {
        string lrc = """
            [00:10.00]沈むように溶けてゆくように
            [00:10.00]As if sinking, as if dissolving
            [00:15.00]二人だけの空が広がる夜に // In the night where the sky spreads out just for the two of us
            """;

        var lines = LyricsService.ParseLrc(lrc);

        Assert.Equal(2, lines.Count);
        Assert.Equal("沈むように溶けてゆくように", lines[0].Text);
        Assert.Equal("As if sinking, as if dissolving", lines[0].Translation);
        Assert.Equal(TimeSpan.FromSeconds(10), lines[0].Timestamp);

        Assert.Equal("二人だけの空が広がる夜に", lines[1].Text);
        Assert.Equal("In the night where the sky spreads out just for the two of us", lines[1].Translation);
        Assert.Equal(TimeSpan.FromSeconds(15), lines[1].Timestamp);
    }

    [Theory]
    [InlineData("0:15.500", 15500)]
    [InlineData("3:29.365", 209365)]
    [InlineData("01:02:03.456", 3723456)]
    [InlineData("45.5s", 45500)]
    [InlineData("5000ms", 5000)]
    public void ParseTimestamp_VariousFormats_ParsesCorrectly(string input, int expectedMs)
    {
        var ts = LyricsService.ParseTimestamp(input);
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), ts);
    }

    [Fact]
    public void ParseRawLyrics_AutoDetectsTtmlAndLrcAndPlain()
    {
        string ttml = "<tt><body><div><p begin=\"0:05.00\">TTML lyric</p></div></body></tt>";
        var ttmlResult = LyricsService.ParseRawLyrics(ttml, "ttml");
        Assert.Single(ttmlResult);
        Assert.Equal("TTML lyric", ttmlResult[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(5), ttmlResult[0].Timestamp);

        string lrc = "[00:12.34]LRC lyric line";
        var lrcResult = LyricsService.ParseRawLyrics(lrc, "lrc");
        Assert.Single(lrcResult);
        Assert.Equal("LRC lyric line", lrcResult[0].Text);
        Assert.Equal(new TimeSpan(0, 0, 0, 12, 340), lrcResult[0].Timestamp);

        string plain = "Line 1\nLine 2";
        var plainResult = LyricsService.ParseRawLyrics(plain, "plain");
        Assert.Equal(2, plainResult.Count);
        Assert.Equal("Line 1", plainResult[0].Text);
        Assert.Equal(TimeSpan.Zero, plainResult[0].Timestamp);
        Assert.Equal("Line 2", plainResult[1].Text);
    }
}

