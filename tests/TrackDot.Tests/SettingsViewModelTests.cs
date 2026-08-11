using System;
using TrackDot.Models;
using TrackDot.Services;
using TrackDot.ViewModels;
using Xunit;

namespace TrackDot.Tests;

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
}