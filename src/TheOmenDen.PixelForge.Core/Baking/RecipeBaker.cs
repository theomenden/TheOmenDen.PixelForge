using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;
using Microsoft.IO;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Runs a <see cref="SheetRecipe"/> end to end: load, assemble, recolour, curate, encode.
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
    /// Decodes a recipe's layers and composites them, stopping before the recolour. Overlays are
    /// deliberately not applied — they belong after the substitution.
    /// <para>
    /// Exposed for the palette preview, which needs the assembly but neither the recolour nor an
    /// encode. Sharing this is what keeps the decode-and-validate loop in one place.
    /// </para>
    /// </summary>
    public static Result<SKBitmap, BakeFailure> AssembleLayers(SheetRecipe recipe)
    {
        Guard.IsNotNull(recipe);

        if (recipe.Layers.IsDefaultOrEmpty)
        {
            return new(BakeFailure.NoLayersSupplied);
        }

        var loaded = new List<SKBitmap>(recipe.Layers.Length);

        try
        {
            foreach (var path in recipe.Layers)
            {
                if (!File.Exists(path.Value))
                {
                    return new(BakeFailure.LayerNotFound);
                }

                // No format pinning needed here: layers are only ever read by SKCanvas, which
                // handles any colour type. Assemble is what returns canonical pixels.
                var layer = SKBitmap.Decode(path.Value);

                if (layer is null)
                {
                    return new(BakeFailure.LayerUnreadable);
                }

                loaded.Add(layer);
            }

            return SheetBaker.Assemble(loaded);
        }
        finally
        {
            foreach (var layer in loaded)
            {
                layer.Dispose();
            }
        }
    }

    private static Result<RecyclableMemoryStream, BakeFailure> Finish(
        SKBitmap assembled,
        SheetRecipe recipe)
    {
        SKBitmap? toned = null;
        SKBitmap? overlaid = null;

        try
        {
            var subject = assembled;

            if (recipe.Recolor.TryGet(out var ramp))
            {
                var recolored = SheetBaker.Recolor(subject, ramp.SubstitutionFrom(SkinRamps.Source));

                if (!recolored.TryGet(out toned))
                {
                    return new(recolored.Error);
                }

                subject = toned;
            }

            if (!recipe.Overlays.IsDefaultOrEmpty)
            {
                var composited = ApplyOverlays(subject, recipe.Overlays);

                if (!composited.TryGet(out overlaid))
                {
                    return new(composited.Error);
                }

                subject = overlaid;
            }

            var curation = SheetBaker.Curate(subject);

            if (!curation.TryGet(out var curated))
            {
                return new(curation.Error);
            }

            using (curated)
            {
                return LosslessWebp.EncodeVerified(curated);
            }
        }
        finally
        {
            toned?.Dispose();
            overlaid?.Dispose();
        }
    }

    /// <summary>
    /// Draws overlay partials over an already-recoloured assembly. Compositing happens on a
    /// premultiplied surface because that is what Skia draws into, then converts back to the
    /// canonical unpremultiplied format — the same round trip <see cref="SheetBaker.Assemble"/>
    /// makes, and exact for the strictly binary alpha this art uses.
    /// </summary>
    private static Result<SKBitmap, BakeFailure> ApplyOverlays(
        SKBitmap subject,
        ImmutableArray<FullPath> overlays)
    {
        var loaded = new List<SKBitmap>(overlays.Length);

        try
        {
            foreach (var path in overlays)
            {
                if (!File.Exists(path.Value))
                {
                    return new(BakeFailure.LayerNotFound);
                }

                var overlay = SKBitmap.Decode(path.Value);

                if (overlay is null)
                {
                    return new(BakeFailure.LayerUnreadable);
                }

                loaded.Add(overlay);

                if (overlay.Width != SheetLayout.SourceWidth || overlay.Height != SheetLayout.SourceHeight)
                {
                    return new(BakeFailure.LayerGeometryMismatch);
                }
            }

            using var composited = new SKBitmap(new SKImageInfo(
                SheetLayout.SourceWidth, SheetLayout.SourceHeight,
                SKColorType.Rgba8888, SKAlphaType.Premul));

            using (var canvas = new SKCanvas(composited))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(subject, 0, 0, SheetBaker.PixelExact);

                foreach (var overlay in loaded)
                {
                    canvas.DrawBitmap(overlay, 0, 0, SheetBaker.PixelExact);
                }
            }

            return SheetBaker.ToCanonical(composited);
        }
        finally
        {
            foreach (var overlay in loaded)
            {
                overlay.Dispose();
            }
        }
    }
}
