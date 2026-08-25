using System;
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
                new LyricLine(0, TimeSpan.FromSeconds(0), "Line 1", "Line 1", new[] { new FuriganaSegment("Line 1", "") }),
                new LyricLine(1, TimeSpan.FromSeconds(5), "Line 2", "Line 2", new[] { new FuriganaSegment("Line 2", "") }),
            };
            return Task.FromResult(list);
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
}
