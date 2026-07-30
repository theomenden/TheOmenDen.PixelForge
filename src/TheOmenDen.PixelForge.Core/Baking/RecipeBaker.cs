using CommunityToolkit.Diagnostics;
using DotNext;
using Microsoft.IO;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Runs a <see cref="SheetRecipe"/> end to end: load, recolour, assemble, curate, encode.
/// <para>
/// The encode step is <see cref="LosslessWebp.EncodeVerified"/>, so a recipe either yields a
/// verified sheet or a <see cref="BakeFailure"/> — there is no path that returns an unchecked
/// one. This is the spec's bake guard: a caller that ignores the failure gets nothing to
/// write, rather than a silently wrong sheet.
/// </para>
/// <para>
/// The returned stream is pooled. The caller owns it and disposes it to return the buffer.
/// </para>
/// </summary>
public static class RecipeBaker
{
    /// <summary>
    /// Bakes a recipe into a verified sheet in the geometry it asks for.
    /// </summary>
    /// <param name="recipe">The sheet to bake. Never <see langword="null"/>.</param>
    /// <returns>
    /// A pooled stream holding the encoded sheet, which the caller owns and must dispose, or the
    /// first <see cref="BakeFailure"/> the assemble or encode hit.
    /// </returns>
    public static Result<RecyclableMemoryStream, BakeFailure> Bake(SheetRecipe recipe)
    {
        Guard.IsNotNull(recipe);

        var assembly = AssembleLayers(recipe);

        if (!assembly.TryGet(out var assembled))
        {
            return new(assembly.Error);
        }

        using (assembled)
        {
            return Finish(assembled, recipe);
        }
    }

    /// <summary>
    /// Decodes a recipe's layers, recolours the skin-bearing ones, and composites the result in
    /// draw order.
    /// </summary>
    /// <param name="recipe">The sheet whose layers are wanted. Never <see langword="null"/>.</param>
    /// <returns>
    /// The assembled source-geometry bitmap, or the first failure encountered:
    /// <see cref="BakeFailure.NoLayersSupplied"/>, <see cref="BakeFailure.LayerNotFound"/>,
    /// <see cref="BakeFailure.LayerUnreadable"/> or a geometry or format mismatch.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The substitution runs per layer, before compositing, rather than once over the flattened
    /// result. That is what lets a bare-armed top take the skin tone while a hat's ramp-coloured
    /// trim keeps its authored one, and it is the only ordering that works for back-hair, which
    /// draws beneath the body.
    /// </para>
    /// <para>
    /// Layers stream through a <see cref="LayerComposite"/> — decoded, drawn, then disposed —
    /// rather than being decoded into a list and composited at the end. At 828 KiB per canonical
    /// source partial the batched form peaked at <c>layers x 828 KiB</c> per worker, which a
    /// ten-slot stack on a many-core batch run turns into hundreds of megabytes of live bitmaps.
    /// </para>
    /// <para>
    /// Exposed for the composite preview, which needs the assembly but neither a curate nor an
    /// encode. Sharing it keeps the decode-and-validate loop in one place.
    /// </para>
    /// </remarks>
    public static Result<SKBitmap, BakeFailure> AssembleLayers(SheetRecipe recipe)
    {
        Guard.IsNotNull(recipe);

        if (recipe.Layers.IsDefaultOrEmpty)
        {
            return new(BakeFailure.NoLayersSupplied);
        }

        // The TryGet must stay inside the ternary: hoisting it into a `hasTone` local first loses
        // the compiler's proof that `ramp` is non-null, and nullable analysis is an error here.
        var substitution = recipe.Tone.TryGet(out var ramp)
            ? ramp.SubstitutionFrom(SkinRamps.Source)
            : default;

        var hasTone = recipe.Tone.HasValue;

        using var composite = new LayerComposite();

        foreach (var layer in recipe.Layers)
        {
            var canonical = Prepare(layer, substitution, hasTone);

            if (!canonical.TryGet(out var ready))
            {
                return new(canonical.Error);
            }

            // Drawn and released before the next one is decoded, so the peak is one layer rather
            // than the whole stack. Prepare has already checked the geometry, which is the only
            // thing the batched form validated up front.
            using (ready)
            {
                composite.Draw(ready);
            }
        }

        return composite.Flatten();
    }

    /// <summary>
    /// Decodes one layer, validates it, and returns it in canonical format — recoloured when the
    /// layer carries skin and the recipe names a tone.
    /// </summary>
    /// <returns>
    /// The prepared bitmap, which the caller owns and must dispose, or
    /// <see cref="BakeFailure.LayerNotFound"/>, <see cref="BakeFailure.LayerUnreadable"/> or
    /// <see cref="BakeFailure.LayerGeometryMismatch"/>.
    /// </returns>
    /// <remarks>
    /// The recolour happens here, before compositing, rather than once over the flattened
    /// assembly. <see cref="SheetBaker.ToCanonical"/> is required on both paths anyway — Skia's
    /// preferred colour type on Windows is BGRA and the substitution reads pixel memory directly —
    /// so the non-skin branch is a format conversion, not a wasted pass.
    /// </remarks>
    private static Result<SKBitmap, BakeFailure> Prepare(
        AssetLayer layer,
        RampSubstitution substitution,
        bool hasTone)
    {
        if (!File.Exists(layer.Path.Value))
        {
            return new(BakeFailure.LayerNotFound);
        }

        using var decoded = SKBitmap.Decode(layer.Path.Value);

        if (decoded is null)
        {
            return new(BakeFailure.LayerUnreadable);
        }

        if (decoded.Width != SheetLayout.SourceWidth || decoded.Height != SheetLayout.SourceHeight)
        {
            return new(BakeFailure.LayerGeometryMismatch);
        }

        return layer.IsSkin && hasTone
            ? SheetBaker.Recolor(decoded, substitution)
            : SheetBaker.ToCanonical(decoded);
    }

    private static Result<RecyclableMemoryStream, BakeFailure> Finish(
        SKBitmap assembled,
        SheetRecipe recipe)
    {
        if (recipe.Geometry is SheetGeometry.Full)
        {
            // Full geometry *is* the assembly — no remap, so nothing to curate.
            return LosslessWebp.EncodeVerified(assembled);
        }

        var curation = SheetBaker.Curate(assembled);

        if (!curation.TryGet(out var curated))
        {
            return new(curation.Error);
        }

        using (curated)
        {
            return LosslessWebp.EncodeVerified(curated);
        }
    }
}
