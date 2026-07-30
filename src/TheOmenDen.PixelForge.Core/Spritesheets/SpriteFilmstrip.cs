using CommunityToolkit.Diagnostics;
using CommunityToolkit.HighPerformance;
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
    /// <summary>The conventional pose: facing south, the <c>stand</c> column.</summary>
    private const int StillFacing = 0;

    private const int StillColumn = 1;

    /// <summary>
    /// Alpha mask for a pixel read as a little-endian <see langword="uint"/>.
    /// </summary>
    /// <remarks>
    /// RGBA8888 lays out R,G,B,A in ascending addresses, so that read is <c>0xAABBGGRR</c> and
    /// alpha is the <em>high</em> byte. Masking the low byte would be testing red — the same trap
    /// <see cref="Palettes.SkinRamp.PackedRgba"/> documents.
    /// </remarks>
    private const uint Opaque = 0xFF000000u;

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

    /// <summary>
    /// A cell that actually has something in it, for use as a still.
    /// </summary>
    /// <param name="scale">Nearest-neighbour multiplier.</param>
    /// <returns>The still, or the failure the crop hit. Never a fully transparent cell unless the
    /// whole sheet is empty.</returns>
    /// <remarks>
    /// <para>
    /// Taking the conventional pose — facing south, the <c>stand</c> column — and calling it the
    /// thumbnail is wrong for 47 of the shipped partials, and wrong in a way that looks like a
    /// broken decoder rather than a content fact. Tails and back hair draw <em>behind</em> the body,
    /// so facing south the body hides them entirely; a bow, an arrow and a pickaxe only exist on the
    /// clips that wield them, so at <c>stand</c> the character is not holding anything.
    /// </para>
    /// <para>
    /// So the conventional pose is preferred and then fallen back on: the first cell carrying any
    /// opaque pixel wins, scanned facing by facing. That keeps the 948 ordinary partials looking
    /// consistent while giving the other 47 a thumbnail of the thing they actually are.
    /// </para>
    /// </remarks>
    public Result<SKBitmap, BakeFailure> RenderStill(int scale)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        var (facing, column) = FindContent();

        return RenderCell(facing, column, scale);
    }

    /// <summary>
    /// The conventional pose if it carries pixels, else the first cell that does.
    /// </summary>
    /// <remarks>
    /// Reads the assembly's pixel memory once through <see cref="Span2D{T}"/> rather than cropping
    /// 92 candidate cells and testing each — <c>SKBitmap.GetPixel</c> in a loop over a 1104x192
    /// sheet is exactly the per-pixel call this project avoids.
    /// </remarks>
    private unsafe (int Facing, int Column) FindContent()
    {
        using var pixmap = _assembly.PeekPixels();

        if (pixmap is null)
        {
            return (StillFacing, StillColumn);
        }

        var pitch = (_assembly.RowBytes / SheetLayout.BytesPerPixel) - _assembly.Width;
        var grid = new Span2D<uint>((void*)_assembly.GetPixels(), _assembly.Height, _assembly.Width, pitch);

        if (HasContent(grid, StillFacing, StillColumn))
        {
            return (StillFacing, StillColumn);
        }

        for (var facing = 0; facing < SheetLayout.SourceRows; facing++)
        {
            for (var column = 0; column < SheetLayout.SourceColumns; column++)
            {
                if (HasContent(grid, facing, column))
                {
                    return (facing, column);
                }
            }
        }

        return (StillFacing, StillColumn);
    }

    /// <summary>Whether any pixel of one cell is not fully transparent.</summary>
    /// <remarks>
    /// Alpha is the high byte: RGBA8888 lays out R,G,B,A in ascending addresses, so a little-endian
    /// <see langword="uint"/> read of that memory is <c>0xAABBGGRR</c>. Testing the low byte would
    /// be testing red.
    /// </remarks>
    private static bool HasContent(Span2D<uint> grid, int facing, int column)
    {
        var cell = grid.Slice(
            facing * SheetLayout.CellSize,
            column * SheetLayout.CellSize,
            SheetLayout.CellSize,
            SheetLayout.CellSize);

        foreach (var pixel in cell)
        {
            if ((pixel & Opaque) is not 0)
            {
                return true;
            }
        }

        return false;
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
