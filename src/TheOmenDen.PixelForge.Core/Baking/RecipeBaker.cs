using CommunityToolkit.Diagnostics;
using DotNext;
using Microsoft.IO;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Palettes;

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

            var assembly = SheetBaker.Assemble(loaded);

            if (!assembly.TryGet(out var assembled))
            {
                return new(assembly.Error);
            }

            using (assembled)
            {
                return Finish(assembled, recipe.Recolor);
            }
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
        Optional<SkinRamp> recolor)
    {
        SKBitmap? toned = null;

        try
        {
            var subject = assembled;

            if (recolor.TryGet(out var ramp))
            {
                var recolored = SheetBaker.Recolor(assembled, ramp.SubstitutionFrom(SkinRamps.Source));

                if (!recolored.TryGet(out toned))
                {
                    return new(recolored.Error);
                }

                subject = toned;
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
        }
    }
}
