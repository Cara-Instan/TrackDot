using System;
using System.Threading;
using TrackDot.Views;
using Xunit;

namespace TrackDot.Tests;

[Collection("WPF")]
public class LyricsWindowTests
{
    [Fact]
    public void LyricsWindow_CanBeInstantiatedOnStaThread()
    {
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.ResourceAssembly == null)
                {
                    System.Windows.Application.ResourceAssembly = typeof(TrackDot.Views.LyricsWindow).Assembly;
                }
                if (System.Windows.Application.Current is not TrackDot.App)
                {
                    var app = new TrackDot.App();
                    app.InitializeComponent();
                }
                var window = new LyricsWindow();
                Assert.NotNull(window);
                Assert.Equal("TrackDot — Lyrics", window.Title);
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

    [Fact]
    public void LyricsHudWindow_CanBeInstantiatedOnStaThread()
    {
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.ResourceAssembly == null)
                {
                    System.Windows.Application.ResourceAssembly = typeof(TrackDot.Views.LyricsHudWindow).Assembly;
                }
                if (System.Windows.Application.Current is not TrackDot.App)
                {
                    var app = new TrackDot.App();
                    app.InitializeComponent();
                }
                var window = new LyricsHudWindow();
                Assert.NotNull(window);
                Assert.Equal("TrackDot — Lyrics HUD", window.Title);
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

