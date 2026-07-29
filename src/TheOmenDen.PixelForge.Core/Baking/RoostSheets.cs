using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using Meziantou.Framework;
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
    /// Both garment partials carry zero skin-ramp pixels, so the recolour can only ever reach
    /// the face. That is what makes a blind five-colour substitution safe here — see the
    /// collision cases in <c>hair1</c> and <c>hat4</c>, which use ramp colours as hair and trim.
    /// </para>
    /// </summary>
    private static ImmutableArray<(ElementsPack Pack, string Slot, string File)> BodyLayers { get; } =
    [
        (ElementsPack.Core, "bottom", "bottom1.png"),
        (ElementsPack.Core, "top", "top11.png"),
        (ElementsPack.Core, "head", "head1.png"),
    ];

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

    public static ImmutableArray<SheetRecipe> Bodies(SourcePacks packs)
    {
        Guard.IsNotNull(packs);

        var layers = ImmutableArray.CreateBuilder<FullPath>(BodyLayers.Length);

        foreach (var (pack, slot, file) in BodyLayers)
        {
            layers.Add(packs.Partial(pack, slot, file));
        }

        var resolved = layers.ToImmutable();
        var recipes = ImmutableArray.CreateBuilder<SheetRecipe>(SkinRamps.All.Length);

        for (var i = 0; i < SkinRamps.All.Length; i++)
        {
            recipes.Add(new()
            {
                Name = $"body-{i + 1:00}",
                Layers = resolved,
                Recolor = SkinRamps.All[i],
            });
        }

        return recipes.ToImmutable();
    }

    /// <summary>
    /// Hair bakes as its own sheet with no body under it — it is a true stacked layer in the
    /// overlay, sharing the body's grid so one frame map describes both. Never recoloured:
    /// a hairstyle keeps its authored colour, and some of them legitimately use skin-ramp hexes.
    /// </summary>
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
                Layers = [packs.Partial(pack, "hair", file)],
            });
        }

        return recipes.ToImmutable();
    }

    /// <summary>Every sheet the spec ships, bodies first.</summary>
    public static ImmutableArray<SheetRecipe> All(SourcePacks packs) =>
        [.. Bodies(packs), .. Hair(packs)];
}
