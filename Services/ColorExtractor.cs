using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TrackDot.Services;

/// <summary>
/// Extracts vibrant dominant accent colors from album artwork images
/// for dynamic UI palette tinting and ambient glowing.
/// </summary>
public static class ColorExtractor
{
    private record ColorBucket(float TotalR, float TotalG, float TotalB, float TotalWeight, int Count)
    {
        public ColorBucket Add(byte r, byte g, byte b, float weight) =>
            new(TotalR + r * weight, TotalG + g * weight, TotalB + b * weight, TotalWeight + weight, Count + 1);
    }

    /// <summary>
    /// Extracts the most vibrant dominant color from the given <see cref="ImageSource"/>.
    /// Returns <c>null</c> if the image is null, invalid, or purely monochrome/black/white.
    /// </summary>
    public static Color? ExtractDominantColor(ImageSource? imageSource)
    {
        if (imageSource is not BitmapSource bitmap)
            return null;

        try
        {
            // Normalize to a small, fast 32x32 sampling grid
            int sampleWidth = Math.Min(32, Math.Max(1, bitmap.PixelWidth));
            int sampleHeight = Math.Min(32, Math.Max(1, bitmap.PixelHeight));

            FormatConvertedBitmap converted = new();
            converted.BeginInit();
            converted.Source = bitmap;
            converted.DestinationFormat = PixelFormats.Bgra32;
            converted.EndInit();

            // Downsample if large
            BitmapSource sourceToSample = converted;
            if (bitmap.PixelWidth > sampleWidth || bitmap.PixelHeight > sampleHeight)
            {
                var transform = new TransformedBitmap(converted, new ScaleTransform(
                    (double)sampleWidth / bitmap.PixelWidth,
                    (double)sampleHeight / bitmap.PixelHeight));
                sourceToSample = transform;
            }

            int stride = sampleWidth * 4;
            byte[] pixels = new byte[stride * sampleHeight];
            sourceToSample.CopyPixels(pixels, stride, 0);

            return ExtractFromBgraBytes(pixels, sampleWidth, sampleHeight);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Analyzes a BGRA32 pixel buffer and extracts the dominant vibrant color.
    /// </summary>
    public static Color? ExtractFromBgraBytes(byte[] bgraBytes, int width, int height)
    {
        if (bgraBytes == null || bgraBytes.Length < 4 || width <= 0 || height <= 0)
            return null;

        // 16 Hue buckets (22.5 deg each)
        const int numBuckets = 16;
        var buckets = new ColorBucket[numBuckets];
        for (int i = 0; i < numBuckets; i++)
            buckets[i] = new ColorBucket(0, 0, 0, 0, 0);

        float maxWeight = 0;
        int bestBucketIndex = -1;
        int totalValidSamples = 0;

        int totalPixels = Math.Min(width * height, bgraBytes.Length / 4);

        for (int i = 0; i < totalPixels; i++)
        {
            int offset = i * 4;
            byte b = bgraBytes[offset];
            byte g = bgraBytes[offset + 1];
            byte r = bgraBytes[offset + 2];
            byte a = bgraBytes[offset + 3];

            if (a < 128) continue; // Ignore transparent pixels

            RgbToHsl(r, g, b, out float h, out float s, out float l);

            // Filter out extreme black, extreme white, and very washed out grayscale
            if (l < 0.12f || l > 0.92f || s * (1f - Math.Abs(l - 0.5f)) < 0.12f)
                continue;

            // Score candidate by saturation and mid-tone lightness preference
            float midToneBonus = 1.0f - Math.Abs(l - 0.5f) * 1.2f;
            if (midToneBonus < 0.2f) midToneBonus = 0.2f;

            float weight = s * s * midToneBonus;

            int bucket = (int)(h / 360f * numBuckets) % numBuckets;
            buckets[bucket] = buckets[bucket].Add(r, g, b, weight);
            totalValidSamples++;

            if (buckets[bucket].TotalWeight > maxWeight)
            {
                maxWeight = buckets[bucket].TotalWeight;
                bestBucketIndex = bucket;
            }
        }

        if (bestBucketIndex >= 0 && buckets[bestBucketIndex].TotalWeight > 0)
        {
            var best = buckets[bestBucketIndex];
            byte avgR = (byte)Math.Clamp((int)Math.Round(best.TotalR / best.TotalWeight), 0, 255);
            byte avgG = (byte)Math.Clamp((int)Math.Round(best.TotalG / best.TotalWeight), 0, 255);
            byte avgB = (byte)Math.Clamp((int)Math.Round(best.TotalB / best.TotalWeight), 0, 255);

            // Ensure the extracted color has enough vibrance and suitable brightness
            return EnhanceVibrance(avgR, avgG, avgB);
        }

        return null;
    }

    private static Color EnhanceVibrance(byte r, byte g, byte b)
    {
        RgbToHsl(r, g, b, out float h, out float s, out float l);

        // Boost saturation if slightly muted
        if (s < 0.55f) s = Math.Min(1.0f, s * 1.35f);

        // Keep lightness in a pleasant range [0.45, 0.70] for UI accents
        if (l < 0.45f) l = 0.48f;
        else if (l > 0.72f) l = 0.68f;

        HslToRgb(h, s, l, out byte outR, out byte outG, out byte outB);
        return Color.FromRgb(outR, outG, outB);
    }

    public static void RgbToHsl(byte rByte, byte gByte, byte bByte, out float h, out float s, out float l)
    {
        float r = rByte / 255f;
        float g = gByte / 255f;
        float b = bByte / 255f;

        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;

        l = (max + min) / 2f;

        if (delta < 0.00001f)
        {
            h = 0;
            s = 0;
            return;
        }

        s = l > 0.5f ? delta / (2f - max - min) : delta / (max + min);

        if (Math.Abs(max - r) < 0.00001f)
        {
            h = (g - b) / delta + (g < b ? 6f : 0f);
        }
        else if (Math.Abs(max - g) < 0.00001f)
        {
            h = (b - r) / delta + 2f;
        }
        else
        {
            h = (r - g) / delta + 4f;
        }

        h *= 60f;
    }

    public static void HslToRgb(float h, float s, float l, out byte rByte, out byte gByte, out byte bByte)
    {
        if (s < 0.00001f)
        {
            byte grey = (byte)Math.Round(l * 255f);
            rByte = gByte = bByte = grey;
            return;
        }

        float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
        float p = 2f * l - q;
        float hk = (h % 360f) / 360f;
        if (hk < 0) hk += 1f;

        rByte = (byte)Math.Round(HueToRgb(p, q, hk + 1f / 3f) * 255f);
        gByte = (byte)Math.Round(HueToRgb(p, q, hk) * 255f);
        bByte = (byte)Math.Round(HueToRgb(p, q, hk - 1f / 3f) * 255f);
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
    }
}

