using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>
/// One assembled sheet, with any single cell of it renderable on demand.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately holds the <em>source</em> assembly rather than a curated sheet. Curating drops the
/// north facing and eight of the twelve generator clips, and left-aligns what remains — all fine
/// for a shipped artifact, and all wrong for looking at the art. Source geometry keeps every
/// column and row, so <see cref="GeneratorClips"/> addresses it directly: a frame is the cell at
/// (facing, <see cref="GeneratorClip.Frames"/>[n]), and playback order comes out right, repeats
/// and all.
/// </para>
/// <para>
/// The counterpart of <see cref="Palettes.PalettePreview"/>, which caches a curated sheet because
/// it exists to preview a recolour. This one caches an assembly because it exists to preview
/// motion. Both crop with <see cref="SKCanvas"/> and enlarge through
/// <see cref="SheetBaker.Upscale"/>, so neither can blur pixel art the other keeps sharp.
/// </para>
/// <para>
/// <b>It holds 828 KiB.</b> A 1104x192 RGBA assembly is not something to keep one of per catalogue
/// entry — 995 of them would be most of a gigabyte. Callers rendering a grid of stills should
/// create, render, and dispose per entry, caching only the small bitmap that comes out.
/// </para>
/// </remarks>
public sealed class SpriteFilmstrip : Disposable
{
    private readonly SKBitmap _assembly;

    private SpriteFilmstrip(SKBitmap assembly) => _assembly = assembly;

    /// <summary>
    /// Assembles <paramref name="recipe"/> and keeps the result for cropping.
    /// </summary>
    /// <param name="recipe">What to assemble. One layer or ten, recoloured or not.</param>
    /// <returns>
    /// The filmstrip, which the caller owns and must dispose, or the <see cref="BakeFailure"/> the
    /// assemble hit — a partial missing from disk being the ordinary one.
    /// </returns>
    /// <remarks>
    /// Goes through <see cref="RecipeBaker.AssembleLayers"/>, the same call the baker makes, so a
    /// preview cannot show something the export would not produce. The tone on the recipe is
    /// honoured rather than cleared, unlike <see cref="Palettes.PalettePreview"/> — there is no
    /// recolour to apply later here, so what you ask for is what you see.
    /// </remarks>
    public static Result<SpriteFilmstrip, BakeFailure> Create(SheetRecipe recipe)
    {
        Guard.IsNotNull(recipe);

        var assembly = RecipeBaker.AssembleLayers(recipe);

        if (!assembly.TryGet(out var assembled))
        {
            return new(assembly.Error);
        }

        return new SpriteFilmstrip(assembled);
    }

    /// <summary>
    /// Opens a single partial straight from disk, without compositing it.
    /// </summary>
    /// <param name="path">The <c>.png</c> to read.</param>
    /// <returns>
    /// The filmstrip, which the caller owns and must dispose, or
    /// <see cref="BakeFailure.LayerNotFound"/>, <see cref="BakeFailure.LayerUnreadable"/> or
    /// <see cref="BakeFailure.LayerGeometryMismatch"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The cheap path, for showing what one file contains. <see cref="Create"/> goes through
    /// <see cref="RecipeBaker.AssembleLayers"/>, which composites onto a fresh surface and converts
    /// again afterwards — three 828 KiB bitmaps to produce one. That is the right price for a
    /// preview of a <em>bake</em>, and the wrong price for a thumbnail of a <em>file</em>, which is
    /// a crop of the decode and nothing more.
    /// </para>
    /// <para>
    /// Decoding is <see cref="SKBitmap.Decode(string)"/>, which streams from disk. Reading through
    /// a pooled stream first was considered and rejected: it would hold the compressed bytes and
    /// the decoded bitmap at once, which is more memory rather than less. Pooling earns its keep on
    /// the encode side, where <see cref="LosslessWebp"/> already uses it.
    /// </para>
    /// </remarks>
    public static Result<SpriteFilmstrip, BakeFailure> Open(FullPath path)
    {
        if (!File.Exists(path.Value))
        {
            return new(BakeFailure.LayerNotFound);
        }

        using var decoded = SKBitmap.Decode(path.Value);

        if (decoded is null)
        {
            return new(BakeFailure.LayerUnreadable);
        }

        if (decoded.Width != SheetLayout.SourceWidth || decoded.Height != SheetLayout.SourceHeight)
        {
            return new(BakeFailure.LayerGeometryMismatch);
        }

        var canonical = SheetBaker.ToCanonical(decoded);

        if (!canonical.TryGet(out var assembly))
        {
            return new(canonical.Error);
        }

        return new SpriteFilmstrip(assembly);
    }

    /// <summary>
    /// One cell, enlarged. The returned bitmap is the caller's to dispose.
    /// </summary>
    /// <param name="facing">Source row, indexing <see cref="GeneratorClips.Facings"/>.</param>
    /// <param name="column">Source column, from a clip's <see cref="GeneratorClip.Frames"/>.</param>
    /// <param name="scale">Nearest-neighbour multiplier.</param>
    /// <returns>
    /// The cell at <paramref name="scale"/>, or <see cref="BakeFailure.SourceGeometryMismatch"/>
    /// when the coordinates fall outside the sheet.
    /// </returns>
    /// <remarks>
    /// Out-of-range coordinates are a <see cref="Result{T, TError}"/> rather than an exception
    /// because a clip table and a sheet can legitimately disagree — a pack revision that dropped a
    /// column should show a caller an empty frame, not tear down the page.
    /// </remarks>
    public Result<SKBitmap, BakeFailure> RenderCell(int facing, int column, int scale)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Guard.IsGreaterThan(scale, 0);

        if (facing < 0
            || facing >= SheetLayout.SourceRows
            || column < 0
            || column >= SheetLayout.SourceColumns)
        {
            return new(BakeFailure.SourceGeometryMismatch);
        }

        var cropped = Crop(facing, column);

        if (!cropped.TryGet(out var cell))
        {
            return cropped;
        }

        using (cell)
        {
            return SheetBaker.Upscale(cell, scale);
        }
    }

    /// <summary>Copies one cell out of the assembly, drawn rather than stride-arithmetic'd.</summary>
    private Result<SKBitmap, BakeFailure> Crop(int facing, int column)
    {
        using var cell = new SKBitmap(new SKImageInfo(
            SheetLayout.CellSize, SheetLayout.CellSize, SKColorType.Rgba8888, SKAlphaType.Premul));

        using (var canvas = new SKCanvas(cell))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(
                _assembly,
                SKRect.Create(
                    column * SheetLayout.CellSize,
                    facing * SheetLayout.CellSize,
                    SheetLayout.CellSize,
                    SheetLayout.CellSize),
                SKRect.Create(0, 0, SheetLayout.CellSize, SheetLayout.CellSize),
                SheetBaker.PixelExact);
        }

        return SheetBaker.ToCanonical(cell);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _assembly.Dispose();
        }

        base.Dispose(disposing);
    }
}
