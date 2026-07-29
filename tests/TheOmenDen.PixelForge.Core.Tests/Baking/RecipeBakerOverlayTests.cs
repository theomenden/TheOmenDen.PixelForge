using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// Overlays are drawn after the recolour, which is what makes flattening safe. RoostSheets
/// names hair1 and hat4 as partials that use skin-ramp hexes as hair and trim: composite
/// those before the substitution and the recolour rewrites them.
/// </summary>
public sealed class RecipeBakerOverlayTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    /// <summary>A source-geometry partial filled with one colour, written as PNG.</summary>
    private FullPath WritePartial(string name, SKColor fill)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            SheetLayout.SourceWidth, SheetLayout.SourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var pixels = bitmap.Pixels;
        Array.Fill(pixels, fill);
        bitmap.Pixels = pixels;

        var path = _directory.FullPath / name;

        using var stream = File.Create(path.Value);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        data.SaveTo(stream);

        return path;
    }

    /// <summary>
    /// The body is painted in a source ramp step, the overlay in the SAME step. After the bake
    /// the body must have moved to the target ramp and the overlay must not have moved at all.
    /// </summary>
    [Fact]
    public void Bake_LeavesOverlayColoursUntouched_WhenTheyCollideWithTheSourceRamp()
    {
        var collidingStep = SkinRamps.Source.Steps[3];
        var target = SkinRamps.All[4];

        var body = WritePartial("body.png", collidingStep);
        var overlay = WritePartial("overlay.png", collidingStep);

        // The overlay covers the whole sheet, so every visible pixel comes from it.
        var recipe = new SheetRecipe
        {
            Name = "collide",
            Layers = [body],
            Recolor = target,
            Overlays = [overlay],
        };

        var baked = RecipeBaker.Bake(recipe);

        Assert.True(baked.IsSuccessful, $"bake failed with {baked.Error}");

        using var stream = baked.Value;
        using var decoded = SKBitmap.Decode(
            stream.GetBuffer().AsSpan(0, (int)stream.Length),
            new SKImageInfo(SheetLayout.OutputWidth, SheetLayout.OutputHeight,
                SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var actual = decoded.GetPixel(SheetLayout.CellSize / 2, SheetLayout.CellSize / 2);

        Assert.Equal(collidingStep, actual);
        Assert.NotEqual(target.Steps[3], actual);
    }

    /// <summary>An overlay of the wrong geometry is bad input, not a bug.</summary>
    [Fact]
    public void Bake_ReportsLayerGeometryMismatch_WhenAnOverlayIsTheWrongSize()
    {
        var body = WritePartial("body.png", SkinRamps.Source.Steps[0]);

        using var small = new SKBitmap(new SKImageInfo(48, 48, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var wrong = _directory.FullPath / "wrong.png";

        using (var stream = File.Create(wrong.Value))
        using (var image = SKImage.FromBitmap(small))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            data.SaveTo(stream);
        }

        var result = RecipeBaker.Bake(new SheetRecipe
        {
            Name = "bad-overlay",
            Layers = [body],
            Overlays = [wrong],
        });

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.LayerGeometryMismatch, result.Error);
    }

    /// <summary>No overlays is the existing layered path and must be unchanged.</summary>
    [Fact]
    public void Bake_RecoloursNormally_WhenThereAreNoOverlays()
    {
        var target = SkinRamps.All[4];
        var body = WritePartial("body.png", SkinRamps.Source.Steps[3]);

        var baked = RecipeBaker.Bake(new SheetRecipe
        {
            Name = "plain",
            Layers = [body],
            Recolor = target,
        });

        Assert.True(baked.IsSuccessful, $"bake failed with {baked.Error}");

        using var stream = baked.Value;
        using var decoded = SKBitmap.Decode(
            stream.GetBuffer().AsSpan(0, (int)stream.Length),
            new SKImageInfo(SheetLayout.OutputWidth, SheetLayout.OutputHeight,
                SKColorType.Rgba8888, SKAlphaType.Unpremul));

        Assert.Equal(target.Steps[3], decoded.GetPixel(SheetLayout.CellSize / 2, SheetLayout.CellSize / 2));
    }
}
