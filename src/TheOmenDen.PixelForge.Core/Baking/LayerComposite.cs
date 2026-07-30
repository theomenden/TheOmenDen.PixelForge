using DotNext;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// A source-geometry composite surface that takes layers one at a time.
/// <para>
/// This exists so a caller never has to hold every decoded layer at once. A canonical source
/// partial is 1104x192x4 = 828 KiB, so a stack that decoded all of them first peaked at
/// <c>layers x 828 KiB</c> — 9.7 MiB for the full ten-slot stack, multiplied again by
/// <c>MaxDegreeOfParallelism</c> across a batch run. Drawing and releasing one at a time makes
/// that peak flat in stack depth: the surface, one layer, and the converted result.
/// </para>
/// <para>
/// The surface is premultiplied because that is what Skia draws into;
/// <see cref="Flatten"/> converts to the unpremultiplied canonical format. The source art is
/// strictly binary alpha, so that round trip is exact rather than merely close.
/// </para>
/// </summary>
/// <remarks>
/// Both <see cref="SheetBaker.Assemble"/> and <see cref="RecipeBaker.AssembleLayers"/> go through
/// this rather than each standing up their own canvas, so the clear, the sampling mode and the
/// premultiplied round trip cannot drift apart between the two.
/// </remarks>
internal sealed class LayerComposite : Disposable
{
    private readonly SKBitmap _surface;
    private readonly SKCanvas _canvas;

    public LayerComposite()
    {
        _surface = new SKBitmap(new SKImageInfo(
            SheetLayout.SourceWidth,
            SheetLayout.SourceHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));

        _canvas = new SKCanvas(_surface);
        _canvas.Clear(SKColors.Transparent);
    }

    /// <summary>
    /// Draws one layer over what is already there. The layer is only read, so the caller is free
    /// to dispose it the moment this returns — which is the whole point of the type.
    /// </summary>
    public void Draw(SKBitmap layer) => _canvas.DrawBitmap(layer, 0, 0, SheetBaker.PixelExact);

    /// <summary>
    /// Converts the accumulated surface into a new canonical-format bitmap, which the caller owns.
    /// </summary>
    /// <returns>
    /// The flattened bitmap, or <see cref="BakeFailure.LayerPixelFormatMismatch"/> when the
    /// surface cannot be converted.
    /// </returns>
    /// <remarks>
    /// The canvas is disposed first, matching the scoping the separate-canvas version relied on:
    /// nothing reads the surface's pixel memory while a canvas over it is still live.
    /// <c>SKCanvas.Dispose</c> is idempotent — it is inherited from Skia's own disposal guard — so
    /// <see cref="Dispose(bool)"/> disposing it again is a no-op, and calling this is not required
    /// before disposing the composite.
    /// </remarks>
    public Result<SKBitmap, BakeFailure> Flatten()
    {
        _canvas.Dispose();

        return SheetBaker.ToCanonical(_surface);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _canvas.Dispose();
            _surface.Dispose();
        }

        base.Dispose(disposing);
    }
}
