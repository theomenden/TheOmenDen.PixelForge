using System.Collections.Frozen;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.HighPerformance;
using DotNext;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Composites Time Elements layer partials into the curated sheet Corvus consumes.
/// <para>
/// Compositing is Skia's (<see cref="SKCanvas"/>), the frame remap is
/// CommunityToolkit's (<see cref="Span2D{T}"/> slice-and-copy), and format conversion is
/// Skia's (<see cref="SKPixmap.ReadPixels(SKImageInfo, nint, int, int, int)"/>). The only
/// hand-written pixel loop is <see cref="Recolor"/>, because a palette substitution is the one
/// operation neither library can express — see the remarks there.
/// </para>
/// <para>
/// Geometry and pixel format are validated as <see cref="BakeFailure"/> values rather than
/// exceptions: the inputs are files on someone's disk, so the wrong image is ordinary bad
/// input, not a bug.
/// </para>
/// </summary>
public static class SheetBaker
{
    /// <summary>Unpremultiplied RGBA8888 — chosen so encode and round-trip compare are exact.</summary>
    private static SKImageInfo CanonicalInfo(int width, int height) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

    /// <summary>
    /// Nearest with no mipmapping. Layers land 1:1 so no resampling should occur at all, but
    /// stating it means a future scaled draw cannot quietly blur pixel art.
    /// </summary>
    private static SKSamplingOptions PixelExact => new(SKFilterMode.Nearest, SKMipmapMode.None);

    /// <summary>
    /// Flattens layer partials in draw order — the caller passes them back-to-front, matching
    /// the generator's <c>CharacterLayers</c> ordering (shadow, bottom, top, head).
    /// </summary>
    /// <remarks>
    /// Compositing happens on a premultiplied surface because that is what Skia draws into,
    /// then converts to the unpremultiplied canonical format. The source art is strictly binary
    /// alpha, so that round trip is exact rather than merely close.
    /// </remarks>
    public static Result<SKBitmap, BakeFailure> Assemble(IReadOnlyList<SKBitmap> layers)
    {
        Guard.IsNotNull(layers);

        if (layers.Count is 0)
        {
            return new(BakeFailure.NoLayersSupplied);
        }

        foreach (var layer in layers)
        {
            if (layer.Width != SheetLayout.SourceWidth || layer.Height != SheetLayout.SourceHeight)
            {
                return new(BakeFailure.LayerGeometryMismatch);
            }
        }

        using var composited = new SKBitmap(
            new SKImageInfo(SheetLayout.SourceWidth, SheetLayout.SourceHeight, SKColorType.Rgba8888, SKAlphaType.Premul));

        using (var canvas = new SKCanvas(composited))
        {
            canvas.Clear(SKColors.Transparent);

            foreach (var layer in layers)
            {
                canvas.DrawBitmap(layer, 0, 0, PixelExact);
            }
        }

        return ToCanonical(composited);
    }

    /// <summary>
    /// Converts any decoded bitmap into the canonical format. Skia's platform-preferred colour
    /// type on Windows is BGRA, so this is what keeps red and blue from quietly swapping once
    /// pixel memory is read directly.
    /// </summary>
    public static Result<SKBitmap, BakeFailure> ToCanonical(SKBitmap source)
    {
        Guard.IsNotNull(source);

        var canonical = new SKBitmap(CanonicalInfo(source.Width, source.Height));

        using var pixmap = source.PeekPixels();

        if (pixmap is null
            || !pixmap.ReadPixels(canonical.Info, canonical.GetPixels(), canonical.RowBytes, 0, 0))
        {
            canonical.Dispose();
            return new(BakeFailure.LayerPixelFormatMismatch);
        }

        return canonical;
    }

    /// <summary>
    /// Applies a palette substitution, leaving every colour outside the table untouched.
    /// Alpha is preserved, so a fully transparent pixel stays transparent.
    /// </summary>
    /// <remarks>
    /// Hand-written on purpose. <c>SKColorFilter.CreateTable</c> applies four independent
    /// per-channel lookup tables, which cannot express this mapping: a skin step must change
    /// only when all three of its channels match, and a per-channel table would recolour every
    /// pixel sharing a single channel value — silently rewriting garment and hair pixels that
    /// legitimately reuse ramp bytes. <c>CreateColorMatrix</c> is a linear transform and is no
    /// closer. There is no library form of an arbitrary RGB-to-RGB lookup.
    /// </remarks>
    public static Result<SKBitmap, BakeFailure> Recolor(SKBitmap source, FrozenDictionary<uint, SKColor> substitution)
    {
        Guard.IsNotNull(source);
        Guard.IsNotNull(substitution);

        var recolored = ToCanonical(source);

        if (!recolored.TryGet(out var target))
        {
            return recolored;
        }

        using var pixmap = target.PeekPixels();
        var bytes = pixmap.GetPixelSpan();

        for (var offset = 0; offset + SheetLayout.BytesPerPixel <= bytes.Length; offset += SheetLayout.BytesPerPixel)
        {
            if (bytes[offset + 3] is 0)
            {
                continue;
            }

            var key = ((uint)bytes[offset] << 16) | ((uint)bytes[offset + 1] << 8) | bytes[offset + 2];

            if (substitution.TryGetValue(key, out var replacement))
            {
                bytes[offset] = replacement.Red;
                bytes[offset + 1] = replacement.Green;
                bytes[offset + 2] = replacement.Blue;
            }
        }

        return target;
    }

    /// <summary>
    /// Slices the 23x4 assembly down to the curated 5x24 sheet: 8 animations on 3 facings,
    /// north dropped. Frames land left-aligned, so a clip shorter than
    /// <see cref="SheetLayout.OutputColumns"/> leaves its trailing cells transparent.
    /// </summary>
    public static Result<SKBitmap, BakeFailure> Curate(SKBitmap assembled)
    {
        Guard.IsNotNull(assembled);

        if (assembled.Width != SheetLayout.SourceWidth || assembled.Height != SheetLayout.SourceHeight)
        {
            return new(BakeFailure.SourceGeometryMismatch);
        }

        if (assembled.ColorType is not SKColorType.Rgba8888)
        {
            return new(BakeFailure.LayerPixelFormatMismatch);
        }

        var curated = new SKBitmap(CanonicalInfo(SheetLayout.OutputWidth, SheetLayout.OutputHeight));

        var source = AsPixelGrid(assembled);
        var target = AsPixelGrid(curated);

        for (var clipIndex = 0; clipIndex < SheetLayout.Clips.Length; clipIndex++)
        {
            var clip = SheetLayout.Clips[clipIndex];

            for (var facing = 0; facing < SheetLayout.FacingCount; facing++)
            {
                var outputRow = SheetLayout.RowFor(clipIndex, facing);

                for (var frame = 0; frame < clip.FrameCount; frame++)
                {
                    var cell = source.Slice(
                        facing * SheetLayout.CellSize,
                        (clip.SourceColumn + frame) * SheetLayout.CellSize,
                        SheetLayout.CellSize,
                        SheetLayout.CellSize);

                    cell.CopyTo(target.Slice(
                        outputRow * SheetLayout.CellSize,
                        frame * SheetLayout.CellSize,
                        SheetLayout.CellSize,
                        SheetLayout.CellSize));
                }
            }
        }

        return curated;
    }

    /// <summary>
    /// Views a bitmap's pixel memory as a 2D grid so the frame remap is a slice-and-copy rather
    /// than hand-rolled stride arithmetic. Pitch is derived from <see cref="SKBitmap.RowBytes"/>
    /// rather than assumed, since Skia is free to pad rows.
    /// </summary>
    private static unsafe Span2D<uint> AsPixelGrid(SKBitmap bitmap)
    {
        var pitch = (bitmap.RowBytes / SheetLayout.BytesPerPixel) - bitmap.Width;

        return new Span2D<uint>((void*)bitmap.GetPixels(), bitmap.Height, bitmap.Width, pitch);
    }
}
