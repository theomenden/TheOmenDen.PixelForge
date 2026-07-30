using System.Collections.Immutable;
using DotNext;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>
/// Turns what the batch page has ticked into the recipes a run would bake.
/// </summary>
/// <remarks>
/// Its own type rather than private members on <see cref="BatchExportViewModel"/>: every member
/// here is a pure function of the selection, the tone axis and the mode, and the composite still
/// needs the same expansion without starting a run.
/// </remarks>
internal static class ExportPlan
{
    /// <summary>
    /// The recipes a selection, tone axis and mode imply.
    /// </summary>
    /// <param name="selections">What every slot contributes.</param>
    /// <param name="tones">The ticked tones, multiplying only combinations that carry skin.</param>
    /// <param name="mode">Which geometry to write, or both.</param>
    /// <returns>
    /// The recipes, or the <see cref="PlanFailure"/> that stopped them — a half-committed body is
    /// a notice, never a crash.
    /// </returns>
    /// <remarks>
    /// <see cref="ExportMode.Both"/> plans the same selection twice rather than baking once and
    /// re-cutting, because the two geometries are different remaps of the assembly rather than
    /// crops of one another.
    /// </remarks>
    public static Result<ImmutableArray<SheetRecipe>, PlanFailure> Recipes(
        ImmutableArray<SlotSelection> selections,
        ImmutableArray<SkinRamp> tones,
        ExportMode mode)
    {
        if (mode is not ExportMode.Both)
        {
            return BatchPlan.Expand(selections, tones, mode is ExportMode.Full
                ? SheetGeometry.Full
                : SheetGeometry.Curated);
        }

        var curated = BatchPlan.Expand(selections, tones, SheetGeometry.Curated);

        if (!curated.TryGet(out var first))
        {
            return new(curated.Error);
        }

        var full = BatchPlan.Expand(selections, tones, SheetGeometry.Full);

        if (!full.TryGet(out var second))
        {
            return new(full.Error);
        }

        ImmutableArray<SheetRecipe> both = [.. first, .. second];

        return new(both);
    }

    /// <summary>
    /// The paper-doll layer set a selection, tone axis and mode imply.
    /// </summary>
    /// <param name="selections">What every slot contributes.</param>
    /// <param name="tones">The ticked tones, multiplying bodies only.</param>
    /// <param name="mode">Which geometry to write, or both.</param>
    /// <param name="labels">The directory name for each body, from <see cref="HeroRegistry.Labels"/>.</param>
    /// <returns>The recipes, or the <see cref="PlanFailure"/> that stopped them.</returns>
    /// <remarks>
    /// The export path. <see cref="Recipes"/> stays for <see cref="Still"/>, which needs a dressed
    /// composite for the canvas — the preview composites, the export layers.
    /// </remarks>
    public static Result<ImmutableArray<SheetRecipe>, PlanFailure> Layers(
        ImmutableArray<SlotSelection> selections,
        ImmutableArray<SkinRamp> tones,
        ExportMode mode,
        IReadOnlyDictionary<HeroKey, string> labels)
    {
        if (mode is not ExportMode.Both)
        {
            return LayerPlan.Expand(
                selections,
                tones,
                mode is ExportMode.Full ? SheetGeometry.Full : SheetGeometry.Curated,
                labels);
        }

        var curated = LayerPlan.Expand(selections, tones, SheetGeometry.Curated, labels);

        if (!curated.TryGet(out var first))
        {
            return new(curated.Error);
        }

        var full = LayerPlan.Expand(selections, tones, SheetGeometry.Full, labels);

        if (!full.TryGet(out var second))
        {
            return new(full.Error);
        }

        ImmutableArray<SheetRecipe> both = [.. first, .. second];

        return new(both);
    }

    /// <summary>What a rejected plan tells the user.</summary>
    /// <param name="failure">Why <see cref="Recipes"/> produced nothing.</param>
    /// <returns>The sentence the page shows as a notice.</returns>
    public static string Explain(PlanFailure failure) => failure switch
    {
        PlanFailure.RequiredSlotEmpty =>
            "Bottom, top and head go together. Fill all three, or leave all three empty for an overlay-only sheet.",
        PlanFailure.HeroRegistryUnreadable =>
            "heroes.json in the output folder could not be read. Move or repair it before exporting — "
            + "renumbering over it would make existing hero folders mean different characters.",
        _ => "Nothing selected.",
    };

    /// <summary>
    /// The single recipe the composite still stands for.
    /// </summary>
    /// <param name="selections">What every slot contributes.</param>
    /// <param name="tones">The ticked tones; only the first is ever previewed.</param>
    /// <returns>
    /// The still's recipe, or <see cref="Optional{T}.None"/> when the selection plans nothing or is
    /// one <see cref="BatchPlan.Expand"/> rejects — a preview must not be the thing that reports a
    /// half-committed body.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Narrowed to one choice per slot <em>before</em> expanding, rather than expanding and taking
    /// index 0. The cross product is exactly what the page's planned-count label exists to warn
    /// about, and materialising billions of recipes on a debounce tick to discard all but one is
    /// the obvious way to hang the UI.
    /// </para>
    /// <para>
    /// Always <see cref="SheetGeometry.Curated"/>, because that is the geometry
    /// <see cref="PalettePreview"/> crops its idle row from. Which geometry a run writes is a
    /// property of the run, not of the still, so changing mode does not rebake the preview.
    /// </para>
    /// </remarks>
    public static Optional<SheetRecipe> Still(
        ImmutableArray<SlotSelection> selections,
        ImmutableArray<SkinRamp> tones)
    {
        ImmutableArray<SkinRamp> first = tones.IsDefaultOrEmpty ? [] : [tones[0]];

        var expanded = BatchPlan.Expand(Narrow(selections), first, SheetGeometry.Curated);

        return expanded.TryGet(out var recipes) && recipes.Length is not 0
            ? recipes[0]
            : Optional<SheetRecipe>.None;
    }

    /// <summary>
    /// One choice per slot, so the expansion above yields exactly one combination.
    /// </summary>
    /// <remarks>
    /// Slots contributing nothing are dropped rather than narrowed, matching what
    /// <see cref="BatchPlan.Expand"/> does with them — a slot with no choices is not an axis of
    /// zero, it is simply absent.
    /// </remarks>
    private static ImmutableArray<SlotSelection> Narrow(ImmutableArray<SlotSelection> selections)
    {
        if (selections.IsDefaultOrEmpty)
        {
            return [];
        }

        var narrowed = ImmutableArray.CreateBuilder<SlotSelection>(selections.Length);

        foreach (var selection in selections)
        {
            if (!selection.Choices.IsDefaultOrEmpty)
            {
                narrowed.Add(selection with { Choices = [Worn(selection.Choices)] });
            }
        }

        return narrowed.ToImmutable();
    }

    /// <summary>
    /// The choice that shows the most of a slot.
    /// </summary>
    /// <param name="choices">One slot's ticked choices, <c>(none)</c> included.</param>
    /// <returns>
    /// The first real partial, or <see cref="Optional{T}.None"/> when that is all the slot offers.
    /// </returns>
    /// <remarks>
    /// Deliberately not <paramref name="choices"/>'s first element. Every optional slot prepends
    /// <c>(none)</c> so the plainest sheet is written first, which would make the literal first
    /// combination a bare body no matter how much the user had ticked — a still that ignores the
    /// selection is not a preview of it.
    /// </remarks>
    private static Optional<AssetPartial> Worn(ImmutableArray<Optional<AssetPartial>> choices)
    {
        foreach (var choice in choices)
        {
            if (choice.HasValue)
            {
                return choice;
            }
        }

        return choices[0];
    }
}
