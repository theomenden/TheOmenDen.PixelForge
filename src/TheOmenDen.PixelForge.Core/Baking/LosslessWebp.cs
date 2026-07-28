using System.Buffers.Binary;
using CommunityToolkit.Diagnostics;
using DotNext;
using Microsoft.IO;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Buffers;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Lossless WebP encoding, verified rather than asserted.
/// <para>
/// The contract with Corvus says lossless is proven "by round-trip, never by trusting the
/// encoder flag", so <see cref="EncodeVerified"/> is the only way to get a sheet out of here:
/// it decodes what it just encoded and compares. A failed verification comes back as a
/// <see cref="BakeFailure"/> to be handled, not an exception to be swallowed.
/// </para>
/// <para>
/// Encoding writes straight into a pooled <see cref="RecyclableMemoryStream"/> via Skia's
/// stream overload, so there is no intermediate <c>SKData</c> and no <c>byte[]</c> copy on the
/// way to disk. The caller owns the returned stream and disposes it to return the buffer.
/// </para>
/// </summary>
public static class LosslessWebp
{
    private static SKWebpEncoderOptions Options => new(SKWebpEncoderCompression.Lossless, 100f);

    /// <summary>
    /// Encodes without verifying. Prefer <see cref="EncodeVerified"/> — this is exposed so
    /// tests can observe raw encoder behaviour.
    /// </summary>
    public static Result<RecyclableMemoryStream, BakeFailure> Encode(SKBitmap bitmap)
    {
        Guard.IsNotNull(bitmap);

        var stream = PooledStreams.New($"webp:{bitmap.Width}x{bitmap.Height}");

        try
        {
            using var pixmap = bitmap.PeekPixels();

            if (pixmap is null || !SKWebpEncoder.Encode(stream, pixmap, Options))
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
    /// Encodes, then proves the result is both a lossless container and pixel-exact.
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
            // GetBuffer hands back the pooled buffer itself. ToArray would copy it onto the
            // managed heap, which is why the manager is configured to make ToArray throw.
            var written = stream.GetBuffer().AsSpan(0, (int)stream.Length);

            if (!IsLosslessContainer(written))
            {
                stream.Dispose();
                return new(BakeFailure.EncoderProducedLossyOutput);
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
    /// Walks the RIFF container looking for the compression chunk. Lossless WebP carries
    /// <c>VP8L</c>; lossy carries <c>VP8 </c> (trailing space). This reads the file rather
    /// than trusting the flag that produced it.
    /// </summary>
    public static bool IsLosslessContainer(ReadOnlySpan<byte> webp)
    {
        const int HeaderLength = 12;
        const int ChunkHeaderLength = 8;

        if (webp.Length < HeaderLength
            || !webp[..4].SequenceEqual("RIFF"u8)
            || !webp.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return false;
        }

        var sawLossless = false;
        var offset = HeaderLength;

        while (offset + ChunkHeaderLength <= webp.Length)
        {
            var fourCc = webp.Slice(offset, 4);
            var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(webp.Slice(offset + 4, 4));

            if (fourCc.SequenceEqual("VP8 "u8))
            {
                return false;
            }

            if (fourCc.SequenceEqual("VP8L"u8))
            {
                sawLossless = true;
            }

            // chunk payloads are padded to an even length
            var advance = ChunkHeaderLength + (long)payloadLength + (payloadLength & 1);

            if (advance <= 0 || offset + advance > webp.Length)
            {
                break;
            }

            offset += (int)advance;
        }

        return sawLossless;
    }

    /// <summary>
    /// Decodes <paramref name="webp"/> and compares it to <paramref name="original"/>.
    /// <para>
    /// Alpha must match everywhere. RGB is compared only where alpha is non-zero: Skia has no
    /// equivalent of <c>cwebp -exact</c>, so colour underneath a fully transparent pixel is
    /// not preserved. Nothing renders it, but a raw byte comparison would fail on it and send
    /// someone hunting a bug that is not there.
    /// </para>
    /// </summary>
    public static bool RoundTripsExactly(ReadOnlySpan<byte> webp, SKBitmap original)
    {
        Guard.IsNotNull(original);

        var info = new SKImageInfo(
            original.Width,
            original.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);

        using var decoded = SKBitmap.Decode(webp, info);

        if (decoded is null || decoded.Width != original.Width || decoded.Height != original.Height)
        {
            return false;
        }

        using var expectedPixels = original.PeekPixels();
        using var actualPixels = decoded.PeekPixels();

        if (expectedPixels is null || actualPixels is null)
        {
            return false;
        }

        var expected = expectedPixels.GetPixelSpan();
        var actual = actualPixels.GetPixelSpan();

        if (expected.Length != actual.Length)
        {
            return false;
        }

        for (var offset = 0; offset + 4 <= expected.Length; offset += 4)
        {
            if (expected[offset + 3] != actual[offset + 3])
            {
                return false;
            }

            if (expected[offset + 3] is 0)
            {
                continue;
            }

            if (expected[offset] != actual[offset]
                || expected[offset + 1] != actual[offset + 1]
                || expected[offset + 2] != actual[offset + 2])
            {
                return false;
            }
        }

        return true;
    }
}
