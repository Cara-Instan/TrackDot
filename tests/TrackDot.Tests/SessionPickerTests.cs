using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrackDot.Models;
using TrackDot.Tests.Fakes;
using TrackDot.ViewModels;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Unit tests for Feature 9 — Multi-Session Picker.
/// Verifies session list publishing, HasMultipleSessions toggle logic,
/// and SelectSessionCommand behavior on the MainViewModel / FakeMediaControllerService seam.
/// </summary>
public sealed class SessionPickerTests
{
    [Fact]
    public void No_sessions_HasMultipleSessions_is_false()
    {
        var (vm, svc) = BuildViewModel();
        Assert.Empty(vm.AvailableSessions);
        Assert.False(vm.HasMultipleSessions);
    }

    [Fact]
    public void Single_session_HasMultipleSessions_is_false()
    {
        var (vm, svc) = BuildViewModel();
        svc.PublishSessionList(new[]
        {
            new MediaSessionInfo("com.spotify.client", "Spotify", true)
        });

        Assert.Single(vm.AvailableSessions);
        Assert.False(vm.HasMultipleSessions);
    }

    [Fact]
    public void Two_sessions_HasMultipleSessions_is_true()
    {
        var (vm, svc) = BuildViewModel();
        svc.PublishSessionList(new[]
        {
            new MediaSessionInfo("com.spotify.client", "Spotify", true),
            new MediaSessionInfo("chrome.exe", "Google Chrome", false)
        });

        Assert.Equal(2, vm.AvailableSessions.Count);
        Assert.True(vm.HasMultipleSessions);
    }

    [Fact]
    public void SessionListChanged_raises_PropertyChanged_for_sessions_and_multiple_flag()
    {
        var (vm, svc) = BuildViewModel();

        var propertyNames = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
                propertyNames.Add(e.PropertyName);
        };

        svc.PublishSessionList(new[]
        {
            new MediaSessionInfo("com.spotify.client", "Spotify", true),
            new MediaSessionInfo("chrome.exe", "Google Chrome", false)
        });

        Assert.Contains(nameof(MainViewModel.AvailableSessions), propertyNames);
        Assert.Contains(nameof(MainViewModel.HasMultipleSessions), propertyNames);
    }

    [Fact]
    public async Task SelectSessionCommand_forwards_aumid_to_service()
    {
        var (vm, svc) = BuildViewModel();
        svc.PublishSessionList(new[]
        {
            new MediaSessionInfo("com.spotify.client", "Spotify", true),
            new MediaSessionInfo("chrome.exe", "Google Chrome", false)
        });

        await Task.Run(() => vm.SelectSessionCommand.Execute("chrome.exe"));

        Assert.Equal(1, svc.SelectSessionCallCount);
        Assert.Equal("chrome.exe", svc.LastSelectedAumid);
    }

    private static (MainViewModel Vm, FakeMediaControllerService Svc) BuildViewModel()
    {
        var svc = new FakeMediaControllerService();
        var ticker = new FakeTicker();
        var vm = new MainViewModel(svc, ticker);
        return (vm, svc);
    }
}
