using System.Numerics;
using System.Runtime.InteropServices;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.HighPerformance;
using DotNext;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Composites Time Elements layer partials into the curated sheet Corvus consumes.
/// <para>
/// Compositing is Skia's (<see cref="SKCanvas"/>), the frame remap is
/// CommunityToolkit's (<see cref="Span2D{T}"/> slice-and-copy), and format conversion is
/// Skia's (<see cref="SKPixmap.ReadPixels(SKImageInfo, nint, int, int, int)"/>). The only
/// hand-written pixel loop is <see cref="Recolor"/> — the one operation done by hand rather than
/// delegated, and the remarks there record which library forms were considered and why each was
/// rejected.
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
    /// <para>
    /// Internal rather than private: <see cref="RecipeBaker"/> and
    /// <see cref="Palettes.PalettePreview"/> draw pixel art too, and this must stay identical on
    /// every path — a single source stops one of them drifting to <see cref="SKFilterMode.Linear"/>
    /// unnoticed.
    /// </para>
    /// </summary>
    internal static SKSamplingOptions PixelExact => new(SKFilterMode.Nearest, SKMipmapMode.None);

    /// <summary>
    /// Flattens layer partials in draw order — the caller passes them back-to-front, matching
    /// the generator's <c>CharacterLayers</c> ordering (shadow, bottom, top, head).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layers are borrowed, not owned — every one has to be alive for the whole call, so this
    /// is the form that peaks at the entire stack. The bake path does not use it:
    /// <see cref="RecipeBaker.AssembleLayers"/> drives a <see cref="LayerComposite"/> directly so
    /// it can dispose each layer as soon as it has been drawn. This overload remains for callers
    /// that already hold decoded bitmaps and want to keep them.
    /// </para>
    /// <para>
    /// Compositing happens on a premultiplied surface because that is what Skia draws into,
    /// then converts to the unpremultiplied canonical format. The source art is strictly binary
    /// alpha, so that round trip is exact rather than merely close.
    /// </para>
    /// </remarks>
    public static Result<SKBitmap, BakeFailure> Assemble(IReadOnlyList<SKBitmap> layers)
    {
        Guard.IsNotNull(layers);

        if (layers.Count is 0)
        {
            return new(BakeFailure.NoLayersSupplied);
        }

        // Layers must agree with each other, which is what compositing actually requires. Matching
        // Time Elements is Curate's question, and it asks it separately — checking it here as well
        // was over-constraint, and it is what refused a second pack's geometry outright.
        var first = layers[0];

        foreach (var layer in layers)
        {
            if (layer.Width != first.Width || layer.Height != first.Height)
            {
                return new(BakeFailure.LayerGeometryMismatch);
            }
        }

        using var composite = new LayerComposite(first.Width, first.Height);

        foreach (var layer in layers)
        {
            composite.Draw(layer);
        }

        return composite.Flatten();
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
    /// Applies a palette substitution, leaving every colour outside the table untouched and every
    /// transparent pixel exactly as it was.
    /// </summary>
    /// <returns>
    /// The recoloured bitmap in canonical format, or
    /// <see cref="BakeFailure.LayerPixelFormatMismatch"/> when the source cannot be converted to it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Hand-written, but by choice rather than for want of a library.
    /// <see cref="SKColorFilter"/>'s <c>CreateTable</c> applies four independent per-channel lookups
    /// and cannot express "change this pixel only when all three channels match"; its
    /// <c>CreateColorMatrix</c> is a linear transform and is no closer. Skia <em>can</em> express it
    /// through <see cref="SKRuntimeEffect"/>, and <c>ColorHelper.ColorComparer</c> compares single
    /// colours across colour models. Both were rejected: a runtime effect works in float, while this
    /// substitution must be byte-exact to survive <see cref="LosslessWebp"/>'s
    /// <c>EncodeVerified</c> round trip, and it would add a shader compile as a new failure mode;
    /// <c>ColorComparer</c> is a scalar helper that would allocate per pixel.
    /// </para>
    /// </remarks>
    public static Result<SKBitmap, BakeFailure> Recolor(SKBitmap source, RampSubstitution substitution)
    {
        Guard.IsNotNull(source);

        var recolored = ToCanonical(source);

        if (!recolored.TryGet(out var target))
        {
            return recolored;
        }

        if (substitution.IsIdentity)
        {
            return target;
        }

        using var pixmap = target.PeekPixels();

        // MemoryMarshal.Cast is a zero-copy reinterpretation. SKBitmap.Pixels would allocate an
        // SKColor[] — 828 KiB for a source partial, straight to the LOH.
        Substitute(MemoryMarshal.Cast<byte, uint>(pixmap.GetPixelSpan()), substitution);

        return target;
    }

    /// <summary>
    /// Substitutes packed pixels in place, vectorised, with a scalar tail for the remainder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whole 32-bit pixels are compared, alpha included. That is only correct because the source
    /// art has strictly binary alpha — verified across all 995 partials — so every opaque pixel is
    /// <c>0xFF______</c> and no transparent pixel can equal an entry in
    /// <see cref="RampSubstitution.From"/>. It buys the removal of a mask, a separate opacity test
    /// and an alpha recombination from the inner loop, leaving five comparisons and five selects.
    /// </para>
    /// <para>
    /// <see cref="Vector{T}"/> rather than <c>Vector256&lt;T&gt;</c> so the JIT picks the widest
    /// register the machine has — 8 pixels at a time under AVX2, 16 under AVX-512 — instead of the
    /// source pinning an instruction set.
    /// </para>
    /// <para>
    /// Public rather than <see langword="internal"/> only so the test assembly can compare it
    /// against <see cref="SubstituteScalar"/>, which is the sole proof the SIMD loop is right.
    /// <c>InternalsVisibleTo</c> is not an option here: the ZLinq drop-in generator emits an
    /// internal <c>ZLinqDropInExtensions</c> into every assembly, and making this one visible to a
    /// project that has its own makes every drop-in call in it ambiguous. <see cref="Recolor"/>
    /// remains the entry point callers should reach for.
    /// </para>
    /// </remarks>
    public static void Substitute(Span<uint> pixels, RampSubstitution substitution)
    {
        var width = Vector<uint>.Count;

        if (!Vector.IsHardwareAccelerated || pixels.Length < width)
        {
            SubstituteScalar(pixels, substitution);
            return;
        }

        // Broadcast once, outside the loop: the table is five entries and never changes mid-pass.
        var from = new Vector<uint>[substitution.Length];
        var to = new Vector<uint>[substitution.Length];

        for (var step = 0; step < substitution.Length; step++)
        {
            from[step] = new(substitution.From[step]);
            to[step] = new(substitution.To[step]);
        }

        var index = 0;

        for (; index <= pixels.Length - width; index += width)
        {
            var block = new Vector<uint>(pixels.Slice(index, width));
            var result = block;

            // Comparing against `block`, never `result`: the five source colours are distinct, so
            // a lane can match at most one, and folding earlier writes back in would let a target
            // colour that happens to equal a later source colour be substituted twice.
            for (var step = 0; step < from.Length; step++)
            {
                result = Vector.ConditionalSelect(Vector.Equals(block, from[step]), to[step], result);
            }

            result.CopyTo(pixels.Slice(index, width));
        }

        SubstituteScalar(pixels[index..], substitution);
    }

    /// <summary>
    /// The unvectorised substitution. Handles the tail of <see cref="Substitute"/>, and is the
    /// reference the vector path is tested against.
    /// </summary>
    /// <remarks>
    /// Public for the same reason <see cref="Substitute"/> is, and for no other — nothing in the
    /// bake pipeline should call it directly.
    /// </remarks>
    public static void SubstituteScalar(Span<uint> pixels, RampSubstitution substitution)
    {
        var from = substitution.From.AsSpan();
        var to = substitution.To.AsSpan();

        for (var index = 0; index < pixels.Length; index++)
        {
            var pixel = pixels[index];

            for (var step = 0; step < from.Length; step++)
            {
                if (pixel == from[step])
                {
                    pixels[index] = to[step];
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Enlarges a bitmap by a whole-number factor, nearest-neighbour.
    /// </summary>
    /// <param name="source">What to enlarge. Left alone; the result is a new bitmap.</param>
    /// <param name="scale">The multiplier. 1 still copies, so the caller owns one bitmap either way.</param>
    /// <returns>The enlarged bitmap in canonical format, or why it could not be converted.</returns>
    /// <remarks>
    /// Here rather than on each preview because <see cref="PixelExact"/> lives here: every path
    /// that puts pixel art on screen has to enlarge it the same way, and a second copy of this is
    /// a second chance for one of them to drift to <see cref="SKFilterMode.Linear"/> and blur.
    /// </remarks>
    public static Result<SKBitmap, BakeFailure> Upscale(SKBitmap source, int scale)
    {
        Guard.IsNotNull(source);
        Guard.IsGreaterThan(scale, 0);

        using var scaled = new SKBitmap(new SKImageInfo(
            source.Width * scale, source.Height * scale, SKColorType.Rgba8888, SKAlphaType.Premul));

        using (var canvas = new SKCanvas(scaled))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(
                source,
                SKRect.Create(0, 0, source.Width, source.Height),
                SKRect.Create(0, 0, scaled.Width, scaled.Height),
                PixelExact);
        }

        return ToCanonical(scaled);
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
