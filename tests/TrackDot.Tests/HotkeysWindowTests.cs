using System;
using System.Threading;
using TrackDot.Views;
using Xunit;

namespace TrackDot.Tests;

public class HotkeysWindowTests
{
    [Fact]
    public void HotkeysWindow_CanBeInstantiatedOnStaThread()
    {
        string? title = null;
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.ResourceAssembly == null)
                {
                    System.Windows.Application.ResourceAssembly = typeof(TrackDot.Views.HotkeysWindow).Assembly;
                }

                var window = new HotkeysWindow();
                title = window.Title;
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(exception);
        Assert.Equal("Keyboard Shortcuts", title);
    }
}
