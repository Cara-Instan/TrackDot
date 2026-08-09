using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Tests for <see cref="ThumbnailDecoder"/>. The decoder consumes a
/// <c>Func&lt;Task&lt;Stream&gt;&gt;</c> rather than the WinRT
/// runtime class <c>IRandomAccessStreamReference</c>, so we can
/// exercise the failure paths in-process without a live SMTC
/// session.
/// </summary>
/// <remarks>
/// <para>
/// Happy-path decoding (the <c>BitmapDecoder.CreateAsync</c> +
/// <c>SoftwareBitmap</c> pipeline) requires the WinRT COM activation
/// context that the xUnit runner does not initialise. Those cases
/// are covered by the manual smoke test described in
/// <c>docs/SMOKE_TEST.md</c> (Task 12). The tests here pin the
/// failure-path contract: any malformed input, thrown exception, or
/// pre-cancelled token must produce <c>null</c> rather than propagate.
/// </para>
/// <para>
/// <see cref="ThumbnailDecoder.MaxPixelSize"/> is exposed so the
/// policy is observable from tests without a real decode.
/// </para>
/// </remarks>
public sealed class ThumbnailDecoderTests
{
    // ---- MaxPixelSize policy ----

    [Fact]
    public void MaxPixelSize_is_a_positive_power_of_two_clamp()
    {
        // 256 is the design target for the popover artwork. Anything
        // else (e.g. 512, 128) would be a regression of the policy
        // the rest of the codebase assumes.
        Assert.Equal(256, ThumbnailDecoder.MaxPixelSize);
        Assert.True(ThumbnailDecoder.MaxPixelSize > 0,
            $"MaxPixelSize must be > 0 (was {ThumbnailDecoder.MaxPixelSize}).");
        Assert.Equal(0, ThumbnailDecoder.MaxPixelSize & (ThumbnailDecoder.MaxPixelSize - 1));
    }

    // ---- ComputeScaledSize (aspect-preserving clamp) ----
    //
    // The clamp logic is exercised through ComputeScaledSize via a
    // tiny internal helper we expose via InternalsVisibleTo. The
    // happy-path BitmapDecoder pipeline is exercised in the smoke
    // test (Task 12) - it cannot run in the xUnit runner because
    // WinRT COM activation is unavailable outside a UI process.

    [Fact]
    public void ComputeScaledSize_clamps_landscape_image_on_width()
    {
        // 1024 x 512 -> clamp to 256 long-side => 256 x 128.
        var (w, h) = ThumbnailDecoder.ComputeScaledSizeForTest(1024, 512, 256);
        Assert.Equal(256, w);
        Assert.Equal(128, h);
    }

    [Fact]
    public void ComputeScaledSize_clamps_portrait_image_on_height()
    {
        // 512 x 1024 -> clamp to 256 long-side => 128 x 256.
        var (w, h) = ThumbnailDecoder.ComputeScaledSizeForTest(512, 1024, 256);
        Assert.Equal(128, w);
        Assert.Equal(256, h);
    }

    [Fact]
    public void ComputeScaledSize_leaves_small_image_unchanged()
    {
        // 200 x 100 -> 200 < 256, no clamp => 200 x 100.
        var (w, h) = ThumbnailDecoder.ComputeScaledSizeForTest(200, 100, 256);
        Assert.Equal(200, w);
        Assert.Equal(100, h);
    }

    [Fact]
    public void ComputeScaledSize_handles_square_image()
    {
        var (w, h) = ThumbnailDecoder.ComputeScaledSizeForTest(1024, 1024, 256);
        Assert.Equal(256, w);
        Assert.Equal(256, h);
    }

    // ---- openStream returning a null stream ----

    [Fact]
    public async Task DecodeAsync_returns_null_when_openStream_returns_null()
    {
        Func<Task<Stream>> openStream = () => Task.FromResult<Stream>(null!);

        var result = await ThumbnailDecoder.DecodeAsync(openStream);

        Assert.Null(result);
    }

    // ---- openStream throwing ----

    [Fact]
    public async Task DecodeAsync_returns_null_when_openStream_throws()
    {
        Func<Task<Stream>> openStream = () => throw new InvalidOperationException("smtc failure");

        var result = await ThumbnailDecoder.DecodeAsync(openStream);

        Assert.Null(result);
    }

    [Fact]
    public async Task DecodeAsync_returns_null_when_openStream_returns_faulted_task()
    {
        Func<Task<Stream>> openStream = () => Task.FromException<Stream>(new IOException("stream gone"));

        var result = await ThumbnailDecoder.DecodeAsync(openStream);

        Assert.Null(result);
    }

    // ---- cancellation token ----

    [Fact]
    public async Task DecodeAsync_returns_null_when_token_is_already_cancelled()
    {
        var openStreamCalled = false;
        Func<Task<Stream>> openStream = () =>
        {
            openStreamCalled = true;
            return Task.FromResult<Stream>(Stream.Null);
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await ThumbnailDecoder.DecodeAsync(openStream, cts.Token);

        Assert.Null(result);
        Assert.False(openStreamCalled,
            "openStream must not be invoked when the token is already cancelled.");
    }

    [Fact]
    public async Task DecodeAsync_does_not_throw_when_token_is_already_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The whole point: a cancelled token must surface as a null
        // ImageSource, never as OperationCanceledException escaping
        // the controller service.
        var result = await ThumbnailDecoder.DecodeAsync(
            () => Task.FromResult<Stream>(Stream.Null),
            cts.Token);

        Assert.Null(result);
    }

    // ---- decode pipeline throwing ----

    [Fact]
    public async Task DecodeAsync_swallows_exceptions_during_decode()
    {
        // The actual WinRT decode path is unreachable from the test
        // runner, but we can still verify the catch-all branch by
        // feeding a stream whose contents are guaranteed to fail
        // downstream (empty bytes against a real decoder).
        //
        // The decoder must return null - never throw - on any
        // pipeline failure. If this test starts throwing, the
        // contract that the controller service relies on is broken.
        using var emptyStream = new MemoryStream();

        var result = await ThumbnailDecoder.DecodeAsync(() => Task.FromResult<Stream>(emptyStream));

        Assert.Null(result);
    }

    // ---- public contract sanity ----

    [Fact]
    public async Task DecodeAsync_returns_Task_of_nullable_ImageSource()
    {
        // Smoke test for the return type contract. We do not assert
        // anything about the value (the runner cannot run the WinRT
        // pipeline), only that the method resolves without throwing
        // when given a degenerate but non-throwing input.
        var task = ThumbnailDecoder.DecodeAsync(() => Task.FromResult<Stream>(Stream.Null));

        Assert.NotNull(task);
        var result = await task;
        Assert.Null(result); // empty stream -> null
    }
}
