using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

public class ColorExtractorTests
{
    [Fact]
    public void RgbToHsl_And_HslToRgb_PureRed()
    {
        ColorExtractor.RgbToHsl(255, 0, 0, out float h, out float s, out float l);
        Assert.Equal(0f, h, 1);
        Assert.Equal(1f, s, 2);
        Assert.Equal(0.5f, l, 2);

        ColorExtractor.HslToRgb(h, s, l, out byte r, out byte g, out byte b);
        Assert.Equal(255, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
    }

    [Fact]
    public void RgbToHsl_And_HslToRgb_PureGreen()
    {
        ColorExtractor.RgbToHsl(0, 255, 0, out float h, out float s, out float l);
        Assert.Equal(120f, h, 1);
        Assert.Equal(1f, s, 2);
        Assert.Equal(0.5f, l, 2);

        ColorExtractor.HslToRgb(h, s, l, out byte r, out byte g, out byte b);
        Assert.Equal(0, r);
        Assert.Equal(255, g);
        Assert.Equal(0, b);
    }

    [Fact]
    public void RgbToHsl_And_HslToRgb_PureBlue()
    {
        ColorExtractor.RgbToHsl(0, 0, 255, out float h, out float s, out float l);
        Assert.Equal(240f, h, 1);
        Assert.Equal(1f, s, 2);
        Assert.Equal(0.5f, l, 2);

        ColorExtractor.HslToRgb(h, s, l, out byte r, out byte g, out byte b);
        Assert.Equal(0, r);
        Assert.Equal(0, g);
        Assert.Equal(255, b);
    }

    [Fact]
    public void ExtractFromBgraBytes_VibrantBlueImage_ExtractsBlueColor()
    {
        // 4x4 image of solid vibrant blue (B=255, G=50, R=50, A=255)
        byte[] pixels = new byte[4 * 4 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;     // B
            pixels[i + 1] = 50;  // G
            pixels[i + 2] = 50;  // R
            pixels[i + 3] = 255; // A
        }

        var color = ColorExtractor.ExtractFromBgraBytes(pixels, 4, 4);

        Assert.NotNull(color);
        // Blue component should dominate
        Assert.True(color.Value.B > color.Value.R);
        Assert.True(color.Value.B > color.Value.G);
    }

    [Fact]
    public void ExtractFromBgraBytes_MonochromeBlack_ReturnsNull()
    {
        byte[] pixels = new byte[4 * 4 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0;       // B
            pixels[i + 1] = 0;   // G
            pixels[i + 2] = 0;   // R
            pixels[i + 3] = 255; // A
        }

        var color = ColorExtractor.ExtractFromBgraBytes(pixels, 4, 4);
        Assert.Null(color);
    }

    [Fact]
    public void ExtractDominantColor_NullImage_ReturnsNull()
    {
        var color = ColorExtractor.ExtractDominantColor(null);
        Assert.Null(color);
    }

    [Fact]
    public void ExtractDominantColor_BitmapSource_ExtractsVibrantColor()
    {
        // 8x8 bitmap with vibrant green
        int width = 8;
        int height = 8;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 30;      // B
            pixels[i + 1] = 220; // G
            pixels[i + 2] = 30;  // R
            pixels[i + 3] = 255; // A
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();

        var color = ColorExtractor.ExtractDominantColor(bitmap);
        Assert.NotNull(color);
        Assert.True(color.Value.G > color.Value.R);
        Assert.True(color.Value.G > color.Value.B);
    }
}

