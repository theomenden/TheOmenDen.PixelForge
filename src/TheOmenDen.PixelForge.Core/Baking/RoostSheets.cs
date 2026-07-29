using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// The sheets spec 079 ships: seven bodies and nine hair styles.
/// <para>
/// This is the whole art selection, in one table, on purpose. Swapping a hairstyle or an
/// outfit is a one-line edit here rather than a change to the baking machinery.
/// </para>
/// </summary>
public static class RoostSheets
{
    /// <summary>
    /// Bottom, top, head — generator draw orders 3, 4 and 5. Shadow (0) is deliberately
    /// omitted: it is optional in the generator and the overlay composes its own stack.
    /// <para>
    /// The middle element is the slot's folder name in every pack, which is also how the enum
    /// member spells itself — see <see cref="AssetSlots.FolderName"/>. It is parsed back into an
    /// <see cref="AssetSlot"/> so the <see cref="AssetLayer.IsSkin"/> flag comes from
    /// <see cref="AssetSlots.IsSkinBearing"/> rather than being restated here, where it could
    /// drift.
    /// </para>
    /// </summary>
    private static ImmutableArray<(ElementsPack Pack, string Slot, string File)> BodyLayers { get; } =
    [
        (ElementsPack.Core, "bottom", "bottom1.png"),
        (ElementsPack.Core, "top", "top11.png"),
        (ElementsPack.Core, "head", "head1.png"),
    ];

    /// <summary>
    /// Turns one of <see cref="BodyLayers"/>' folder names back into the slot it names.
    /// </summary>
    /// <param name="folder">A slot folder name such as <c>top</c>.</param>
    /// <returns>The matching <see cref="AssetSlot"/>.</returns>
    /// <remarks>
    /// Parsed rather than mapped: <see cref="AssetSlots.FolderName"/> is the member name
    /// lowercased, so a case-insensitive <see cref="Enum.Parse{TEnum}(string, bool)"/> is its
    /// exact inverse and cannot fall out of step with the enum.
    /// </remarks>
    private static AssetSlot ToSlot(string folder) => Enum.Parse<AssetSlot>(folder, ignoreCase: true);

    /// <summary>Three masc, three femme, three neutral — chosen for distinct 48px silhouettes.</summary>
    private static ImmutableArray<(ElementsPack Pack, string File)> HairPicks { get; } =
    [
        (ElementsPack.Core, "hair1.png"),                  // masc    — messy spiky
        (ElementsPack.Core, "hair7.png"),                  // masc    — short crop
        (ElementsPack.CharacterExpansion1, "hair20.png"),  // masc    — quiff
        (ElementsPack.Core, "hair10.png"),                 // femme   — twin buns
        (ElementsPack.CharacterExpansion1, "hair15.png"),  // femme   — long parted
        (ElementsPack.CharacterExpansion2, "hair24.png"),  // femme   — bob + ribbon
        (ElementsPack.Core, "hair9.png"),                  // neutral — parted fringe
        (ElementsPack.CharacterExpansion1, "hair13.png"),  // neutral — band
        (ElementsPack.CharacterExpansion1, "hair16.png"),  // neutral — short bob
    ];

    /// <summary>One body sheet per skin ramp, all sharing the same three partials.</summary>
    /// <param name="packs">Where the source packs live. Never <see langword="null"/>.</param>
    /// <returns>
    /// One recipe per entry in <see cref="SkinRamps.All"/>, named <c>body-01</c> upwards.
    /// </returns>
    public static ImmutableArray<SheetRecipe> Bodies(SourcePacks packs)
    {
        Guard.IsNotNull(packs);

        var layers = ImmutableArray.CreateBuilder<AssetLayer>(BodyLayers.Length);

        foreach (var (pack, slot, file) in BodyLayers)
        {
            layers.Add(new(packs.Partial(pack, slot, file), AssetSlots.IsSkinBearing(ToSlot(slot))));
        }

        var resolved = layers.ToImmutable();
        var recipes = ImmutableArray.CreateBuilder<SheetRecipe>(SkinRamps.All.Length);

        for (var i = 0; i < SkinRamps.All.Length; i++)
        {
            recipes.Add(new()
            {
                Name = $"body-{i + 1:00}",
                Layers = resolved,
                Tone = SkinRamps.All[i],
            });
        }

        return recipes.ToImmutable();
    }

    /// <summary>
    /// Hair bakes as its own sheet with no body under it — it is a true stacked layer in the
    /// overlay, sharing the body's grid so one frame map describes both.
    /// </summary>
    /// <param name="packs">Where the source packs live. Never <see langword="null"/>.</param>
    /// <returns>One recipe per entry in <see cref="HairPicks"/>, named <c>hair-01</c> upwards.</returns>
    /// <remarks>
    /// Every layer is <see cref="AssetLayer.IsSkin"/> <see langword="false"/>, matching
    /// <see cref="AssetSlots.IsSkinBearing"/> for <see cref="AssetSlot.Hair"/>: a hairstyle keeps
    /// its authored colour, and some of them legitimately use skin-ramp hexes as highlights.
    /// </remarks>
    public static ImmutableArray<SheetRecipe> Hair(SourcePacks packs)
    {
        Guard.IsNotNull(packs);

        var recipes = ImmutableArray.CreateBuilder<SheetRecipe>(HairPicks.Length);

        for (var i = 0; i < HairPicks.Length; i++)
        {
            var (pack, file) = HairPicks[i];

            recipes.Add(new()
            {
                Name = $"hair-{i + 1:00}",
                Layers = [new(packs.Partial(pack, "hair", file), AssetSlots.IsSkinBearing(AssetSlot.Hair))],
            });
        }

        return recipes.ToImmutable();
    }

    /// <summary>Every sheet the spec ships, bodies first.</summary>
    /// <param name="packs">Where the source packs live. Never <see langword="null"/>.</param>
    /// <returns><see cref="Bodies"/> followed by <see cref="Hair"/>.</returns>
    public static ImmutableArray<SheetRecipe> All(SourcePacks packs) =>
        [.. Bodies(packs), .. Hair(packs)];

    /// <summary>
    /// The spec-079 art as a picker selection, so the shipped set can be loaded, inspected and
    /// varied from the batch page.
    /// </summary>
    /// <param name="catalog">The scanned packs to resolve the picks against.</param>
    /// <returns>
    /// One <see cref="SlotSelection"/> per filled slot. Partials the catalogue does not hold are
    /// dropped rather than faulted — a pack pointed somewhere wrong should show an obviously short
    /// selection, not an error dialog — and a slot left with nothing at all is omitted rather than
    /// returned empty, since <see cref="BatchPlan"/> would drop it anyway.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is a convenience for exploration. The files Corvus consumes come from
    /// <see cref="All"/>, which names them explicitly; a stem generated by
    /// <see cref="BatchPlan.StemFor"/> would not match the registry.
    /// </para>
    /// <para>
    /// No choice is <see cref="Optional{T}.None"/>. Offering <c>(none)</c> on the required trio is
    /// <see cref="PlanFailure.RequiredSlotEmpty"/>, so a preset that did would be rejected the
    /// moment it was loaded.
    /// </para>
    /// </remarks>
    public static ImmutableArray<SlotSelection> Selection(AssetCatalog catalog)
    {
        Guard.IsNotNull(catalog);

        var selections = new List<SlotSelection>(BodyLayers.Length + 1);

        foreach (var (_, slot, file) in BodyLayers)
        {
            Pick(selections, catalog, ToSlot(slot), [Path.GetFileNameWithoutExtension(file)]);
        }

        Pick(
            selections,
            catalog,
            AssetSlot.Hair,
            [.. HairPicks.AsSpan().Select(static pick => Path.GetFileNameWithoutExtension(pick.File))]);

        return [.. selections];
    }

    /// <summary>
    /// Appends one slot's selection, keeping only the bases <paramref name="catalog"/> actually
    /// holds and skipping the slot entirely when that leaves nothing.
    /// </summary>
    /// <remarks>
    /// Every pick is a base file, so the variant is always <c>0</c> — the picker's per-slot
    /// variants toggle is what expands one into its <c>_cN</c> siblings, and the spec ships the
    /// authored colours.
    /// </remarks>
    private static void Pick(
        List<SlotSelection> selections,
        AssetCatalog catalog,
        AssetSlot slot,
        ReadOnlySpan<string> bases)
    {
        var choices = ImmutableArray.CreateBuilder<Optional<AssetPartial>>(bases.Length);

        foreach (var name in bases)
        {
            if (catalog.Find(slot, name, variant: 0).TryGet(out var partial))
            {
                choices.Add(partial);
            }
        }

        if (choices.Count is not 0)
        {
            selections.Add(new()
            {
                Slot = slot,
                Choices = choices.ToImmutable(),
            });
        }
    }
}
