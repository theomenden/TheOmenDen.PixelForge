using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// Full geometry writes the assembly untouched, which is the whole point: no remap means no
/// frames dropped, so the nock/bow draw, climb and the north facing all survive.
/// </summary>
public sealed class FullGeometryTests
{
    private static FullPath WriteLayer(FullPath directory, string name)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            SheetLayout.SourceWidth, SheetLayout.SourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        bitmap.Erase(new SKColor(0x20, 0x40, 0x60, 0xFF));

        var path = directory / (name + ".png");

        using var stream = File.Create(path.Value);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        data.SaveTo(stream);

        return path;
    }

    private static SheetRecipe Recipe(FullPath layer, SheetGeometry geometry) => new()
    {
        Name = "probe",
        Layers = [new(layer, IsSkin: false)],
        Geometry = geometry,
    };

    [Fact]
    public void Bake_WritesSourceGeometry_WhenTheRecipeAsksForFull()
    {
        using var root = TemporaryDirectory.Create();

        var layer = WriteLayer(root.FullPath, "body");
        var baked = RecipeBaker.Bake(Recipe(layer, SheetGeometry.Full));

        Assert.True(baked.IsSuccessful, $"bake failed with {baked.Error}");

        using var sheet = baked.Value;
        using var decoded = SKBitmap.Decode(sheet.GetBuffer().AsSpan(0, (int)sheet.Length).ToArray());

        Assert.Equal(SheetLayout.SourceWidth, decoded.Width);
        Assert.Equal(SheetLayout.SourceHeight, decoded.Height);
    }

    [Fact]
    public void Bake_WritesContractGeometry_WhenTheRecipeAsksForCurated()
    {
        using var root = TemporaryDirectory.Create();

        var layer = WriteLayer(root.FullPath, "body");
        var baked = RecipeBaker.Bake(Recipe(layer, SheetGeometry.Curated));

        Assert.True(baked.IsSuccessful, $"bake failed with {baked.Error}");

        using var sheet = baked.Value;
        using var decoded = SKBitmap.Decode(sheet.GetBuffer().AsSpan(0, (int)sheet.Length).ToArray());

        Assert.Equal(SheetLayout.OutputWidth, decoded.Width);
        Assert.Equal(SheetLayout.OutputHeight, decoded.Height);
    }

    /// <summary>Curated is the default, so an unset geometry cannot silently change the contract.</summary>
    [Fact]
    public void Geometry_DefaultsToCurated()
    {
        var recipe = new SheetRecipe { Name = "probe", Layers = [] };

        Assert.Equal(SheetGeometry.Curated, recipe.Geometry);
    }
}
