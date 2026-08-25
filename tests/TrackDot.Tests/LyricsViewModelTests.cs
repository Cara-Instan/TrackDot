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
}
