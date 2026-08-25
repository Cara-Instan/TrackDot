using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrackDot.Models;
using TrackDot.Services;
using TrackDot.Tests.Fakes;
using TrackDot.ViewModels;
using Xunit;

namespace TrackDot.Tests;

public class LyricsViewModelTests
{
    private class FakeLyricsService : ILyricsService
    {
        public Task<IReadOnlyList<LyricLine>> FetchLyricsAsync(
            string title, string artist, string album = "", TimeSpan duration = default, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<LyricLine> list = new[]
            {
                new LyricLine(0, TimeSpan.FromSeconds(0), "Line 1", "Line 1", new[] { new FuriganaSegment("Line 1", "") }, "Translation 1"),
                new LyricLine(1, TimeSpan.FromSeconds(5), "Line 2", "Line 2", new[] { new FuriganaSegment("Line 2", "") }, "Translation 2"),
            };
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<LyricsSearchResult>> SearchCandidatesAsync(
            string query, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<LyricsSearchResult> results = new[]
            {
                new LyricsSearchResult(101, "Candidate Song", "Candidate Artist", "Candidate Album", TimeSpan.FromMinutes(3), true, true, "LRCLIB")
            };
            return Task.FromResult(results);
        }

        public Task<IReadOnlyList<LyricLine>> FetchLyricsByResultAsync(
            LyricsSearchResult result, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<LyricLine> list = new[]
            {
                new LyricLine(0, TimeSpan.FromSeconds(0), "Searched Line 1", "Searched Line 1", new[] { new FuriganaSegment("Searched Line 1", "") }),
                new LyricLine(1, TimeSpan.FromSeconds(4), "Searched Line 2", "Searched Line 2", new[] { new FuriganaSegment("Searched Line 2", "") }),
            };
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<LyricLine>> ParseCustomLyricsAsync(
            string rawContent, string? format = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<LyricLine> list = new[]
            {
                new LyricLine(0, TimeSpan.FromSeconds(0), "Custom Line 1", "Custom Line 1", new[] { new FuriganaSegment("Custom Line 1", "") }),
                new LyricLine(1, TimeSpan.FromSeconds(3), "Custom Line 2", "Custom Line 2", new[] { new FuriganaSegment("Custom Line 2", "") }),
            };
            return Task.FromResult(list);
        }

        public void SaveLyricsToCache(
            string title, string artist, string album, IReadOnlyList<LyricLine> lyrics)
        {
        }
    }

    [Fact]
    public void LyricsViewModel_InitializesAndUpdatesFontSizeOnResize()
    {
        var mediaSvc = new FakeMediaControllerService();
        var lyricsSvc = new FakeLyricsService();
        var ticker = new FakeTicker();
        var windowSettings = new WindowSettingsService(initialPinned: false, initialOpacity: 100, initialGlobalHotkeys: true, initialLyricsOpacity: 85);

        using var vm = new LyricsViewModel(mediaSvc, lyricsSvc, ticker, windowSettings);

        Assert.Equal(85, vm.OpacityPercent);
        Assert.True(vm.IsTopmost);

        vm.UpdateWindowHeight(440);
        Assert.Equal(20.0, vm.BaseFontSize);
        Assert.Equal(26.0, vm.ActiveFontSize);
    }

    [Fact]
    public void SeekToLineCommand_SeeksMediaControllerPosition()
    {
        var mediaSvc = new FakeMediaControllerService();
        var lyricsSvc = new FakeLyricsService();
        var ticker = new FakeTicker();
        var windowSettings = new WindowSettingsService(initialPinned: false, initialOpacity: 100, initialGlobalHotkeys: true, initialLyricsOpacity: 85);

        using var vm = new LyricsViewModel(mediaSvc, lyricsSvc, ticker, windowSettings);
        var targetLine = new LyricLine(1, TimeSpan.FromSeconds(42), "Target Line", "Target Line", Array.Empty<FuriganaSegment>());

        vm.SeekToLineCommand.Execute(targetLine);

        Assert.Equal(1, mediaSvc.SeekCallCount);
    }

    [Fact]
    public async Task ManualOffset_AdjustsAndResets_Correctly()
    {
        var mediaSvc = new FakeMediaControllerService();
        var lyricsSvc = new FakeLyricsService();
        var ticker = new FakeTicker();
        var windowSettings = new WindowSettingsService(initialPinned: false, initialOpacity: 100, initialGlobalHotkeys: true, initialLyricsOpacity: 85);

        using var vm = new LyricsViewModel(mediaSvc, lyricsSvc, ticker, windowSettings);

        Assert.Equal(0.0, vm.ManualOffsetSeconds);
        Assert.Equal("0.0s", vm.OffsetDisplay);
        Assert.False(vm.HasNonZeroOffset);

        vm.OffsetLaterCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(0.5, vm.ManualOffsetSeconds);
        Assert.Equal("+0.5s", vm.OffsetDisplay);
        Assert.True(vm.HasNonZeroOffset);

        vm.OffsetEarlierCommand.Execute(null);
        await Task.Yield();
        vm.OffsetEarlierCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(-0.5, vm.ManualOffsetSeconds);
        Assert.Equal("-0.5s", vm.OffsetDisplay);

        vm.ResetOffsetCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(0.0, vm.ManualOffsetSeconds);
        Assert.Equal("0.0s", vm.OffsetDisplay);
    }

    [Fact]
    public void DynamicTinting_ExtractsBrushesWhenArtworkPresent()
    {
        var mediaSvc = new FakeMediaControllerService();
        var lyricsSvc = new FakeLyricsService();
        var ticker = new FakeTicker();
        var windowSettings = new WindowSettingsService(
            initialPinned: false,
            initialOpacity: 100,
            initialGlobalHotkeys: true,
            initialLyricsOpacity: 85,
            initialDynamicTinting: true);

        // 8x8 bitmap with vibrant blue
        int width = 8, height = 8, stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 220;     // B
            pixels[i + 1] = 30;  // G
            pixels[i + 2] = 30;  // R
            pixels[i + 3] = 255; // A
        }
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();

        var snapshot = new MediaSessionSnapshot(
            SourceAppUserModelId: "Spotify",
            Title: "Song With Artwork",
            Artist: "Artist With Artwork",
            AlbumTitle: "Album With Artwork",
            Artwork: bitmap,
            Playback: new PlaybackSnapshot(
                State: MediaPlaybackState.Playing,
                Position: TimeSpan.Zero,
                StartTime: TimeSpan.Zero,
                EndTime: TimeSpan.FromMinutes(3),
                TimelineUpdatedAt: DateTimeOffset.UtcNow,
                Capabilities: new TransportCapabilities(true, true, true, true, true)));

        mediaSvc.Publish(snapshot);

        using var vm = new LyricsViewModel(mediaSvc, lyricsSvc, ticker, windowSettings);

        Assert.NotNull(vm.DominantArtworkColor);
        Assert.NotNull(vm.DynamicAccentBrush);
        Assert.NotNull(vm.ArtworkAmbientGlowBrush);
        Assert.True(vm.HasDynamicAccent);

        // Toggle dynamic tinting setting off
        windowSettings.EnableDynamicTinting = false;
        Assert.Null(vm.DynamicAccentBrush);
        Assert.Null(vm.ArtworkAmbientGlowBrush);
        Assert.False(vm.HasDynamicAccent);

        // Toggle back on
        windowSettings.EnableDynamicTinting = true;
        Assert.NotNull(vm.DynamicAccentBrush);
        Assert.NotNull(vm.ArtworkAmbientGlowBrush);
        Assert.True(vm.HasDynamicAccent);
    }

    [Fact]
    public async Task ManualSearch_And_SelectCandidate_UpdatesLyrics()
    {
        var mediaSvc = new FakeMediaControllerService();
        var lyricsSvc = new FakeLyricsService();
        var ticker = new FakeTicker();
        var windowSettings = new WindowSettingsService();

        using var vm = new LyricsViewModel(mediaSvc, lyricsSvc, ticker, windowSettings);

        vm.OpenSearchPanelCommand.Execute(null);
        await Task.Yield();
        Assert.True(vm.IsSearchPanelOpen);

        vm.SearchQuery = "Candidate Song";
        vm.SearchLyricsCommand.Execute(null);
        await Task.Yield();

        Assert.True(vm.HasSearchResults);
        Assert.Single(vm.SearchResults);

        var candidate = vm.SearchResults[0];
        vm.SelectSearchResultCommand.Execute(candidate);
        await Task.Yield();

        Assert.False(vm.IsSearchPanelOpen);
        Assert.Equal(2, vm.Lines.Count);
        Assert.Equal("Searched Line 1", vm.Lines[0].Text);
    }

    [Fact]
    public async Task CustomLyrics_LoadCustomContent_UpdatesLines()
    {
        var mediaSvc = new FakeMediaControllerService();
        var lyricsSvc = new FakeLyricsService();
        var ticker = new FakeTicker();
        var windowSettings = new WindowSettingsService();

        using var vm = new LyricsViewModel(mediaSvc, lyricsSvc, ticker, windowSettings);

        await vm.LoadCustomLyricsAsync("[00:00.00]Custom Line 1\n[00:03.00]Custom Line 2");

        Assert.Equal(2, vm.Lines.Count);
        Assert.Equal("Custom Line 1", vm.Lines[0].Text);
    }

    [Fact]
    public void LyricsHudViewModel_TracksActiveAndNextLine_AndCommands()
    {
        var mediaSvc = new FakeMediaControllerService();
        var lyricsSvc = new FakeLyricsService();
        var ticker = new FakeTicker();
        var windowSettings = new WindowSettingsService(
            initialPinned: false,
            initialOpacity: 100,
            initialGlobalHotkeys: true,
            initialLyricsOpacity: 85,
            initialDynamicTinting: true,
            initialLyricsHudIsLocked: false,
            initialLyricsShowTranslation: true,
            initialLyricsHudShowTranslation: true);

        using var lyricsVm = new LyricsViewModel(mediaSvc, lyricsSvc, ticker, windowSettings);
        using var hudVm = new LyricsHudViewModel(lyricsVm, mediaSvc, windowSettings);

        Assert.False(hudVm.IsLocked);
        hudVm.ToggleLockCommand.Execute(null);
        Assert.True(hudVm.IsLocked);

        double initialFontSize = hudVm.FontSize;
        hudVm.IncreaseFontSizeCommand.Execute(null);
        Assert.Equal(initialFontSize + 2.0, hudVm.FontSize);

        hudVm.DecreaseFontSizeCommand.Execute(null);
        Assert.Equal(initialFontSize, hudVm.FontSize);

        hudVm.ToggleTranslationCommand.Execute(null);
        Assert.False(hudVm.IsTranslationVisible);
    }
}
