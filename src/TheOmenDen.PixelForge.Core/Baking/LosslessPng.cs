using CommunityToolkit.Diagnostics;
using DotNext;
using Microsoft.IO;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Buffers;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// PNG encoding, verified rather than asserted — the container the engine consumers read.
/// <para>
/// Neither Unity's <c>TextureImporter</c> nor MonoGame's content pipeline opens WebP, so this is
/// not an alternative to <see cref="LosslessWebp"/> so much as the only way to reach those two.
/// Corvus continues to receive WebP.
/// </para>
/// <para>
/// PNG cannot be lossy, so there is no counterpart to
/// <see cref="LosslessWebp.IsLosslessContainer"/> — the format admits no equivalent of a
/// <c>VP8 </c> chunk to catch. The round trip is kept anyway: the encode path has options that can
/// be got wrong, and a sheet must be identical art whichever container a recipe names.
/// </para>
/// </summary>
public static class LosslessPng
{
    /// <summary>The eight bytes every PNG opens with.</summary>
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Encodes without verifying. Prefer <see cref="EncodeVerified"/> — this is exposed so tests
    /// can observe raw encoder behaviour, matching <see cref="LosslessWebp.Encode"/>.
    /// </summary>
    public static Result<RecyclableMemoryStream, BakeFailure> Encode(SKBitmap bitmap)
    {
        Guard.IsNotNull(bitmap);

        var stream = PooledStreams.New($"png:{bitmap.Width}x{bitmap.Height}");

        try
        {
            using var pixmap = bitmap.PeekPixels();

            // The stream overload, so there is no intermediate SKData and no byte[] copy on the
            // way to disk — the same reason LosslessWebp takes it.
            if (pixmap is null || !pixmap.Encode(stream, SKEncodedImageFormat.Png, 100))
            {
                stream.Dispose();
                return new(BakeFailure.EncoderReturnedNoData);
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Encodes, then proves the result is a PNG and pixel-exact.
    /// On failure the pooled buffer is returned rather than leaked.
    /// </summary>
    public static Result<RecyclableMemoryStream, BakeFailure> EncodeVerified(SKBitmap bitmap)
    {
        var encoded = Encode(bitmap);

        if (!encoded.TryGet(out var stream))
        {
            return encoded;
        }

        try
        {
            // GetBuffer hands back the pooled buffer itself; ToArray is configured to throw.
            var written = stream.GetBuffer().AsSpan(0, (int)stream.Length);

            if (!written.StartsWith(Signature))
            {
                stream.Dispose();
                return new(BakeFailure.EncoderReturnedNoData);
            }

            if (!RoundTripsExactly(written, bitmap))
            {
                stream.Dispose();
                return new(BakeFailure.RoundTripMismatch);
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Decodes <paramref name="png"/> and compares every byte to <paramref name="original"/>.
    /// </summary>
    /// <remarks>
    /// Stricter than <see cref="LosslessWebp.RoundTripsExactly"/>, deliberately. That one skips RGB
    /// under fully transparent pixels because Skia has no equivalent of <c>cwebp -exact</c> and the
    /// colour there is genuinely not preserved. PNG stores those bytes, so anything short of a whole
    /// -buffer comparison would be leaving verification on the table for no reason.
    /// </remarks>
    public static bool RoundTripsExactly(ReadOnlySpan<byte> png, SKBitmap original)
    {
        Guard.IsNotNull(original);

        var info = new SKImageInfo(
            original.Width,
            original.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);

        using var decoded = SKBitmap.Decode(png, info);

        if (decoded is null || decoded.Width != original.Width || decoded.Height != original.Height)
        {
            return false;
        }

        using var expectedPixels = original.PeekPixels();
        using var actualPixels = decoded.PeekPixels();

        return expectedPixels is not null
            && actualPixels is not null
            && expectedPixels.GetPixelSpan().SequenceEqual(actualPixels.GetPixelSpan());
    }
}
