using System;
using System.Threading;
using Xunit;

namespace TrackDot.Tests;

public class AboutWindowTests
{
    [Fact]
    public void AboutWindow_CanBeInstantiatedOnStaThread()
    {
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    _ = new System.Windows.Application();
                }
                if (System.Windows.Application.ResourceAssembly == null)
                {
                    System.Windows.Application.ResourceAssembly = typeof(TrackDot.AboutWindow).Assembly;
                }
                var window = new AboutWindow();
                Assert.NotNull(window);
                Assert.Equal("About TrackDot", window.Title);
                Assert.StartsWith("v0.1.0", window.VersionTextBlock.Text);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            throw exception;
        }
    }
}
