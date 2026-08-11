using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrackDot.Models;
using TrackDot.Tests.Fakes;
using TrackDot.ViewModels;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Unit tests for Feature 10 — Volume / Mute Controls.
/// Verifies Volume, VolumePercent, IsMuted, MuteIconPathData, and volume/mute commands.
/// </summary>
public sealed class VolumeControlTests
{
    [Fact]
    public void Volume_and_Mute_properties_reflect_snapshot()
    {
        var (vm, svc) = BuildViewModel();
        var snapshot = MakeSnapshot(volume: 0.75, isMuted: true);

        svc.Publish(snapshot);

        Assert.Equal(0.75, vm.Volume);
        Assert.Equal(75.0, vm.VolumePercent);
        Assert.True(vm.IsMuted);
        Assert.Equal("Unmute", vm.MuteToolTip);
    }

    [Fact]
    public void Unmuted_snapshot_has_mute_tooltip()
    {
        var (vm, svc) = BuildViewModel();
        var snapshot = MakeSnapshot(volume: 0.5, isMuted: false);

        svc.Publish(snapshot);

        Assert.False(vm.IsMuted);
        Assert.Equal("Mute", vm.MuteToolTip);
    }

    [Fact]
    public void Snapshot_publish_raises_PropertyChanged_for_volume_and_mute()
    {
        var (vm, svc) = BuildViewModel();

        var propertyNames = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
                propertyNames.Add(e.PropertyName);
        };

        svc.Publish(MakeSnapshot(volume: 0.4, isMuted: true));

        Assert.Contains(nameof(MainViewModel.Volume), propertyNames);
        Assert.Contains(nameof(MainViewModel.VolumePercent), propertyNames);
        Assert.Contains(nameof(MainViewModel.IsMuted), propertyNames);
        Assert.Contains(nameof(MainViewModel.MuteIconPathData), propertyNames);
        Assert.Contains(nameof(MainViewModel.MuteToolTip), propertyNames);
    }

    [Fact]
    public async Task SetVolumeCommand_forwards_scaled_volume_to_service()
    {
        var (vm, svc) = BuildViewModel();
        svc.Publish(MakeSnapshot(volume: 1.0, isMuted: false, title: "Active Track"));

        // Passing 50% slider value -> service should receive 0.5
        await Task.Run(() => vm.SetVolumeCommand.Execute(50.0));

        Assert.Equal(1, svc.SetVolumeCallCount);
        Assert.Equal(0.5, svc.LastSetVolume, precision: 4);
    }

    [Fact]
    public async Task ToggleMuteCommand_invokes_service()
    {
        var (vm, svc) = BuildViewModel();
        svc.Publish(MakeSnapshot(volume: 0.8, isMuted: false, title: "Active Track"));

        await Task.Run(() => vm.ToggleMuteCommand.Execute(null));

        Assert.Equal(1, svc.ToggleMuteCallCount);
    }

    private static (MainViewModel Vm, FakeMediaControllerService Svc) BuildViewModel()
    {
        var svc = new FakeMediaControllerService();
        var ticker = new FakeTicker();
        var vm = new MainViewModel(svc, ticker);
        return (vm, svc);
    }

    private static MediaSessionSnapshot MakeSnapshot(double volume, bool isMuted, string title = "Test")
    {
        return new MediaSessionSnapshot(
            SourceAppUserModelId: "com.test.app",
            Title: title,
            Artist: "Artist",
            AlbumTitle: "Album",
            Artwork: null,
            Playback: new PlaybackSnapshot(
                State: MediaPlaybackState.Playing,
                Position: TimeSpan.Zero,
                StartTime: TimeSpan.Zero,
                EndTime: TimeSpan.FromMinutes(3),
                TimelineUpdatedAt: DateTimeOffset.UtcNow,
                Capabilities: new TransportCapabilities(true, true, true, true, true)),
            Volume: volume,
            IsMuted: isMuted);
    }
}
