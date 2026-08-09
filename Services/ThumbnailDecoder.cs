using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRTBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace TrackDot.Services;

/// <summary>
/// Decodes SMTC media-property thumbnails into a frozen
/// <see cref="System.Windows.Media.ImageSource"/> ready for the WPF
/// UI thread to bind.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why <c>Func&lt;Task&lt;Stream&gt;&gt;</c> and not the WinRT
/// <c>IRandomAccessStreamReference</c>?</b> The runtime class has no
/// public constructor and no testable substitute - the same
/// testability constraint that shaped <see cref="MediaPropertyMapper"/>
/// in Task 3. By taking a delegate that opens a managed
/// <see cref="Stream"/>, the unit tests can supply a
/// <see cref="MemoryStream"/> and exercise every failure path
/// without a live SMTC session.
/// </para>
/// <para>
/// <b>Why not the UWP <c>WriteableBitmap</c>?</c>
/// <c>SoftwareBitmap.CopyTo(WriteableBitmap)</c> exists in C++/WinRT
/// but not in the CsWinRT projection for the .NET 6 SDK ref. The UWP
/// <c>Windows.UI.Xaml.Media.Imaging.WriteableBitmap</c> here only
/// exposes a <c>(int, int)</c> ctor and a <c>PixelBuffer</c> -
/// no DPI, no PixelFormat - so we cannot construct one that matches
/// the SMTC pixel data. We use the WPF-native
/// <see cref="System.Windows.Media.Imaging.WriteableBitmap"/>, which
/// has the rich <c>WritePixels</c> surface we need.
/// </para>
/// <para>
/// <b>Failure contract:</b> every failure mode (null stream, thrown
/// exception, pre-cancelled token, malformed bytes, WinRT COM error)
/// returns <c>null</c>. The controller service subsumes the result
/// into <see cref="Models.MediaSessionSnapshot.Empty"/> semantics -
/// "no artwork" is a first-class state, not an exception.
/// </para>
/// </remarks>
public static class ThumbnailDecoder
{
    /// <summary>
    /// Maximum side length (in pixels) the decoder will produce.
    /// 256 is the design target for the popover's artwork well;
    /// anything larger is wasted memory and decode cost.
    /// </summary>
    public const int MaxPixelSize = 256;

    /// <summary>
    /// Decodes a thumbnail stream into a frozen
    /// <see cref="System.Windows.Media.ImageSource"/>. Returns
    /// <c>null</c> on any failure - the caller does not need to
    /// distinguish "no artwork" from "could not decode".
    /// </summary>
    /// <param name="openStream">
    /// Asynchronously opens the artwork stream. The delegate is
    /// invoked at most once. Pass
    /// <c>() =&gt; reference.OpenReadAsync().AsTask().ContinueWith(t =&gt; t.Result.AsStreamForRead())</c>
    /// from the controller service to bridge from
    /// <see cref="IRandomAccessStreamReference"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// If already cancelled, the decoder short-circuits without
    /// invoking <paramref name="openStream"/>. Mid-flight cancellation
    /// is honoured at WinRT await points.
    /// </param>
    public static async Task<System.Windows.Media.ImageSource?> DecodeAsync(
        Func<Task<Stream>> openStream,
        CancellationToken cancellationToken = default)
    {
        if (openStream is null) throw new ArgumentNullException(nameof(openStream));
        if (cancellationToken.IsCancellationRequested) return null;

        try
        {
            using var stream = await openStream().ConfigureAwait(true);
            if (stream is null) return null;

            // BitmapDecoder requires an IRandomAccessStream, not a
            // managed Stream. The WinRT projection supplies the
            // bridge via WindowsRuntimeStreamExtensions.
            //
            // BitmapDecoder / SoftwareBitmap / Buffer are CsWinRT
            // runtime classes; their IClosable.Dispose is internal
            // to the projection and not callable from C# code. They
            // are GC-managed, so we do not try to dispose them
            // explicitly. The WPF WriteableBitmap we allocate below
            // is a managed object and the lock/unlock pair ensures
            // the BackBuffer pointer is not in use when the bitmap
            // is GC'd.
            var decoder = await WinRTBitmapDecoder.CreateAsync(stream.AsRandomAccessStream())
                .AsTask(cancellationToken)
                .ConfigureAwait(true);

            // Clamp to MaxPixelSize while preserving aspect ratio.
            // BitmapTransform.ScaledWidth/Height are uint; 0 means
            // "leave as-is" so we only set the axis that overflows.
            var transform = new BitmapTransform();
            var pixelWidth  = (int)decoder.PixelWidth;
            var pixelHeight = (int)decoder.PixelHeight;

            if (pixelWidth > MaxPixelSize || pixelHeight > MaxPixelSize)
            {
                var (scaledW, scaledH) = ComputeScaledSize(pixelWidth, pixelHeight, MaxPixelSize);
                transform.ScaledWidth  = (uint)scaledW;
                transform.ScaledHeight = (uint)scaledH;
            }

            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage)
                .AsTask(cancellationToken)
                .ConfigureAwait(true);

            if (softwareBitmap is null) return null;

            // Marshal the BGRA8 bytes into a WPF WriteableBitmap.
            // The WPF WriteableBitmap's BackBuffer is a raw pointer;
            // we lock it, copy bytes via Marshal.Copy, then unlock.
            // bitmap.Freeze() below makes the result publish-safe
            // across the WPF dispatcher boundary.
            var width  = softwareBitmap.PixelWidth;
            var height = softwareBitmap.PixelHeight;

            var bitmap = new WriteableBitmap(width, height, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32, null);

            var bytesPerPixel = 4; // BGRA8 == 4 bytes per pixel
            var stride = width * bytesPerPixel;
            var bufferSize = stride * height;

            if (bufferSize == 0)
            {
                return null;
            }

            // Windows.Storage.Streams.Buffer is the IBuffer
            // implementation CsWinRT exposes. Allocate with the
            // exact capacity and set Length = capacity so CopyToBuffer
            // fills it. CopyToBuffer is a synchronous method on
            // SoftwareBitmap.
            //
            // BitmapDecoder / SoftwareBitmap / Buffer are CsWinRT
            // runtime classes whose IClosable.Dispose is internal to
            // the projection and not callable from C# code. They are
            // GC-managed; no explicit close is required.
            var buffer = new Windows.Storage.Streams.Buffer((uint)bufferSize)
            {
                Length = (uint)bufferSize,
            };
            softwareBitmap.CopyToBuffer(buffer);

            bitmap.Lock();
            try
            {
                // IBuffer does not expose a ToArray() in CsWinRT; copy
                // via a managed byte[].
                var srcBytes = new byte[buffer.Length];
                using var reader = buffer.AsStream();
                reader.ReadExactly(srcBytes);

                System.Runtime.InteropServices.Marshal.Copy(
                    srcBytes, 0, bitmap.BackBuffer, srcBytes.Length);
            }
            finally
            {
                bitmap.Unlock();
            }

            bitmap.Freeze();
            return bitmap;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            // Malformed thumbnail, COM activation failure, decoder
            // mismatch - all collapse to "no artwork". Task 9 will
            // wire real logging.
            return null;
        }
    }

    /// <summary>
    /// Computes a scaled (width, height) that fits within
    /// <paramref name="maxSide"/> on the longer axis while preserving
    /// aspect ratio. Always returns positive integer pixel counts;
    /// the input dimensions are assumed to be positive.
    /// </summary>
    private static (int Width, int Height) ComputeScaledSize(
        int pixelWidth, int pixelHeight, int maxSide)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return (maxSide, maxSide);
        }

        if (pixelWidth >= pixelHeight)
        {
            var w = Math.Min(pixelWidth, maxSide);
            var h = (int)Math.Round(pixelHeight * (double)w / pixelWidth);
            if (h < 1) h = 1;
            return (w, h);
        }

        var h2 = Math.Min(pixelHeight, maxSide);
        var w2 = (int)Math.Round(pixelWidth * (double)h2 / pixelHeight);
        if (w2 < 1) w2 = 1;
        return (w2, h2);
    }

    /// <summary>
    /// Test-only entry point that exposes the private
    /// <see cref="ComputeScaledSize"/> logic to the unit-test
    /// assembly via <c>InternalsVisibleTo</c>. Used to verify the
    /// aspect-preserving clamp policy without standing up the full
    /// WinRT pipeline.
    /// </summary>
    internal static (int Width, int Height) ComputeScaledSizeForTest(
        int pixelWidth, int pixelHeight, int maxSide)
        => ComputeScaledSize(pixelWidth, pixelHeight, maxSide);
}
