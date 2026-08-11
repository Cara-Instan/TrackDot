using System;
using TrackDot.Services;
using TrackDot.Tests.Fakes;
using TrackDot.ViewModels;
using Xunit;

namespace TrackDot.Tests;

public class GlobalHotkeyServiceTests
{
    [Fact]
    public void Constructor_NullViewModel_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GlobalHotkeyService(null!));
    }

    [Fact]
    public void Register_ZeroHandle_DoesNotRegister()
    {
        var fakeMedia = new FakeMediaControllerService();
        var fakeTicker = new FakeTicker();
        using var vm = new MainViewModel(fakeMedia, fakeTicker);
        using var service = new GlobalHotkeyService(vm);

        service.Register(IntPtr.Zero);

        Assert.False(service.IsRegistered);
    }

    [Fact]
    public void Unregister_WhenNotRegistered_DoesNotThrow()
    {
        var fakeMedia = new FakeMediaControllerService();
        var fakeTicker = new FakeTicker();
        using var vm = new MainViewModel(fakeMedia, fakeTicker);
        using var service = new GlobalHotkeyService(vm);

        service.Unregister();

        Assert.False(service.IsRegistered);
    }

    [Fact]
    public void Dispose_UnregistersAndDisposesCleanly()
    {
        var fakeMedia = new FakeMediaControllerService();
        var fakeTicker = new FakeTicker();
        using var vm = new MainViewModel(fakeMedia, fakeTicker);
        var service = new GlobalHotkeyService(vm);

        service.Dispose();

        Assert.False(service.IsRegistered);
    }
}
