using System;
using TrackDot.Models;
using TrackDot.Services;
using TrackDot.ViewModels;
using Xunit;

namespace TrackDot.Tests;

[Collection("PortableMode")]
public class SettingsViewModelTests
{
    private class FakeStartupService : IStartupService
    {
        public bool IsEnabled { get; set; }
        public void Enable() => IsEnabled = true;
        public void Disable() => IsEnabled = false;
    }

    [Fact]
    public void ThemeSelection_UpdatesSelectedThemeAndHelperProperties()
    {
        using var themeService = new ThemeService();
        var startupService = new FakeStartupService();
        using var sut = new SettingsViewModel(startupService, themeService);

        sut.IsDarkTheme = true;
        Assert.Equal(AppThemeMode.Dark, sut.SelectedTheme);
        Assert.True(sut.IsDarkTheme);
        Assert.False(sut.IsLightTheme);
        Assert.False(sut.IsSystemTheme);

        sut.IsLightTheme = true;
        Assert.Equal(AppThemeMode.Light, sut.SelectedTheme);
        Assert.True(sut.IsLightTheme);
        Assert.False(sut.IsDarkTheme);
        Assert.False(sut.IsSystemTheme);

        sut.IsSystemTheme = true;
        Assert.Equal(AppThemeMode.System, sut.SelectedTheme);
        Assert.True(sut.IsSystemTheme);
    }

    [Fact]
    public void OpacityPercent_UpdatesWindowSettingsServiceAndDisplayText()
    {
        using var themeService = new ThemeService();
        var startupService = new FakeStartupService();
        var windowSettings = new WindowSettingsService(initialOpacity: 100);
        using var sut = new SettingsViewModel(startupService, themeService, windowSettings);

        Assert.Equal(100, sut.OpacityPercent);
        Assert.Equal("100%", sut.OpacityDisplayText);

        sut.OpacityPercent = 85;
        Assert.Equal(85, windowSettings.OpacityPercent);
        Assert.Equal(0.85, windowSettings.WindowOpacity, 2);
        Assert.Equal("85%", sut.OpacityDisplayText);
    }

    [Fact]
    public void EnableGlobalHotkeys_UpdatesWindowSettingsService()
    {
        using var themeService = new ThemeService();
        var startupService = new FakeStartupService();
        var windowSettings = new WindowSettingsService(initialGlobalHotkeys: true);
        using var sut = new SettingsViewModel(startupService, themeService, windowSettings);

        Assert.True(sut.EnableGlobalHotkeys);

        sut.EnableGlobalHotkeys = false;
        Assert.False(windowSettings.EnableGlobalHotkeys);
        Assert.False(sut.EnableGlobalHotkeys);

        sut.EnableGlobalHotkeys = true;
        Assert.True(windowSettings.EnableGlobalHotkeys);
        Assert.True(sut.EnableGlobalHotkeys);
    }
}
