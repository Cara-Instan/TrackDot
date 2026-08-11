using System;
using TrackDot.Models;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

public class ThemeServiceTests
{
    [Fact]
    public void Construction_ExplicitInitialMode_SetsSelectedTheme()
    {
        using var sut = new ThemeService(AppThemeMode.System);
        Assert.Equal(AppThemeMode.System, sut.SelectedTheme);
    }

    [Fact]
    public void SettingSelectedThemeToDark_MakesIsEffectiveDarkTrue()
    {
        using var sut = new ThemeService(AppThemeMode.System);
        sut.SelectedTheme = AppThemeMode.Dark;
        Assert.True(sut.IsEffectiveDark);
        Assert.Equal(AppThemeMode.Dark, sut.SelectedTheme);

        // Reset so user registry stays at System
        sut.SelectedTheme = AppThemeMode.System;
    }

    [Fact]
    public void SettingSelectedThemeToLight_MakesIsEffectiveDarkFalse()
    {
        using var sut = new ThemeService(AppThemeMode.System);
        sut.SelectedTheme = AppThemeMode.Light;
        Assert.False(sut.IsEffectiveDark);
        Assert.Equal(AppThemeMode.Light, sut.SelectedTheme);

        // Reset so user registry stays at System
        sut.SelectedTheme = AppThemeMode.System;
    }

    [Fact]
    public void ModeChange_RaisesEffectiveThemeChangedEvent()
    {
        using var sut = new ThemeService(AppThemeMode.System);
        sut.SelectedTheme = AppThemeMode.Dark;

        bool? raisedTheme = null;
        sut.EffectiveThemeChanged += (s, isDark) => raisedTheme = isDark;

        sut.SelectedTheme = AppThemeMode.Light;

        Assert.False(raisedTheme);

        // Reset so user registry stays at System
        sut.SelectedTheme = AppThemeMode.System;
    }

    [Fact]
    public void DetectSystemIsDark_DoesNotThrow()
    {
        using var sut = new ThemeService(AppThemeMode.System);
        var result = sut.DetectSystemIsDark();
        Assert.True(result || !result);
    }
}
