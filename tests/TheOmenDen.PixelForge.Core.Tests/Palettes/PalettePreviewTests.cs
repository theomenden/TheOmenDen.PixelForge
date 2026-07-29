using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Palettes;

public sealed class PalettePreviewTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    /// <summary>The source column of the idle clip, looked up by name to match production.</summary>
    private static int IdleSourceColumn { get; } = FindIdleSourceColumn();

    private static int FindIdleSourceColumn()
    {
        foreach (var clip in SheetLayout.Clips.AsSpan())
        {
            if (clip.Name == "idle")
            {
                return clip.SourceColumn;
            }
        }

        throw new InvalidOperationException("SheetLayout.Clips has no clip named 'idle'.");
    }

    /// <summary>Differently tints each facing row so a wrong row/facing mapping would show up as
    /// the wrong colour, and gives one row a distinguishable per-channel value.</summary>
    private static SKColor Tint(SKColor fill, int row) => row switch
    {
        1 => new SKColor(fill.Green, fill.Blue, fill.Red),
        2 => new SKColor(fill.Blue, fill.Red, fill.Green),
        _ => new SKColor((byte)~fill.Red, (byte)~fill.Green, (byte)~fill.Blue),
    };

    /// <summary>
    /// A source-geometry partial written as PNG. Row 0 (south, what <c>RenderIdleRow</c> samples
    /// in these tests) is <paramref name="fill"/> throughout except its one corner pixel, which is
    /// set apart so an interpolated upscale blends it with its neighbours — a uniform cell would
    /// leave <c>RenderIdleRow_UpscalesWithoutInterpolating</c> unable to fail on linear filtering.
    /// The other three rows are tinted differently so a wrong facing mapping would be visible too.
    /// </summary>
    private FullPath WritePartial(string name, SKColor fill)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            SheetLayout.SourceWidth, SheetLayout.SourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var pixels = bitmap.Pixels;

        for (var row = 0; row < SheetLayout.SourceRows; row++)
        {
            var tinted = row is 0 ? fill : Tint(fill, row);
            var start = row * SheetLayout.CellSize * bitmap.Width;
            var length = SheetLayout.CellSize * bitmap.Width;

            Array.Fill(pixels, tinted, start, length);
        }

        // Never a ramp colour, so it survives every recolour untouched and contrasts with its
        // fill-coloured neighbours.
        pixels[IdleSourceColumn * SheetLayout.CellSize] = SKColors.Magenta;

        bitmap.Pixels = pixels;

        var path = _directory.FullPath / name;

        using var stream = File.Create(path.Value);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        data.SaveTo(stream);

        return path;
    }

    private PalettePreview CreateOrFail(SKColor fill)
    {
        var recipe = new SheetRecipe
        {
            Name = "preview",
            Layers = [WritePartial("body.png", fill)],
        };

        var created = PalettePreview.Create(recipe);

        Assert.True(created.IsSuccessful, $"create failed with {created.Error}");

        return created.Value;
    }

    [Fact]
    public void RenderIdleRow_ProducesThreeFacingsAtTheRequestedScale()
    {
        using var preview = CreateOrFail(SkinRamps.Source.Steps[3]);

        var rendered = preview.RenderIdleRow(SkinRamps.All[4], scale: 4);

        Assert.True(rendered.IsSuccessful, $"render failed with {rendered.Error}");

        using var image = rendered.Value;

        Assert.Equal(SheetLayout.CellSize * SheetLayout.FacingCount * 4, image.Width);
        Assert.Equal(SheetLayout.CellSize * 4, image.Height);
    }

    [Fact]
    public void RenderIdleRow_AppliesTheRamp()
    {
        var target = SkinRamps.All[4];

        using var preview = CreateOrFail(SkinRamps.Source.Steps[3]);

        var rendered = preview.RenderIdleRow(target, scale: 1);

        Assert.True(rendered.IsSuccessful, $"render failed with {rendered.Error}");

        using var image = rendered.Value;

        Assert.Equal(target.Steps[3], image.GetPixel(SheetLayout.CellSize / 2, SheetLayout.CellSize / 2));
    }

    /// <summary>
    /// The source ramp must render as itself — a substitution from Source to Source is the
    /// identity, and a preview that shifted colour on the default tone would be lying.
    /// </summary>
    [Fact]
    public void RenderIdleRow_IsIdentity_ForTheSourceRamp()
    {
        var fill = SkinRamps.Source.Steps[2];

        using var preview = CreateOrFail(fill);

        var rendered = preview.RenderIdleRow(SkinRamps.Source, scale: 1);

        Assert.True(rendered.IsSuccessful);

        using var image = rendered.Value;

        Assert.Equal(fill, image.GetPixel(SheetLayout.CellSize / 2, SheetLayout.CellSize / 2));
    }

    /// <summary>
    /// Nearest-neighbour upscaling: a scaled pixel block must be one flat colour, never a
    /// gradient. This is what keeps the XAML Image from blurring pixel art.
    /// </summary>
    [Fact]
    public void RenderIdleRow_UpscalesWithoutInterpolating()
    {
        using var preview = CreateOrFail(SkinRamps.Source.Steps[1]);

        var rendered = preview.RenderIdleRow(SkinRamps.Source, scale: 4);

        Assert.True(rendered.IsSuccessful);

        using var image = rendered.Value;

        var first = image.GetPixel(0, 0);

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(first, image.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var preview = CreateOrFail(SkinRamps.Source.Steps[0]);

        preview.Dispose();
        preview.Dispose();

        Assert.Throws<ObjectDisposedException>(() => preview.RenderIdleRow(SkinRamps.Source, 1));
    }

    [Fact]
    public void RenderIdleRow_ThrowsObjectDisposed_AfterDispose()
    {
        var preview = CreateOrFail(SkinRamps.Source.Steps[0]);

        preview.Dispose();

        Assert.Throws<ObjectDisposedException>(() => preview.RenderIdleRow(SkinRamps.Source, 1));
    }

    [Fact]
    public void Create_IgnoresTheRecipeRecolour_SoAnyRampCanBeSubstitutedLater()
    {
        var fill = SkinRamps.Source.Steps[3];

        var recipe = new SheetRecipe
        {
            Name = "pre-toned",
            Layers = [WritePartial("body.png", fill)],
            Recolor = SkinRamps.All[5],
        };

        var created = PalettePreview.Create(recipe);

        Assert.True(created.IsSuccessful, $"create failed with {created.Error}");

        using var preview = created.Value;

        var rendered = preview.RenderIdleRow(SkinRamps.Source, scale: 1);

        Assert.True(rendered.IsSuccessful);

        using var image = rendered.Value;

        // The cache must hold source-toned pixels — rendering with Source is the identity.
        Assert.Equal(fill, image.GetPixel(SheetLayout.CellSize / 2, SheetLayout.CellSize / 2));
    }

    [Fact]
    public void Create_ReportsLayerNotFound_WhenAPartialIsMissing()
    {
        var created = PalettePreview.Create(new SheetRecipe
        {
            Name = "absent",
            Layers = [_directory.FullPath / "nope.png"],
        });

        Assert.False(created.IsSuccessful);
        Assert.Equal(BakeFailure.LayerNotFound, created.Error);
    }
}
