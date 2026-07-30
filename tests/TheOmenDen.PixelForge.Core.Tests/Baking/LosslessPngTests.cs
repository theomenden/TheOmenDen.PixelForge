using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// PNG is lossless by construction, so the round trip is not proving the codec the way
/// <see cref="LosslessWebp"/>'s does. It proves this project's encode path, which still has
/// options that can be got wrong, and which has to stay interchangeable with the WebP one —
/// a sheet must be identical art whichever container a recipe names.
/// </summary>
public sealed class LosslessPngTests
{
    /// <summary>
    /// The same hostile input <see cref="LosslessWebpTests"/> uses: hard colour edges every pixel
    /// and fully transparent regions, mirroring real pixel art's strictly binary alpha.
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

    /// <summary>
    /// Verified through an independent path — the container signature and a fresh Skia decode —
    /// rather than through the predicate the encoder itself consults, which would only prove the
    /// check agrees with itself.
    /// </summary>
    [Fact]
    public void EncodeVerified_ProducesAPngThatDecodesToTheSamePixels()
    {
        using var source = NoisyPixelArt();

        var result = LosslessPng.EncodeVerified(source);

        Assert.True(result.IsSuccessful, $"encode failed with {result.Error}");

        using var stream = result.Value;
        var written = stream.GetBuffer().AsSpan(0, (int)stream.Length).ToArray();

        Assert.Equal<byte[]>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], written[..8]);

        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        using var decoded = SKBitmap.Decode(written, info);

        Assert.NotNull(decoded);

        using var expected = source.PeekPixels();
        using var actual = decoded.PeekPixels();

        Assert.Equal<byte[]>(expected.GetPixelSpan().ToArray(), actual.GetPixelSpan().ToArray());
    }

    /// <summary>
    /// The dispatcher really reaches a different encoder per format, rather than both recipes
    /// quietly landing on the same one — which would produce WebP bytes inside a .png and no
    /// downstream complaint until an importer refused the file.
    /// </summary>
    [Theory]
    [InlineData(SheetFormat.Png, (byte)0x89)]
    [InlineData(SheetFormat.Webp, (byte)'R')]
    public void SheetEncoder_EncodesInTheContainerTheFormatNames(SheetFormat format, byte firstByte)
    {
        using var source = NoisyPixelArt();

        var result = SheetEncoder.EncodeVerified(source, format);

        Assert.True(result.IsSuccessful, $"encode failed with {result.Error}");

        using var stream = result.Value;

        Assert.Equal(firstByte, stream.GetBuffer()[0]);
    }
}
