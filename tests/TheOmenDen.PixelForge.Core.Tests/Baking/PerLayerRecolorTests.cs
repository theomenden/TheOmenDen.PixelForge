using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The reason the substitution moved off the flattened assembly and onto individual layers.
/// <para>
/// Against the full library a whole-assembly recolour is simply wrong: 23 of 28 tops draw bare
/// arms and hands, so skin lives on the top layer, while hats and hair legitimately use the same
/// hexes as trim and highlights. Only a per-layer rule can recolour the first and spare the
/// second — and it is also the only formulation that handles back-hair, which draws *below* the
/// body and so could never have been an after-the-fact overlay.
/// </para>
/// </summary>
public sealed class PerLayerRecolorTests
{
    /// <summary>Writes a source-geometry PNG filled with one colour.</summary>
    private static FullPath WriteLayer(FullPath directory, string name, SKColor fill)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            SheetLayout.SourceWidth, SheetLayout.SourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        bitmap.Erase(fill);

        var path = directory / (name + ".png");

        using var stream = File.Create(path.Value);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        data.SaveTo(stream);

        return path;
    }

    [Fact]
    public void AssembleLayers_RecoloursOnlySkinBearingLayers()
    {
        using var root = TemporaryDirectory.Create();

        var rampColour = SkinRamps.Source.Steps[1];

        // Both layers are painted the *same* ramp colour. Only the one marked IsSkin may change.
        var skin = WriteLayer(root.FullPath, "top", rampColour);
        var authored = WriteLayer(root.FullPath, "hat", rampColour);

        var target = SkinRamps.All[4];

        var recipe = new SheetRecipe
        {
            Name = "probe",
            Tone = target,
            Layers =
            [
                new(skin, IsSkin: true),
                new(authored, IsSkin: false),
            ],
        };

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.True(result.IsSuccessful, $"assemble failed with {result.Error}");

        using var assembled = result.Value;

        // The hat is drawn last and is opaque, so what survives on top is the *authored* colour.
        Assert.Equal(rampColour, assembled.GetPixel(0, 0));
    }

    [Fact]
    public void AssembleLayers_RecoloursASkinLayerWhenNothingCoversIt()
    {
        using var root = TemporaryDirectory.Create();

        var rampColour = SkinRamps.Source.Steps[1];
        var skin = WriteLayer(root.FullPath, "top", rampColour);
        var target = SkinRamps.All[4];

        var recipe = new SheetRecipe
        {
            Name = "probe",
            Tone = target,
            Layers = [new(skin, IsSkin: true)],
        };

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.True(result.IsSuccessful, $"assemble failed with {result.Error}");

        using var assembled = result.Value;

        Assert.Equal(target.Steps[1], assembled.GetPixel(0, 0));
    }

    [Fact]
    public void AssembleLayers_LeavesEverythingAlone_WhenNoToneIsChosen()
    {
        using var root = TemporaryDirectory.Create();

        var rampColour = SkinRamps.Source.Steps[1];
        var skin = WriteLayer(root.FullPath, "top", rampColour);

        var recipe = new SheetRecipe
        {
            Name = "probe",
            Layers = [new(skin, IsSkin: true)],
        };

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.True(result.IsSuccessful, $"assemble failed with {result.Error}");

        using var assembled = result.Value;

        Assert.Equal(rampColour, assembled.GetPixel(0, 0));
    }

    [Fact]
    public void AssembleLayers_ReportsLayerNotFound_WhenAPartialIsMissing()
    {
        using var root = TemporaryDirectory.Create();

        var recipe = new SheetRecipe
        {
            Name = "probe",
            Layers = [new(root.FullPath / "absent.png", IsSkin: true)],
        };

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.LayerNotFound, result.Error);
    }

    [Fact]
    public void AssembleLayers_ReportsNoLayersSupplied_WhenTheRecipeIsEmpty()
    {
        var recipe = new SheetRecipe
        {
            Name = "probe",
            Layers = [],
        };

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.NoLayersSupplied, result.Error);
    }
}
