using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Turns a per-slot selection into the batch of recipes it describes.
/// </summary>
/// <remarks>
/// <para>
/// The interesting rule is the tone axis: it multiplies a combination <em>only when that
/// combination actually contains a skin-bearing partial</em>. Selecting hair alone is one sheet
/// per hairstyle, not one per hairstyle per tone, which is exactly the layered two-texture
/// contract Corvus consumes — getting it wrong turns 9 hair sheets into 63 identical ones.
/// </para>
/// <para>
/// It is evaluated per combination rather than once for the run, because a selection that mixes
/// <c>(none)</c> with a real top produces some combinations that carry skin and some that do not,
/// and the skinless ones must still emit a single sheet with no tone.
/// </para>
/// </remarks>
public static class BatchPlan
{
    /// <summary>
    /// How a ramp name becomes the tone segment of a stem.
    /// </summary>
    /// <remarks>
    /// The separator is spelled out rather than left to the package default so the naming contract
    /// lives here and not in a dependency's defaults, and
    /// <see cref="SlugOptions.CanEndWithSeparator"/> is off because <c>"Tone 4 (Green)"</c>
    /// otherwise slugs to a trailing <c>-</c> from the closing bracket.
    /// </remarks>
    private static readonly SlugOptions ToneSlug = new()
    {
        Separator = "-",
        CanEndWithSeparator = false,
        CasingTransformation = CasingTransformation.ToLowerCase,
    };

    /// <summary>
    /// Expands a per-slot selection into one recipe per combination, multiplied by the tone axis
    /// where a combination carries skin.
    /// </summary>
    /// <param name="selections">
    /// What is ticked, one entry per slot. Order does not matter — layers come out in
    /// <see cref="AssetSlot"/> order regardless.
    /// </param>
    /// <param name="tones">The skin ramps to bake, applied only to combinations that carry skin.</param>
    /// <param name="geometry">Stamped onto every recipe produced.</param>
    /// <returns>
    /// The recipes, or <see cref="PlanFailure.NothingSelected"/> when nothing is ticked, or
    /// <see cref="PlanFailure.RequiredSlotEmpty"/> when the body is half-committed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The tone axis applies <em>per combination</em>, not to the run as a whole. A combination
    /// with no skin-bearing partial produces a single sheet with no tone: hair alone is nine
    /// sheets, not sixty-three.
    /// </para>
    /// <para>
    /// The odometer is hand-written because no available LINQ provider expresses a cartesian
    /// product over a variable number of sequences — neither ZLinq nor System.Linq ships one, and
    /// it is the only such gap in this feature.
    /// </para>
    /// </remarks>
    public static Result<ImmutableArray<SheetRecipe>, PlanFailure> Expand(
        ImmutableArray<SlotSelection> selections,
        ImmutableArray<SkinRamp> tones,
        SheetGeometry geometry)
    {
        Guard.IsFalse(tones.IsDefault);

        var filled = Filled(selections);

        if (Validate(filled).TryGet(out var failure))
        {
            return new(failure);
        }

        var recipes = ImmutableArray.CreateBuilder<SheetRecipe>();
        var odometer = new int[filled.Length];
        var chosen = new List<AssetPartial>(filled.Length);

        do
        {
            Gather(filled, odometer, chosen);
            Emit(recipes, chosen, tones, geometry);
        }
        while (Advance(filled, odometer));

        return new(recipes.ToImmutable());
    }

    /// <summary>
    /// How many sheets <see cref="Expand"/> would produce from the same selection.
    /// </summary>
    /// <param name="selections">What is ticked, one entry per slot.</param>
    /// <param name="tones">The skin ramps to bake.</param>
    /// <returns>
    /// The planned sheet count, or <c>0</c> when the selection is one <see cref="Expand"/> would
    /// reject — a label must not advertise a run that cannot start.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Counted in closed form rather than by walking the odometer, because this drives a live label
    /// that recomputes on every tick. Combinations carrying no skin are the product of each slot's
    /// skinless choices — for a skin-bearing slot only <c>(none)</c> qualifies — and only the
    /// remainder pays the tone axis.
    /// </para>
    /// <para>
    /// <see langword="long"/>, and saturating rather than wrapping: ten slots of a 995-file library
    /// overflow <see langword="int"/> easily and can pass <see cref="long.MaxValue"/> too, and a
    /// label reading a negative number is worse than one reading an implausible one.
    /// </para>
    /// </remarks>
    public static long Count(ImmutableArray<SlotSelection> selections, ImmutableArray<SkinRamp> tones)
    {
        Guard.IsFalse(tones.IsDefault);

        var filled = Filled(selections);

        if (Validate(filled).HasValue)
        {
            return 0;
        }

        var total = 1L;
        var skinless = 1L;

        foreach (var selection in filled)
        {
            total = Saturate(total, selection.Choices.Length);
            skinless = Saturate(skinless, SkinlessChoices(selection));
        }

        var toned = Saturate(total - skinless, tones.Length);

        return toned > long.MaxValue - skinless ? long.MaxValue : toned + skinless;
    }

    /// <summary>
    /// The output stem for one combination: each partial's <see cref="AssetPartial.Stem"/> joined
    /// with <c>_</c>, then the tone.
    /// </summary>
    /// <param name="chosen">
    /// The combination's partials, already in draw order — <see cref="Expand"/> supplies them that
    /// way, and the stem is only as readable as that ordering makes it.
    /// </param>
    /// <param name="tone">The tone the sheet is baked in, or none.</param>
    /// <returns>The stem, without an extension.</returns>
    /// <remarks>
    /// <para>
    /// The tone segment is omitted for <see cref="SkinRamps.Source"/> and for no tone at all — the
    /// source ramp is what the art is already authored in, so naming it would add a redundant
    /// segment to most files. Other ramps are slugged with <see cref="Slug"/> rather than
    /// hand-sanitised, which also covers user-created ramps whose names are arbitrary text.
    /// </para>
    /// <para>
    /// Ten slots plus a tone can approach <c>MAX_PATH</c> under a deep output directory. That is
    /// accepted: <see cref="BatchManifest"/> is the authoritative index, and a write failure
    /// surfaces as <see cref="BakeFailure.OutputWriteFailed"/> rather than silent truncation.
    /// </para>
    /// </remarks>
    public static string StemFor(IReadOnlyList<AssetPartial> chosen, Optional<SkinRamp> tone)
    {
        Guard.IsNotNull(chosen);

        var stem = string.Join('_', chosen.AsValueEnumerable().Select(static partial => partial.Stem).ToArray());

        if (!tone.TryGet(out var ramp)
            || string.Equals(ramp.Name, SkinRamps.Source.Name, StringComparison.OrdinalIgnoreCase))
        {
            return stem;
        }

        return $"{stem}_{Slug.Create(ramp.Name, ToneSlug)}";
    }

    /// <summary>
    /// The selections that contribute a real axis, in draw order.
    /// </summary>
    /// <remarks>
    /// Sorting once here is what makes every combination come out ordered without a second sort
    /// per combination — <see cref="AssetSlot"/>'s value <em>is</em> the draw order. A selection
    /// with no choices is dropped rather than multiplying the cross product by zero.
    /// </remarks>
    private static ImmutableArray<SlotSelection> Filled(ImmutableArray<SlotSelection> selections)
    {
        if (selections.IsDefaultOrEmpty)
        {
            return [];
        }

        var kept = selections.AsSpan().Where(static selection => !selection.Choices.IsDefaultOrEmpty).ToArray();

        Array.Sort(kept, static (left, right) => left.Slot.CompareTo(right.Slot));

        return [.. kept];
    }

    /// <summary>Whether <paramref name="filled"/> can be expanded, and if not, why.</summary>
    /// <remarks>
    /// The body is all-or-nothing. A run may commit to every required slot, or to none of them;
    /// committing to some is <see cref="PlanFailure.RequiredSlotEmpty"/>, which catches both a
    /// missing bottom and a head that offers <c>(none)</c>.
    /// </remarks>
    private static Optional<PlanFailure> Validate(ImmutableArray<SlotSelection> filled)
    {
        if (filled.IsEmpty)
        {
            return PlanFailure.NothingSelected;
        }

        var required = 0;
        var committed = 0;

        foreach (var slot in AssetSlots.DrawOrder)
        {
            if (!AssetSlots.IsRequired(slot))
            {
                continue;
            }

            required++;

            if (IsCommitted(filled, slot))
            {
                committed++;
            }
        }

        return committed is 0 || committed == required
            ? Optional<PlanFailure>.None
            : PlanFailure.RequiredSlotEmpty;
    }

    /// <summary>
    /// Whether every combination will carry a partial in <paramref name="slot"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the slot is selected and none of its choices is
    /// <see cref="Optional{T}.None"/>, <see langword="false"/> when it is absent or may be empty.
    /// </returns>
    private static bool IsCommitted(ImmutableArray<SlotSelection> filled, AssetSlot slot)
    {
        foreach (var selection in filled)
        {
            if (selection.Slot == slot)
            {
                return !selection.Choices.AsSpan().Any(static choice => !choice.HasValue);
            }
        }

        return false;
    }

    /// <summary>Reads the odometer into the partials one combination is made of.</summary>
    private static void Gather(
        ImmutableArray<SlotSelection> filled,
        ReadOnlySpan<int> odometer,
        List<AssetPartial> chosen)
    {
        chosen.Clear();

        for (var axis = 0; axis < filled.Length; axis++)
        {
            if (filled[axis].Choices[odometer[axis]].TryGet(out var partial))
            {
                chosen.Add(partial);
            }
        }
    }

    /// <summary>Steps the odometer to the next combination.</summary>
    /// <returns>
    /// <see langword="false"/> once every combination has been visited, which is what ends the
    /// walk.
    /// </returns>
    private static bool Advance(ImmutableArray<SlotSelection> filled, Span<int> odometer)
    {
        for (var axis = odometer.Length - 1; axis >= 0; axis--)
        {
            odometer[axis]++;

            if (odometer[axis] < filled[axis].Choices.Length)
            {
                return true;
            }

            odometer[axis] = 0;
        }

        return false;
    }

    /// <summary>Adds the sheets one combination produces — the tone axis lives here.</summary>
    private static void Emit(
        ImmutableArray<SheetRecipe>.Builder recipes,
        List<AssetPartial> chosen,
        ImmutableArray<SkinRamp> tones,
        SheetGeometry geometry)
    {
        var layers = LayersOf(chosen);

        if (!chosen.Any(static partial => AssetSlots.IsSkinBearing(partial.Slot)))
        {
            recipes.Add(new()
            {
                Name = StemFor(chosen, Optional<SkinRamp>.None),
                Layers = layers,
                Geometry = geometry,
            });

            return;
        }

        foreach (var tone in tones)
        {
            recipes.Add(new()
            {
                Name = StemFor(chosen, tone),
                Layers = layers,
                Tone = tone,
                Geometry = geometry,
            });
        }
    }

    /// <summary>
    /// The combination's layers, each carrying its slot's <see cref="AssetSlots.IsSkinBearing"/>
    /// verdict so the baker never has to look a slot up again.
    /// </summary>
    private static ImmutableArray<AssetLayer> LayersOf(List<AssetPartial> chosen)
    {
        var layers = ImmutableArray.CreateBuilder<AssetLayer>(chosen.Count);

        foreach (var partial in chosen)
        {
            layers.Add(new(partial.Path, AssetSlots.IsSkinBearing(partial.Slot)));
        }

        return layers.ToImmutable();
    }

    /// <summary>
    /// How many of a slot's choices leave a combination free of skin.
    /// </summary>
    /// <remarks>
    /// On a skin-bearing slot only <c>(none)</c> qualifies, because every partial there is skin by
    /// definition. On any other slot every choice does.
    /// </remarks>
    private static long SkinlessChoices(SlotSelection selection) =>
        AssetSlots.IsSkinBearing(selection.Slot)
            ? selection.Choices.AsSpan().Count(static choice => !choice.HasValue)
            : selection.Choices.Length;

    /// <summary>Multiplies without wrapping, clamping at <see cref="long.MaxValue"/>.</summary>
    private static long Saturate(long left, long right) =>
        left is 0 || right is 0 ? 0
        : left > long.MaxValue / right ? long.MaxValue
        : left * right;
}
