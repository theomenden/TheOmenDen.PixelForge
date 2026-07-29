using CommunityToolkit.Diagnostics;
using DotNext;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// Renders a recoloured sprite for the palette editor, fast enough to keep up with a colour
/// picker being dragged.
/// <para>
/// The body is baked <em>once</em>, un-recoloured, and the curated result is cached. Each render
/// then applies only the five-colour substitution, so changing a ramp step costs a pass over one
/// small crop rather than a decode, composite and curate.
/// </para>
/// <para>
/// The recolour happens <em>after</em> the curate here, the reverse of the export path. Both
/// operations are pixel-local, so they commute — the output is identical, and cropping first
/// means the substitution walks 6,912 pixels instead of 211,968.
/// </para>
/// <para>
/// Upscaling is nearest-neighbour, because WinUI 3's <c>Image</c> has no interpolation-mode
/// switch: scaling here is the only way to stop the platform blurring pixel art.
/// </para>
/// </summary>
public sealed class PalettePreview : Disposable
{
    /// <summary>Frame 0 of the idle clip on all three facings — the three faces, side by side.</summary>
    public const int IdleRowWidth = SheetLayout.CellSize * SheetLayout.FacingCount;

    public const int IdleRowHeight = SheetLayout.CellSize;

    private readonly SKBitmap _curated;

    private PalettePreview(SKBitmap curated) => _curated = curated;

    /// <summary>
    /// Index of the idle clip. Looked up by name rather than hard-coded, so reordering
    /// <see cref="SheetLayout.Clips"/> cannot silently point the preview at a different animation.
    /// </summary>
    private static int IdleClipIndex { get; } = FindClip("idle");

    private static int FindClip(string name)
    {
        for (var i = 0; i < SheetLayout.Clips.Length; i++)
        {
            if (SheetLayout.Clips[i].Name == name)
            {
                return i;
            }
        }

        return ThrowHelper.ThrowInvalidOperationException<int>($"SheetLayout.Clips has no clip named '{name}'.");
    }

    /// <summary>
    /// Bakes <paramref name="body"/> without its recolour and caches the curated sheet.
    /// <see cref="SheetRecipe.Recolor"/> is ignored on purpose: the cache must hold source-toned
    /// pixels so any ramp can be substituted in later.
    /// </summary>
    public static Result<PalettePreview, BakeFailure> Create(SheetRecipe body)
    {
        var assembly = RecipeBaker.AssembleLayers(body);

        if (!assembly.TryGet(out var assembled))
        {
            return new(assembly.Error);
        }

        using (assembled)
        {
            var curation = SheetBaker.Curate(assembled);

            if (!curation.TryGet(out var curated))
            {
                return new(curation.Error);
            }

            return new PalettePreview(curated);
        }
    }

    /// <summary>
    /// The idle frame on all three facings, recoloured into <paramref name="ramp"/> and scaled up
    /// by <paramref name="scale"/>. The returned bitmap is the caller's to dispose.
    /// </summary>
    public Result<SKBitmap, BakeFailure> RenderIdleRow(SkinRamp ramp, int scale)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Guard.IsNotNull(ramp);
        Guard.IsGreaterThan(scale, 0);

        var cropped = CropIdleRow();

        if (!cropped.TryGet(out var crop))
        {
            return cropped;
        }

        SKBitmap? toned = null;

        try
        {
            using (crop)
            {
                var recolored = SheetBaker.Recolor(crop, ramp.SubstitutionFrom(SkinRamps.Source));

                if (!recolored.TryGet(out toned))
                {
                    return new(recolored.Error);
                }
            }

            if (scale is 1)
            {
                var result = toned;

                toned = null;

                return result;
            }

            return Upscale(toned, scale);
        }
        finally
        {
            toned?.Dispose();
        }
    }

    /// <summary>
    /// Copies frame 0 of the idle clip from each facing row into a three-cell strip. Drawn with
    /// <see cref="SKCanvas"/> rather than hand-rolled stride arithmetic.
    /// </summary>
    private Result<SKBitmap, BakeFailure> CropIdleRow()
    {
        using var strip = new SKBitmap(new SKImageInfo(
            IdleRowWidth, IdleRowHeight, SKColorType.Rgba8888, SKAlphaType.Premul));

        using (var canvas = new SKCanvas(strip))
        {
            canvas.Clear(SKColors.Transparent);

            for (var facing = 0; facing < SheetLayout.FacingCount; facing++)
            {
                var sourceRow = SheetLayout.RowFor(IdleClipIndex, facing);

                var source = SKRect.Create(
                    0,
                    sourceRow * SheetLayout.CellSize,
                    SheetLayout.CellSize,
                    SheetLayout.CellSize);

                var destination = SKRect.Create(
                    facing * SheetLayout.CellSize,
                    0,
                    SheetLayout.CellSize,
                    SheetLayout.CellSize);

                canvas.DrawBitmap(_curated, source, destination, SheetBaker.PixelExact);
            }
        }

        return SheetBaker.ToCanonical(strip);
    }

    private static Result<SKBitmap, BakeFailure> Upscale(SKBitmap source, int scale)
    {
        using var scaled = new SKBitmap(new SKImageInfo(
            source.Width * scale, source.Height * scale, SKColorType.Rgba8888, SKAlphaType.Premul));

        using (var canvas = new SKCanvas(scaled))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(
                source,
                SKRect.Create(0, 0, source.Width, source.Height),
                SKRect.Create(0, 0, scaled.Width, scaled.Height),
                SheetBaker.PixelExact);
        }

        return SheetBaker.ToCanonical(scaled);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _curated.Dispose();
        }

        base.Dispose(disposing);
    }
}
