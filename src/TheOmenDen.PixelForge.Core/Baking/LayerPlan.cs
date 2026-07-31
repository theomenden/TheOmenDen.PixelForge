using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Turns a per-slot selection into a paper-doll layer set: one sheet per body and tone, and one
/// sheet per optional partial.
/// </summary>
/// <remarks>
/// <para>
/// This is not <see cref="BatchPlan"/> with a flag. The two expand along different axes.
/// <see cref="BatchPlan"/> takes the cross product of every slot, so ticking 9 hair, 5 hats and 22
/// weapons across 7 tones is 9,660 flattened sheets. Here the required trio cross-products and
/// takes the tone axis, while every optional partial emits exactly one sheet with no tone and no
/// multiplication at all — the same selection is 43 sheets, because an attachment is baked once and
/// shared by every hero rather than re-baked onto each of them.
/// </para>
/// <para>
/// <see cref="BatchPlan"/> is not superseded: <c>ExportPlan.Still</c> narrows a selection to one
/// combination and needs a dressed composite for the canvas. The preview composites; the export
/// layers.
/// </para>
/// <para>
/// Pure. Hero labels arrive already resolved, so nothing here reads <c>heroes.json</c> or touches a
/// disk — which is what keeps the whole expansion testable without a window or a filesystem.
/// </para>
/// </remarks>
public static class LayerPlan
{
    /// <summary>Where a hero's sheets go, under the export root.</summary>
    public const string HeroesFolder = "heroes";

    /// <summary>Where the shared, tone-independent layers go, under the export root.</summary>
    public const string AttachmentsFolder = "attachments";

    /// <summary>The segment marking a sheet as the non-default geometry.</summary>
    /// <remarks>
    /// <see cref="SheetGeometry.Curated"/> stays unmarked, mirroring how the tone segment is itself
    /// omitted for the source ramp: the default is silent. Without this a run in
    /// <c>ExportMode.Both</c> plans two recipes with one name, and two workers race
    /// <c>File.Create</c> on the same path.
    /// </remarks>
    private const string FullSuffix = "_full";

    /// <summary>
    /// The hero a sheet belongs to, read back out of its directory.
    /// </summary>
    /// <param name="directory">A recipe's <see cref="SheetRecipe.Directory"/>.</param>
    /// <returns>
    /// The hero's label, or nothing for an attachment — which belongs to no hero and is shared by
    /// all of them.
    /// </returns>
    /// <remarks>
    /// Here rather than as a field on <see cref="SheetRecipe"/> because this type owns the
    /// convention at both ends: it writes <c>heroes/&lt;label&gt;</c> and it reads it back. A third
    /// placement property on the recipe could disagree with the directory it sits beside.
    /// </remarks>
    public static Optional<string> HeroOf(string directory)
    {
        const string Prefix = HeroesFolder + "/";

        return directory is not null && directory.StartsWith(Prefix, StringComparison.Ordinal)
            ? directory[Prefix.Length..]
            : Optional<string>.None;
    }

    /// <summary>
    /// The distinct bodies a selection describes, so a caller can resolve their labels before
    /// planning.
    /// </summary>
    /// <param name="selections">What is ticked, one entry per slot.</param>
    /// <returns>
    /// One key per body combination, in plan order, or empty when the selection has no body — an
    /// overlay-only run is legal and produces attachments alone.
    /// </returns>
    /// <remarks>
    /// Separate from <see cref="Expand"/> on purpose. Assigning a hero its number reads and writes
    /// <c>heroes.json</c>, and threading that I/O through the planner would cost it its purity for
    /// no gain: the caller resolves labels between the two calls.
    /// </remarks>
    public static ImmutableArray<HeroKey> HeroKeys(ImmutableArray<SlotSelection> selections)
    {
        var required = Required(BatchPlan.Filled(selections));

        if (required.IsEmpty)
        {
            return [];
        }

        var keys = ImmutableArray.CreateBuilder<HeroKey>();

        Walk(required, chosen => keys.Add(KeyOf(chosen)));

        return keys.ToImmutable();
    }

    /// <summary>
    /// Expands a selection into the layer set it describes.
    /// </summary>
    /// <param name="selections">What is ticked, one entry per slot.</param>
    /// <param name="tones">The skin ramps to bake. Multiplies bodies only.</param>
    /// <param name="geometry">Stamped onto every recipe produced.</param>
    /// <param name="labels">
    /// The directory name for each body, from <see cref="HeroKeys"/>. Every key this selection
    /// produces must be present.
    /// </param>
    /// <returns>
    /// The recipes, or <see cref="PlanFailure.NothingSelected"/> when nothing is ticked, or
    /// <see cref="PlanFailure.RequiredSlotEmpty"/> when the body is half-committed.
    /// </returns>
    public static Result<ImmutableArray<SheetRecipe>, PlanFailure> Expand(
        ImmutableArray<SlotSelection> selections,
        ImmutableArray<SkinRamp> tones,
        SheetGeometry geometry,
        IReadOnlyDictionary<HeroKey, string> labels)
    {
        Guard.IsFalse(tones.IsDefault);
        Guard.IsNotNull(labels);

        var filled = BatchPlan.Filled(selections);

        if (BatchPlan.Validate(filled).TryGet(out var failure))
        {
            return new(failure);
        }

        var recipes = ImmutableArray.CreateBuilder<SheetRecipe>();

        Bodies(recipes, Required(filled), tones, geometry, labels);
        Attachments(recipes, filled, geometry);

        return new(recipes.ToImmutable());
    }

    /// <summary>
    /// Writes one plan into every container asked for.
    /// </summary>
    /// <param name="recipes">An expanded plan, from <see cref="Expand"/>.</param>
    /// <param name="formats">The containers to write. One leaves the count unchanged.</param>
    /// <returns>
    /// The recipes stamped with a format, each appearing once per entry in
    /// <paramref name="formats"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Corvus reads WebP and neither Unity nor MonoGame can open it, so a run serving both has to
    /// write both. Multiplying here rather than in the view model is what keeps it testable without
    /// a window, which is the same line <see cref="LayerPlan"/> already sits on.
    /// </para>
    /// <para>
    /// No collision follows, because <see cref="SheetRecipe.RelativePath"/> takes its extension
    /// from the format — which is why the geometry marking needs no format axis beside it.
    /// </para>
    /// </remarks>
    public static ImmutableArray<SheetRecipe> InFormats(
        ImmutableArray<SheetRecipe> recipes,
        ReadOnlySpan<SheetFormat> formats)
    {
        if (recipes.IsDefaultOrEmpty || formats.Length is 0)
        {
            return [];
        }

        var stamped = ImmutableArray.CreateBuilder<SheetRecipe>(recipes.Length * formats.Length);

        foreach (var format in formats)
        {
            foreach (var recipe in recipes)
            {
                stamped.Add(recipe with { Format = format });
            }
        }

        return stamped.ToImmutable();
    }

    /// <summary>
    /// What <see cref="Expand"/> would produce, broken into its parts for the page's label.
    /// </summary>
    /// <param name="selections">What is ticked, one entry per slot.</param>
    /// <param name="tones">The ticked skin ramps.</param>
    /// <returns>
    /// The breakdown, or all zeroes when the selection is one <see cref="Expand"/> would reject —
    /// a label must not advertise a run that cannot start.
    /// </returns>
    /// <remarks>
    /// Closed form rather than an expansion, because this recomputes on every tick of the picker.
    /// </remarks>
    public static PlannedCounts Count(
        ImmutableArray<SlotSelection> selections,
        ImmutableArray<SkinRamp> tones)
    {
        Guard.IsFalse(tones.IsDefault);

        var filled = BatchPlan.Filled(selections);

        if (BatchPlan.Validate(filled).HasValue)
        {
            return default;
        }

        var heroes = 0L;
        var attachments = 0L;

        foreach (var selection in filled)
        {
            if (AssetSlots.IsRequired(selection.Slot))
            {
                heroes = heroes is 0 ? selection.Choices.Length : BatchPlan.Saturate(heroes, selection.Choices.Length);

                continue;
            }

            attachments += Worn(selection);
        }

        return new(heroes, tones.Length, attachments);
    }

    /// <summary>One recipe per body per tone, each in its hero's directory.</summary>
    private static void Bodies(
        ImmutableArray<SheetRecipe>.Builder recipes,
        ImmutableArray<SlotSelection> required,
        ImmutableArray<SkinRamp> tones,
        SheetGeometry geometry,
        IReadOnlyDictionary<HeroKey, string> labels)
    {
        if (required.IsEmpty)
        {
            return;
        }

        Walk(required, chosen =>
        {
            var label = labels[KeyOf(chosen)];
            var layers = LayersOf(chosen);

            foreach (var tone in tones)
            {
                recipes.Add(new()
                {
                    Name = BatchPlan.WithTone(Marked(label, geometry), tone),
                    Directory = $"{HeroesFolder}/{label}",
                    Layers = layers,
                    Tone = tone,
                    Geometry = geometry,
                });
            }
        });
    }

    /// <summary>
    /// One recipe per optional partial, baked alone on transparency.
    /// </summary>
    /// <remarks>
    /// No tone axis and no hero: an attachment carries no skin, so it is identical for every body
    /// and every ramp, and re-baking it per hero would be the cross product coming back through the
    /// side door. <c>(none)</c> choices contribute nothing — the absence of a hat is not a sheet.
    /// </remarks>
    private static void Attachments(
        ImmutableArray<SheetRecipe>.Builder recipes,
        ImmutableArray<SlotSelection> filled,
        SheetGeometry geometry)
    {
        foreach (var selection in filled)
        {
            if (AssetSlots.IsRequired(selection.Slot))
            {
                continue;
            }

            var folder = $"{AttachmentsFolder}/{AssetSlots.FolderName(selection.Slot)}";

            foreach (var choice in selection.Choices)
            {
                if (!choice.TryGet(out var partial))
                {
                    continue;
                }

                recipes.Add(new()
                {
                    Name = Marked(partial.Stem, geometry),
                    Directory = folder,
                    Layers = [new(partial.Path, AssetSlots.IsSkinBearing(partial.Slot))],
                    Geometry = geometry,
                });
            }
        }
    }

    /// <summary>The required selections, in draw order. Empty for an overlay-only run.</summary>
    private static ImmutableArray<SlotSelection> Required(ImmutableArray<SlotSelection> filled) =>
        [.. filled.AsSpan().Where(static selection => AssetSlots.IsRequired(selection.Slot))];

    /// <summary>
    /// Visits every combination of <paramref name="axes"/>, handing each to
    /// <paramref name="visit"/> in draw order.
    /// </summary>
    /// <remarks>
    /// The same hand-written odometer <see cref="BatchPlan.Expand"/> uses, and for the same reason:
    /// no available LINQ provider expresses a cartesian product over a variable number of
    /// sequences. Narrower here — the axes are the required trio, which never offers
    /// <c>(none)</c>, so a combination always yields three partials.
    /// </remarks>
    private static void Walk(ImmutableArray<SlotSelection> axes, Action<List<AssetPartial>> visit)
    {
        var odometer = new int[axes.Length];
        var chosen = new List<AssetPartial>(axes.Length);

        do
        {
            chosen.Clear();

            for (var axis = 0; axis < axes.Length; axis++)
            {
                if (axes[axis].Choices[odometer[axis]].TryGet(out var partial))
                {
                    chosen.Add(partial);
                }
            }

            visit(chosen);
        }
        while (Advance(axes, odometer));
    }

    /// <summary>Steps the odometer to the next combination.</summary>
    private static bool Advance(ImmutableArray<SlotSelection> axes, Span<int> odometer)
    {
        for (var axis = odometer.Length - 1; axis >= 0; axis--)
        {
            odometer[axis]++;

            if (odometer[axis] < axes[axis].Choices.Length)
            {
                return true;
            }

            odometer[axis] = 0;
        }

        return false;
    }

    /// <summary>The identity of the body <paramref name="chosen"/> makes up.</summary>
    private static HeroKey KeyOf(List<AssetPartial> chosen)
    {
        var stems = new string[AssetSlots.DrawOrder.Length];

        Array.Fill(stems, string.Empty);

        foreach (var partial in chosen)
        {
            stems[(int)partial.Slot] = partial.Stem;
        }

        return new(stems[(int)AssetSlot.Bottom], stems[(int)AssetSlot.Top], stems[(int)AssetSlot.Head]);
    }

    /// <summary>The combination's layers, each carrying its slot's skin verdict.</summary>
    private static ImmutableArray<AssetLayer> LayersOf(List<AssetPartial> chosen)
    {
        var layers = ImmutableArray.CreateBuilder<AssetLayer>(chosen.Count);

        foreach (var partial in chosen)
        {
            layers.Add(new(partial.Path, AssetSlots.IsSkinBearing(partial.Slot)));
        }

        return layers.ToImmutable();
    }

    /// <summary>How many real partials a slot contributes. <c>(none)</c> is not a sheet.</summary>
    private static long Worn(SlotSelection selection) =>
        selection.Choices.AsSpan().Count(static choice => choice.HasValue);

    /// <summary>Marks a stem with the geometry, leaving the default silent.</summary>
    private static string Marked(string stem, SheetGeometry geometry) =>
        geometry is SheetGeometry.Full ? stem + FullSuffix : stem;
}
