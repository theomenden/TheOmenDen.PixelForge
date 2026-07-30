using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The invariant compositing actually has: layers must agree with <em>each other</em>.
/// <para>
/// Three places used to assert Time Elements' 1104x192 instead — <see cref="SheetBaker.Assemble"/>,
/// <c>RecipeBaker.Prepare</c> and <c>LayerComposite</c>'s surface — which meant no other pack's art
/// could be assembled at all. Whether a finished assembly suits a geometry is
/// <see cref="SheetBaker.Curate"/>'s question, and it still asks it.
/// </para>
/// <para>
/// These drive the recipe path rather than the borrowed-bitmap overload, because that path builds
/// its surface lazily from the first decoded layer and is therefore the one carrying new logic.
/// </para>
/// </summary>
public sealed class AssembleGeometryTests : IDisposable
{
    private readonly TemporaryDirectory _root = TemporaryDirectory.Create();

    public void Dispose() => _root.Dispose();

    /// <summary>Writes a PNG of the given size, filled so the composite has something to draw.</summary>
    private FullPath WriteLayer(string name, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        bitmap.Erase(new SKColor(0x20, 0x40, 0x60, 0xFF));

        var path = _root.FullPath / (name + ".png");

        using var stream = File.Create(path.Value);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        data.SaveTo(stream);

        return path;
    }

    private static SheetRecipe Recipe(params FullPath[] layers) => new()
    {
        Name = "probe",
        Geometry = SheetGeometry.Full,
        Layers = [.. layers.Select(static path => new AssetLayer(path, IsSkin: false))],
    };

    /// <summary>
    /// Time Fantasy's diagonal sheet is 156x144 on a 26x36 grid. Nothing about compositing cares,
    /// and after this nothing about the assembler does either.
    /// </summary>
    [Fact]
    public void AssembleLayers_AcceptsAConsistentGeometryThatIsNotTimeElements()
    {
        var recipe = Recipe(WriteLayer("back", 156, 144), WriteLayer("front", 156, 144));

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.True(result.IsSuccessful, $"assemble failed with {result.Error}");

        using var assembled = result.Value;

        Assert.Equal(156, assembled.Width);
        Assert.Equal(144, assembled.Height);
    }

    /// <summary>
    /// The narrowed check must not have become no check. The first layer fixes the geometry and a
    /// later one that disagrees is still refused — which is the guarantee that used to come from
    /// comparing every layer against a constant.
    /// </summary>
    [Fact]
    public void AssembleLayers_ReportsLayerGeometryMismatch_WhenALaterLayerDisagrees()
    {
        var recipe = Recipe(WriteLayer("back", 156, 144), WriteLayer("front", 78, 144));

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.LayerGeometryMismatch, result.Error);
    }

    /// <summary>
    /// Curate is still the guardian of Time Elements' grid. A consistent assembly that is not that
    /// grid now reaches it — and is refused there, by name, rather than earlier and generically.
    /// </summary>
    [Fact]
    public void Curate_StillRefusesAnAssemblyThatIsNotTheTimeElementsGrid()
    {
        var recipe = Recipe(WriteLayer("only", 156, 144));

        var assembled = RecipeBaker.AssembleLayers(recipe);

        Assert.True(assembled.IsSuccessful, $"assemble failed with {assembled.Error}");

        using var bitmap = assembled.Value;

        var curated = SheetBaker.Curate(bitmap);

        Assert.False(curated.IsSuccessful);
        Assert.Equal(BakeFailure.SourceGeometryMismatch, curated.Error);
    }
}
