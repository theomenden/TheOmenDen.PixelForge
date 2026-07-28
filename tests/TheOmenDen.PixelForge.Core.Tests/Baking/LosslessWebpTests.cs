using Microsoft.IO;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Buffers;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The contract says lossless is proven by round-trip, never by trusting the encoder flag.
/// These tests hold both halves of that: the container really carries VP8L, and the pixels
/// really survive — and a failure surfaces as a <see cref="BakeFailure"/> rather than an
/// exception someone can swallow.
/// </summary>
public sealed class LosslessWebpTests
{
    /// <summary>
    /// Deliberately hostile to a lossy codec: hard colour edges every pixel, plus fully
    /// transparent regions. Mirrors real pixel art, which is strictly binary alpha.
    /// </summary>
    private static SKBitmap NoisyPixelArt(int width = 64, int height = 48)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var pixels = bitmap.Pixels;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = (x + y) % 5 is 0
                    ? SKColors.Transparent
                    : new SKColor((byte)(x * 7), (byte)(y * 11), (byte)((x ^ y) * 3), 0xFF);
            }
        }

        bitmap.Pixels = pixels;
        return bitmap;
    }

    private static byte[] LossyEncode(SKBitmap bitmap)
    {
        using var pixmap = bitmap.PeekPixels();
        using var lossy = SKWebpEncoder.Encode(
            pixmap,
            new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, 80f));

        Assert.NotNull(lossy);

        return lossy.ToArray();
    }

    private static RecyclableMemoryStream EncodeOrFail(SKBitmap bitmap)
    {
        var result = LosslessWebp.EncodeVerified(bitmap);

        Assert.True(result.IsSuccessful, $"encode failed with {result.Error}");

        return result.Value;
    }

    [Fact]
    public void EncodeVerified_RoundTripsExactly()
    {
        using var source = NoisyPixelArt();
        using var stream = EncodeOrFail(source);

        // GetBuffer, never ToArray — the manager is configured to make ToArray throw.
        var encoded = stream.GetBuffer().AsSpan(0, (int)stream.Length);

        Assert.True(encoded.Length > 0);
        Assert.True(LosslessWebp.IsLosslessContainer(encoded));
        Assert.True(LosslessWebp.RoundTripsExactly(encoded, source));
    }

    [Fact]
    public void EncodeVerified_PreservesBinaryAlpha()
    {
        using var source = NoisyPixelArt();
        using var stream = EncodeOrFail(source);

        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var decoded = SKBitmap.Decode(stream.GetBuffer().AsSpan(0, (int)stream.Length), info);

        var expected = source.Pixels;
        var actual = decoded.Pixels;

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Alpha, actual[i].Alpha);
            Assert.True(actual[i].Alpha is 0 or 0xFF, "alpha stopped being binary");
        }
    }

    /// <summary>
    /// The check that earns its keep — a lossy encode of the same bitmap must be rejected by
    /// the container check, or the guard is decorative.
    /// </summary>
    [Fact]
    public void IsLosslessContainer_ReturnsFalse_ForALossyEncode()
    {
        using var source = NoisyPixelArt();

        Assert.False(LosslessWebp.IsLosslessContainer(LossyEncode(source)));
    }

    [Fact]
    public void RoundTripsExactly_ReturnsFalse_ForALossyEncode()
    {
        using var source = NoisyPixelArt();

        Assert.False(LosslessWebp.RoundTripsExactly(LossyEncode(source), source));
    }

    [Fact]
    public void IsLosslessContainer_ReturnsFalse_ForEmptyInput()
        => Assert.False(LosslessWebp.IsLosslessContainer([]));

    [Fact]
    public void IsLosslessContainer_ReturnsFalse_ForNonRiffBytes()
        => Assert.False(LosslessWebp.IsLosslessContainer("this is not a webp file at all"u8));

    [Fact]
    public void IsLosslessContainer_ReturnsFalse_WhenRiffHeaderIsTruncated()
        => Assert.False(LosslessWebp.IsLosslessContainer("RIFF"u8));

    /// <summary>
    /// Pins the manager configuration. ToArray copies a pooled buffer back onto the managed
    /// heap, undoing the pooling; the manager is set up to refuse. If that option is ever
    /// dropped this fails rather than quietly regressing allocation behaviour.
    /// </summary>
    [Fact]
    public void PooledStreams_RefuseToArray_SoPoolingCannotBeUndone()
    {
        using var stream = PooledStreams.New(nameof(PooledStreams_RefuseToArray_SoPoolingCannotBeUndone));

        stream.Write("payload"u8);

        Assert.Throws<NotSupportedException>(() => stream.ToArray());
    }
}
