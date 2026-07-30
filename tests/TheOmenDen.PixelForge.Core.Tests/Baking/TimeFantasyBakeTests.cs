using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// Baking Time Fantasy art, whose source palette is not the one every Time Elements partial is
/// authored in.
/// <para>
/// The baker used to build every substitution as <c>ramp.SubstitutionFrom(SkinRamps.Source)</c>.
/// Against Time Fantasy art that table lists colours the art does not contain, so the recolour
/// matches nothing and the sheet comes out in its authored palette — silently, which is the whole
/// failure this pack was known to risk and is worse for happening inside our own baker.
/// </para>
/// </summary>
public sealed class TimeFantasyBakeTests : IDisposable
{
    private readonly TemporaryDirectory _root = TemporaryDirectory.Create();

    public void Dispose() => _root.Dispose();

    /// <summary>A Time-Fantasy-sized sheet filled with one of its own palette colours.</summary>
    private FullPath WriteSheet(string name, SKColor fill)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            TimeFantasyLayout.SheetWidth,
            TimeFantasyLayout.SheetHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul));

        bitmap.Erase(fill);

        var path = _root.FullPath / (name + ".png");

        using var stream = File.Create(path.Value);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        data.SaveTo(stream);

        return path;
    }

    /// <summary>
    /// The recipe has to say which pack its art comes from, because that is what names the palette
    /// the substitution reads <em>from</em>. The tone only names what it goes to.
    /// </summary>
    [Fact]
    public void AssembleLayers_RecoloursTimeFantasyArtThroughItsOwnPalette()
    {
        // Tone 4 is green, so a shade that failed to be remapped stays obviously tan.
        var target = SkinRamps.All[4];

        var recipe = new SheetRecipe
        {
            Name = "probe",
            Geometry = SheetGeometry.Full,
            Format = SheetFormat.Png,
            Pack = SourcePack.TimeFantasy,
            Tone = target,
            Layers = [new(WriteSheet("body", TimeFantasyRamps.Skin[1]), IsSkin: true)],
        };

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.True(result.IsSuccessful, $"assemble failed with {result.Error}");

        using var assembled = result.Value;

        // Time Fantasy skin step 1 lands on Time Elements step 1.
        Assert.Equal(target.Steps[TimeFantasyRamps.TargetSteps[1]], assembled.GetPixel(0, 0));
    }

    /// <summary>
    /// The outline is the entry that is exact, and it is also the one a reader is most likely to
    /// assume is handled by the skin table alone.
    /// </summary>
    [Fact]
    public void AssembleLayers_SendsTheTimeFantasyOutlineToBlack()
    {
        var recipe = new SheetRecipe
        {
            Name = "probe",
            Geometry = SheetGeometry.Full,
            Format = SheetFormat.Png,
            Pack = SourcePack.TimeFantasy,
            Tone = SkinRamps.All[4],
            Layers = [new(WriteSheet("outline", TimeFantasyRamps.Outline), IsSkin: true)],
        };

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.True(result.IsSuccessful, $"assemble failed with {result.Error}");

        using var assembled = result.Value;

        Assert.Equal(SKColors.Black, assembled.GetPixel(0, 0));
    }

    /// <summary>
    /// Silence still means Time Elements. A recipe that says nothing about its pack must keep
    /// behaving exactly as every existing one does.
    /// </summary>
    [Fact]
    public void Pack_WhenUnstated_IsTimeElements()
    {
        var recipe = new SheetRecipe { Name = "probe", Layers = [] };

        Assert.Equal(SourcePack.TimeElements, recipe.Pack);
    }

    /// <summary>
    /// The Time Fantasy root stays absent unless set, and setting it is not required to construct
    /// a <see cref="SourcePacks"/>. Making it required would break every existing construction
    /// site; folding it into the three-pack readiness gate would blank the app for every user who
    /// does not own this pack.
    /// </summary>
    [Fact]
    public void FantasyRoot_IsAbsentUnlessConfigured()
    {
        var packs = new SourcePacks
        {
            CoreAssets = _root.FullPath,
            Expansion1Assets = _root.FullPath,
            Expansion2Assets = _root.FullPath,
        };

        Assert.False(packs.FantasyRoot.HasValue);

        var configured = packs with { FantasyRoot = _root.FullPath };

        Assert.True(configured.FantasyRoot.TryGet(out var root));
        Assert.Equal(_root.FullPath, root);
    }
}
