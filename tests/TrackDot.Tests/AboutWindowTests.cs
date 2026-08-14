using System;
using System.Threading;
using TrackDot.Views;
using Xunit;

namespace TrackDot.Tests;

[Collection("WPF")]
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
                // WPF refuses to create a second Application in the
                // same AppDomain. MainWindowShowPopoverTests needs the
                // App class (not a barebones Application) so App.xaml's
                // <Application.Resources> register MainWindow.xaml's
                // resource lookups. Use App here too so the singleton
                // is whichever class runs first - either way it is App
                // and both test classes see it.
                if (System.Windows.Application.ResourceAssembly == null)
                {
                    System.Windows.Application.ResourceAssembly = typeof(TrackDot.Views.AboutWindow).Assembly;
                }
                if (System.Windows.Application.Current is not TrackDot.App)
                {
                    var app = new TrackDot.App();
                    app.InitializeComponent();
                }
                var window = new AboutWindow();
                Assert.NotNull(window);
                Assert.Equal("About TrackDot", window.Title);
                Assert.StartsWith("v0.2.0-beta", window.VersionTextBlock.Text);
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
