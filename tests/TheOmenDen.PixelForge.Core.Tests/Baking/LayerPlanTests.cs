using System.Collections.Immutable;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The layer expansion: bodies cross-product and take the tone axis, attachments do neither.
/// </summary>
public sealed class LayerPlanTests
{
    private static AssetPartial Partial(AssetSlot slot, string name) => new()
    {
        Slot = slot,
        Pack = ElementsPack.Core,
        Base = name,
        Variant = 0,
        Path = FullPath.FromPath($"C:/packs/core/assets/{AssetSlots.FolderName(slot)}/{name}.png"),
    };

    private static SlotSelection Slot(AssetSlot slot, params string[] names) => new()
    {
        Slot = slot,
        Choices = [.. names.Select(name => (Optional<AssetPartial>)Partial(slot, name))],
    };

    /// <summary>A slot that also offers <c>(none)</c>, which is how the picker presents optionals.</summary>
    private static SlotSelection Optional(AssetSlot slot, params string[] names) => new()
    {
        Slot = slot,
        Choices = [Optional<AssetPartial>.None, .. names.Select(name => (Optional<AssetPartial>)Partial(slot, name))],
    };

    private static ImmutableArray<SlotSelection> Body(params string[] bottoms) =>
    [
        Slot(AssetSlot.Bottom, bottoms),
        Slot(AssetSlot.Top, "top11"),
        Slot(AssetSlot.Head, "head1"),
    ];

    private static ImmutableArray<SkinRamp> Tones(int count) => [.. SkinRamps.All.AsSpan()[..count]];

    private static Dictionary<HeroKey, string> Labels(ImmutableArray<SlotSelection> selections)
    {
        var labels = new Dictionary<HeroKey, string>();
        var keys = LayerPlan.HeroKeys(selections);

        for (var i = 0; i < keys.Length; i++)
        {
            labels[keys[i]] = $"villager_{i + 1:00}";
        }

        return labels;
    }

    private static ImmutableArray<SheetRecipe> Expand(
        ImmutableArray<SlotSelection> selections,
        ImmutableArray<SkinRamp> tones,
        SheetGeometry geometry = SheetGeometry.Curated)
    {
        var expanded = LayerPlan.Expand(selections, tones, geometry, Labels(selections));

        Assert.True(expanded.IsSuccessful);

        return expanded.Value;
    }

    /// <summary>One body trio, one sheet per tone, all in that hero's directory.</summary>
    [Fact]
    public void Expand_BakesOneBodySheetPerTone()
    {
        var recipes = Expand(Body("bottom1"), Tones(3));

        Assert.Equal(3, recipes.Length);
        Assert.All(recipes, recipe => Assert.Equal("heroes/villager_01", recipe.Directory));
        Assert.All(recipes, recipe => Assert.Equal(3, recipe.Layers.Length));
    }

    /// <summary>
    /// The source ramp leaves no tone segment, exactly as it does for a composited stem — the
    /// default is silent.
    /// </summary>
    [Fact]
    public void Expand_NamesTheSourceToneSheetAfterTheHeroAlone()
    {
        var recipes = Expand(Body("bottom1"), [SkinRamps.Source]);

        Assert.Equal("villager_01", Assert.Single(recipes).Name);
    }

    [Fact]
    public void Expand_SuffixesEveryOtherToneOntoTheHeroLabel()
    {
        var toned = SkinRamps.All.AsValueEnumerable()
            .First(ramp => !string.Equals(ramp.Name, SkinRamps.Source.Name, StringComparison.OrdinalIgnoreCase));

        var recipes = Expand(Body("bottom1"), [toned]);

        Assert.StartsWith("villager_01_", Assert.Single(recipes).Name, StringComparison.Ordinal);
    }

    /// <summary>Distinct bodies are distinct heroes, and each gets its own directory.</summary>
    [Fact]
    public void Expand_GivesEachBodyItsOwnHeroDirectory()
    {
        var recipes = Expand(Body("bottom1", "bottom3"), [SkinRamps.Source]);

        Assert.Equal(2, recipes.Length);
        Assert.Equal(
            ["heroes/villager_01", "heroes/villager_02"],
            [.. recipes.Select(static recipe => recipe.Directory).Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// The whole point: an attachment is baked once, not once per hero and not once per tone.
    /// </summary>
    /// <remarks>
    /// Two bodies and three tones would be six sheets under a cross product for every hair as well.
    /// Here the three hairs stay three sheets, because an attachment carries no skin and is
    /// identical for every body.
    /// </remarks>
    [Fact]
    public void Expand_BakesEachAttachmentOnce_WhateverTheBodiesAndTones()
    {
        ImmutableArray<SlotSelection> selections =
        [
            .. Body("bottom1", "bottom3"),
            Optional(AssetSlot.Hair, "hair1", "hair7", "hair9"),
        ];

        var recipes = Expand(selections, Tones(3));

        var hair = recipes.Where(static recipe => string.Equals(recipe.Directory, "attachments/hair", StringComparison.Ordinal)).ToArray();

        Assert.Equal(3, hair.Length);
        Assert.All(hair, recipe => Assert.False(recipe.Tone.HasValue));
        Assert.All(hair, recipe => Assert.Single(recipe.Layers));

        // 2 bodies x 3 tones, plus 3 hairs baked once between them.
        Assert.Equal(9, recipes.Length);
    }

    /// <summary>The absence of a hat is not a sheet.</summary>
    [Fact]
    public void Expand_IgnoresTheNoneChoice()
    {
        ImmutableArray<SlotSelection> selections = [.. Body("bottom1"), Optional(AssetSlot.Hat, "hat3")];

        var recipes = Expand(selections, [SkinRamps.Source]);

        Assert.Equal("hat3", Assert.Single(recipes, r => string.Equals(r.Directory, "attachments/hat", StringComparison.Ordinal)).Name);
    }

    /// <summary>Attachments land in the slot folder the packs already use.</summary>
    [Fact]
    public void Expand_PutsEachAttachmentInItsSlotFolder()
    {
        ImmutableArray<SlotSelection> selections =
        [
            .. Body("bottom1"),
            Optional(AssetSlot.Weapon, "sword1"),
            Optional(AssetSlot.BackHair, "backhair2"),
        ];

        var recipes = Expand(selections, [SkinRamps.Source]);

        Assert.Contains(recipes, r => string.Equals(r.Directory, "attachments/weapon", StringComparison.Ordinal));
        Assert.Contains(recipes, r => string.Equals(r.Directory, "attachments/backhair", StringComparison.Ordinal));
    }

    /// <summary>
    /// An overlay-only run is legal — all three required slots empty — and produces attachments
    /// with no hero directory at all.
    /// </summary>
    [Fact]
    public void Expand_WithNoBody_ProducesAttachmentsOnly()
    {
        var recipes = Expand([Optional(AssetSlot.Hair, "hair1", "hair7")], Tones(3));

        Assert.Equal(2, recipes.Length);
        Assert.All(recipes, recipe => Assert.StartsWith("attachments/", recipe.Directory, StringComparison.Ordinal));
    }

    /// <summary>
    /// The collision that exists on main: two geometries planned from one selection must not
    /// produce two recipes with one path.
    /// </summary>
    [Fact]
    public void Expand_MarksTheNonDefaultGeometry_SoBothModesCanCoexist()
    {
        var curated = Expand(Body("bottom1"), [SkinRamps.Source]);
        var full = Expand(Body("bottom1"), [SkinRamps.Source], SheetGeometry.Full);

        Assert.Equal("villager_01", Assert.Single(curated).Name);
        Assert.Equal("villager_01_full", Assert.Single(full).Name);
        Assert.NotEqual(curated[0].RelativePath, full[0].RelativePath, StringComparer.Ordinal);
    }

    [Fact]
    public void Expand_RejectsAHalfCommittedBody()
    {
        var expanded = LayerPlan.Expand(
            [Slot(AssetSlot.Bottom, "bottom1"), Slot(AssetSlot.Top, "top11")],
            [SkinRamps.Source],
            SheetGeometry.Curated,
            new Dictionary<HeroKey, string>());

        Assert.False(expanded.IsSuccessful);
        Assert.Equal(PlanFailure.RequiredSlotEmpty, expanded.Error);
    }

    [Fact]
    public void Expand_RejectsAnEmptySelection()
    {
        var expanded = LayerPlan.Expand(
            [], [SkinRamps.Source], SheetGeometry.Curated, new Dictionary<HeroKey, string>());

        Assert.False(expanded.IsSuccessful);
        Assert.Equal(PlanFailure.NothingSelected, expanded.Error);
    }

    /// <summary>Every body combination is offered for labelling, in plan order.</summary>
    [Fact]
    public void HeroKeys_ReturnsOneKeyPerBodyCombination()
    {
        var keys = LayerPlan.HeroKeys(Body("bottom1", "bottom3"));

        Assert.Equal(2, keys.Length);
        Assert.Equal(new HeroKey("bottom1", "top11", "head1"), keys[0]);
        Assert.Equal(new HeroKey("bottom3", "top11", "head1"), keys[1]);
    }

    [Fact]
    public void HeroKeys_WithNoBody_IsEmpty() =>
        Assert.Empty(LayerPlan.HeroKeys([Optional(AssetSlot.Hair, "hair1")]));

    /// <summary>
    /// The count agrees with the expansion, which is what stops the page advertising a run it will
    /// not produce.
    /// </summary>
    [Fact]
    public void Count_AgreesWithWhatExpandProduces()
    {
        ImmutableArray<SlotSelection> selections =
        [
            .. Body("bottom1", "bottom3"),
            Optional(AssetSlot.Hair, "hair1", "hair7", "hair9"),
            Optional(AssetSlot.Hat, "hat3"),
        ];

        var counts = LayerPlan.Count(selections, Tones(3));

        Assert.Equal(2, counts.Heroes);
        Assert.Equal(3, counts.Tones);
        Assert.Equal(4, counts.Attachments);
        Assert.Equal(Expand(selections, Tones(3)).Length, counts.Sheets);
    }

    /// <summary>A selection the expansion would reject must not advertise a count.</summary>
    [Fact]
    public void Count_IsZero_WhenTheBodyIsHalfCommitted()
    {
        var counts = LayerPlan.Count(
            [Slot(AssetSlot.Bottom, "bottom1"), Slot(AssetSlot.Top, "top11")], Tones(3));

        Assert.Equal(0, counts.Sheets);
    }

    /// <summary>
    /// The headline number for the selection spec 001 is built around: 9 hair, 5 hats, 22 weapons,
    /// 7 tones. The cross product this replaced produced 9,660.
    /// </summary>
    [Fact]
    public void Count_ForTheWorkedExample_IsFortyThreeSheets()
    {
        ImmutableArray<SlotSelection> selections =
        [
            .. Body("bottom1"),
            Optional(AssetSlot.Hair, [.. Enumerable.Range(1, 9).Select(static i => $"hair{i}")]),
            Optional(AssetSlot.Hat, [.. Enumerable.Range(1, 5).Select(static i => $"hat{i}")]),
            Optional(AssetSlot.Weapon, [.. Enumerable.Range(1, 22).Select(static i => $"weapon{i}")]),
        ];

        var counts = LayerPlan.Count(selections, Tones(7));

        Assert.Equal(1, counts.Heroes);
        Assert.Equal(36, counts.Attachments);
        Assert.Equal(43, counts.Sheets);
    }
}
