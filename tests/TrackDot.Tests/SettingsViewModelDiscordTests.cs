using System;
using System.Collections.Generic;
using System.Linq;
using TrackDot.Models;
using TrackDot.Services;
using TrackDot.ViewModels;
using Xunit;

namespace TrackDot.Tests;

public class SettingsViewModelDiscordTests
{
    private class FakeStartupService : IStartupService
    {
        public bool IsEnabled { get; set; }
        public void Enable() => IsEnabled = true;
        public void Disable() => IsEnabled = false;
    }

    [Fact]
    public void Properties_ReflectWindowSettingsService()
    {
        var startup = new FakeStartupService { IsEnabled = false };
        var settings = new WindowSettingsService(
            initialEnableDiscordRpc: true,
            initialDiscordShowTimestamps: false,
            initialDiscordShowAlbum: true,
            initialDiscordShowPauseStatus: false,
            initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());

        using var vm = new SettingsViewModel(startup, windowSettings: settings);

        Assert.True(vm.EnableDiscordRpc);
        Assert.False(vm.DiscordShowTimestamps);
        Assert.True(vm.DiscordShowAlbum);
        Assert.False(vm.DiscordShowPauseStatus);
    }

    [Fact]
    public void Setters_UpdateWindowSettingsAndNotify()
    {
        var startup = new FakeStartupService { IsEnabled = false };
        var settings = new WindowSettingsService(
            initialEnableDiscordRpc: false,
            initialDiscordShowTimestamps: true,
            initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());

        using var vm = new SettingsViewModel(startup, windowSettings: settings);

        var changedProps = new List<string>();
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) changedProps.Add(e.PropertyName);
        };

        vm.EnableDiscordRpc = true;
        Assert.True(settings.EnableDiscordRpc);
        Assert.Contains(nameof(vm.EnableDiscordRpc), changedProps);

        vm.DiscordShowTimestamps = false;
        Assert.False(settings.DiscordShowTimestamps);
        Assert.Contains(nameof(vm.DiscordShowTimestamps), changedProps);
    }

    [Fact]
    public void SourceAppItems_PopulateAndToggleUpdatesService()
    {
        var startup = new FakeStartupService { IsEnabled = false };
        var settings = new WindowSettingsService(initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());

        settings.RegisterOrUpdateSourceApp("Spotify.exe", "Spotify");
        settings.RegisterOrUpdateSourceApp("chrome.exe", "Google Chrome");

        using var vm = new SettingsViewModel(startup, windowSettings: settings);

        Assert.Equal(2, vm.SourceAppItems.Count);
        Assert.True(vm.HasSourceApps);

        var spotify = vm.SourceAppItems.First(i => i.Aumid == "Spotify.exe");
        Assert.False(spotify.DiscordRpcEnabled);

        // Toggle in UI
        spotify.DiscordRpcEnabled = true;

        var registered = settings.RegisteredSourceApps;
        var spotifyRegistered = Assert.Single(registered, a => a.Aumid == "Spotify.exe");
        Assert.True(spotifyRegistered.DiscordRpcEnabled);
    }

    [Fact]
    public void ClearSourceApps_EmptiesList()
    {
        var startup = new FakeStartupService { IsEnabled = false };
        var settings = new WindowSettingsService(initialRegisteredSourceApps: Array.Empty<SourceAppSetting>());

        settings.RegisterOrUpdateSourceApp("Spotify.exe", "Spotify");

        using var vm = new SettingsViewModel(startup, windowSettings: settings);
        Assert.Single(vm.SourceAppItems);

        vm.ClearSourceApps();

        Assert.Empty(vm.SourceAppItems);
        Assert.False(vm.HasSourceApps);
        Assert.Empty(settings.RegisteredSourceApps);
    }
}

