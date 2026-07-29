using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// Proves the frame remap against a synthetic source in which every cell is stamped with its
/// own coordinates, so a misplaced row or column is caught by reading the output back rather
/// than by eyeballing a sheet.
/// </summary>
public sealed class SheetBakerTests
{
    /// <summary>Every cell filled with R = source column, G = source facing row.</summary>
    private static SKBitmap CoordinateStampedSource()
    {
        var bitmap = NewBitmap(SheetLayout.SourceWidth, SheetLayout.SourceHeight);
        var pixels = bitmap.Pixels;

        for (var y = 0; y < SheetLayout.SourceHeight; y++)
        {
            for (var x = 0; x < SheetLayout.SourceWidth; x++)
            {
                var column = (byte)(x / SheetLayout.CellSize);
                var row = (byte)(y / SheetLayout.CellSize);

                pixels[(y * SheetLayout.SourceWidth) + x] = new SKColor(column, row, 0xC0, 0xFF);
            }
        }

        bitmap.Pixels = pixels;
        return bitmap;
    }

    private static SKBitmap NewBitmap(int width, int height)
        => new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

    private static SKBitmap CurateOrFail(SKBitmap source)
    {
        var result = SheetBaker.Curate(source);

        Assert.True(result.IsSuccessful, $"curate failed with {result.Error}");

        return result.Value;
    }

    private static SKBitmap RecolorOrFail(SKBitmap source, RampSubstitution substitution)
    {
        var result = SheetBaker.Recolor(source, substitution);

        Assert.True(result.IsSuccessful, $"recolor failed with {result.Error}");

        return result.Value;
    }

    private static SKColor CellCentre(SKBitmap sheet, int column, int row)
        => sheet.GetPixel(
            (column * SheetLayout.CellSize) + (SheetLayout.CellSize / 2),
            (row * SheetLayout.CellSize) + (SheetLayout.CellSize / 2));

    [Fact]
    public void Curate_ProducesTheContractGeometry()
    {
        using var source = CoordinateStampedSource();
        using var curated = CurateOrFail(source);

        Assert.Equal(SheetLayout.OutputWidth, curated.Width);
        Assert.Equal(SheetLayout.OutputHeight, curated.Height);
    }

    /// <summary>
    /// The whole remap table in one assertion: every clip, every facing, every frame lands on
    /// the cell the contract says it should, carrying the source coordinates it came from.
    /// </summary>
    [Fact]
    public void Curate_PlacesEveryFrameOnItsContractCell()
    {
        using var source = CoordinateStampedSource();
        using var curated = CurateOrFail(source);

        for (var clipIndex = 0; clipIndex < SheetLayout.Clips.Length; clipIndex++)
        {
            var clip = SheetLayout.Clips[clipIndex];

            for (var facing = 0; facing < SheetLayout.FacingCount; facing++)
            {
                var row = SheetLayout.RowFor(clipIndex, facing);

                for (var frame = 0; frame < clip.FrameCount; frame++)
                {
                    var actual = CellCentre(curated, frame, row);

                    Assert.Equal((byte)(clip.SourceColumn + frame), actual.Red);
                    Assert.Equal((byte)facing, actual.Green);
                    Assert.Equal(0xFF, actual.Alpha);
                }
            }
        }
    }

    /// <summary>North is source row 3. Nothing in the curated sheet may carry it.</summary>
    [Fact]
    public void Curate_DropsTheNorthFacing()
    {
        using var source = CoordinateStampedSource();
        using var curated = CurateOrFail(source);

        foreach (var pixel in curated.Pixels)
        {
            if (pixel.Alpha is not 0)
            {
                Assert.NotEqual(3, pixel.Green);
            }
        }
    }

    [Fact]
    public void Curate_LeavesTrailingCellsTransparent_WhenAClipIsShorterThanTheSheet()
    {
        using var source = CoordinateStampedSource();
        using var curated = CurateOrFail(source);

        for (var clipIndex = 0; clipIndex < SheetLayout.Clips.Length; clipIndex++)
        {
            var clip = SheetLayout.Clips[clipIndex];

            for (var facing = 0; facing < SheetLayout.FacingCount; facing++)
            {
                var row = SheetLayout.RowFor(clipIndex, facing);

                for (var frame = clip.FrameCount; frame < SheetLayout.OutputColumns; frame++)
                {
                    Assert.Equal(0, CellCentre(curated, frame, row).Alpha);
                }
            }
        }
    }

    [Fact]
    public void Curate_ReportsSourceGeometryMismatch_WhenGivenTheWrongSize()
    {
        using var wrong = NewBitmap(64, 64);

        var result = SheetBaker.Curate(wrong);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.SourceGeometryMismatch, result.Error);
    }

    [Fact]
    public void Assemble_TakesTheFrontmostOpaquePixel()
    {
        using var back = NewBitmap(SheetLayout.SourceWidth, SheetLayout.SourceHeight);
        using var front = NewBitmap(SheetLayout.SourceWidth, SheetLayout.SourceHeight);

        var backPixels = back.Pixels;
        var frontPixels = front.Pixels;

        Array.Fill(backPixels, new SKColor(0x11, 0x22, 0x33, 0xFF));

        // front covers only the first pixel; everywhere else it is transparent
        frontPixels[0] = new SKColor(0xAA, 0xBB, 0xCC, 0xFF);

        back.Pixels = backPixels;
        front.Pixels = frontPixels;

        var result = SheetBaker.Assemble([back, front]);

        Assert.True(result.IsSuccessful);

        using var assembled = result.Value;

        Assert.Equal(new SKColor(0xAA, 0xBB, 0xCC, 0xFF), assembled.GetPixel(0, 0));
        Assert.Equal(new SKColor(0x11, 0x22, 0x33, 0xFF), assembled.GetPixel(1, 0));
    }

    [Fact]
    public void Assemble_ReportsLayerGeometryMismatch_WhenALayerIsTheWrongSize()
    {
        using var good = NewBitmap(SheetLayout.SourceWidth, SheetLayout.SourceHeight);
        using var bad = NewBitmap(48, 48);

        var result = SheetBaker.Assemble([good, bad]);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.LayerGeometryMismatch, result.Error);
    }

    [Fact]
    public void Assemble_ReportsNoLayersSupplied_WhenGivenNothing()
    {
        var result = SheetBaker.Assemble([]);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.NoLayersSupplied, result.Error);
    }

    [Fact]
    public void Recolor_ReplacesRampColours_AndLeavesEverythingElseAlone()
    {
        var target = SkinRamps.All[3];
        var substitution = target.SubstitutionFrom(SkinRamps.Source);

        using var source = NewBitmap(4, 1);
        var pixels = source.Pixels;

        pixels[0] = SkinRamps.Source.Steps[0];
        pixels[1] = SkinRamps.Source.Steps[3];
        pixels[2] = new SKColor(0x12, 0x34, 0x56, 0xFF);   // not in the ramp
        pixels[3] = SKColors.Transparent;
        source.Pixels = pixels;

        using var recolored = RecolorOrFail(source, substitution);

        Assert.Equal(target.Steps[0], recolored.GetPixel(0, 0));
        Assert.Equal(target.Steps[3], recolored.GetPixel(1, 0));
        Assert.Equal(new SKColor(0x12, 0x34, 0x56, 0xFF), recolored.GetPixel(2, 0));
        Assert.Equal(0, recolored.GetPixel(3, 0).Alpha);
    }

    /// <summary>
    /// The guide proves a recolour is a pure index substitution by showing identical pixel
    /// counts per step across all four human tones. This asserts the same property of our
    /// implementation.
    /// </summary>
    [Fact]
    public void Recolor_PreservesPerStepPixelCounts()
    {
        using var source = NewBitmap(SkinRamps.StepCount * 3, 1);
        var pixels = source.Pixels;

        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = SkinRamps.Source.Steps[i % SkinRamps.StepCount];
        }

        source.Pixels = pixels;

        foreach (var ramp in SkinRamps.All)
        {
            using var recolored = RecolorOrFail(source, ramp.SubstitutionFrom(SkinRamps.Source));

            for (var step = 0; step < SkinRamps.StepCount; step++)
            {
                var expected = CountOf(source, SkinRamps.Source.Steps[step]);
                var actual = CountOf(recolored, ramp.Steps[step]);

                Assert.Equal(expected, actual);
            }
        }
    }

    private static int CountOf(SKBitmap bitmap, SKColor colour)
    {
        var count = 0;

        foreach (var pixel in bitmap.Pixels)
        {
            if (pixel.Alpha is not 0 && SkinRamp.Pack(pixel) == SkinRamp.Pack(colour))
            {
                count++;
            }
        }

        return count;
    }
}
