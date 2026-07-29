using System.Collections.Immutable;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The planner turns a per-slot selection into recipes. Its subtle rule is the tone axis: it
/// multiplies a combination only when that combination actually carries skin, so selecting hair
/// alone yields one sheet per style rather than one per style per tone.
/// </summary>
public sealed class BatchPlanTests
{
    private static AssetPartial Partial(AssetSlot slot, string baseName, int variant = 0) => new()
    {
        Slot = slot,
        Pack = ElementsPack.Core,
        Base = baseName,
        Variant = variant,
        Path = FullPath.FromPath(Path.Combine(Path.GetTempPath(), $"{baseName}.png")),
    };

    private static Optional<AssetPartial>[] Choices(ReadOnlySpan<AssetPartial> partials) =>
        partials.Select(static partial => (Optional<AssetPartial>)partial).ToArray();

    private static SlotSelection Selection(AssetSlot slot, params ReadOnlySpan<AssetPartial> partials) =>
        new() { Slot = slot, Choices = [.. Choices(partials)] };

    /// <summary>A selection that also offers "no piece in this slot".</summary>
    private static SlotSelection WithNone(AssetSlot slot, params ReadOnlySpan<AssetPartial> partials) =>
        new()
        {
            Slot = slot,
            Choices = [Optional<AssetPartial>.None, .. Choices(partials)],
        };

    private static ImmutableArray<SlotSelection> Body() =>
    [
        Selection(AssetSlot.Bottom, Partial(AssetSlot.Bottom, "bottom1")),
        Selection(AssetSlot.Top, Partial(AssetSlot.Top, "top11")),
        Selection(AssetSlot.Head, Partial(AssetSlot.Head, "head1")),
    ];

    private static ImmutableArray<SheetRecipe> ExpandOrFail(
        ImmutableArray<SlotSelection> selections,
        ImmutableArray<SkinRamp> tones)
    {
        var result = BatchPlan.Expand(selections, tones, SheetGeometry.Curated);

        Assert.True(result.IsSuccessful, $"expand failed with {result.Error}");

        return result.Value;
    }

    [Fact]
    public void Expand_MultipliesEveryAxis()
    {
        ImmutableArray<SlotSelection> selections =
        [
            Selection(AssetSlot.Bottom, Partial(AssetSlot.Bottom, "bottom1"), Partial(AssetSlot.Bottom, "bottom9")),
            Selection(AssetSlot.Top, Partial(AssetSlot.Top, "top11"), Partial(AssetSlot.Top, "top15"), Partial(AssetSlot.Top, "top23")),
            Selection(AssetSlot.Head, Partial(AssetSlot.Head, "head1")),
            Selection(AssetSlot.Hair, Partial(AssetSlot.Hair, "hair1"), Partial(AssetSlot.Hair, "hair7"),
                Partial(AssetSlot.Hair, "hair15"), Partial(AssetSlot.Hair, "hair24")),
        ];

        var recipes = ExpandOrFail(selections, SkinRamps.All);

        // 2 bottoms x 3 tops x 1 head x 4 hair x 7 tones
        Assert.Equal(168, recipes.Length);
    }

    /// <summary>The live planned-count label must not be able to disagree with the run.</summary>
    [Fact]
    public void Count_AgreesWithExpand()
    {
        ImmutableArray<SlotSelection> selections =
        [
            .. Body(),
            WithNone(AssetSlot.Hat, Partial(AssetSlot.Hat, "hat4")),
        ];

        var recipes = ExpandOrFail(selections, SkinRamps.All);

        Assert.Equal(recipes.Length, BatchPlan.Count(selections, SkinRamps.All));
    }

    /// <summary>
    /// The Corvus contract in one assertion: hair alone is nine sheets, not sixty-three.
    /// </summary>
    [Fact]
    public void Expand_DoesNotApplyTheToneAxis_WhenNothingSelectedCarriesSkin()
    {
        var selections = ImmutableArray.Create(Selection(
            AssetSlot.Hair,
            Partial(AssetSlot.Hair, "hair1"),
            Partial(AssetSlot.Hair, "hair7"),
            Partial(AssetSlot.Hair, "hair9")));

        var recipes = ExpandOrFail(selections, SkinRamps.All);

        Assert.Equal(3, recipes.Length);
        Assert.All(recipes, recipe => Assert.False(recipe.Tone.HasValue));
    }

    /// <summary>
    /// A mixed selection must not pay the tone axis on the skinless combinations either — the
    /// "no top" combination is one sheet, not seven identical ones.
    /// </summary>
    [Fact]
    public void Expand_AppliesTheToneAxisPerCombination()
    {
        ImmutableArray<SlotSelection> selections =
        [
            Selection(AssetSlot.Hair, Partial(AssetSlot.Hair, "hair1")),
            WithNone(AssetSlot.Top, Partial(AssetSlot.Top, "top11")),
        ];

        var recipes = ExpandOrFail(selections, SkinRamps.All);

        // hair-only combination: 1 sheet. hair + top combination: 7 tones.
        Assert.Equal(8, recipes.Length);
        Assert.Single(recipes.AsSpan().Where(static recipe => !recipe.Tone.HasValue).ToArray());
        Assert.Equal(recipes.Length, BatchPlan.Count(selections, SkinRamps.All));
    }

    [Fact]
    public void Expand_OrdersLayersByDrawOrder()
    {
        ImmutableArray<SlotSelection> selections =
        [
            Selection(AssetSlot.Hat, Partial(AssetSlot.Hat, "hat4")),
            Selection(AssetSlot.Bottom, Partial(AssetSlot.Bottom, "bottom1")),
            Selection(AssetSlot.Head, Partial(AssetSlot.Head, "head1")),
            Selection(AssetSlot.Top, Partial(AssetSlot.Top, "top11")),
        ];

        var recipe = ExpandOrFail(selections, [SkinRamps.Source])[0];

        Assert.Equal(4, recipe.Layers.Length);
        Assert.EndsWith("bottom1.png", recipe.Layers[0].Path.Value, StringComparison.Ordinal);
        Assert.EndsWith("top11.png", recipe.Layers[1].Path.Value, StringComparison.Ordinal);
        Assert.EndsWith("head1.png", recipe.Layers[2].Path.Value, StringComparison.Ordinal);
        Assert.EndsWith("hat4.png", recipe.Layers[3].Path.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_MarksOnlySkinBearingLayers()
    {
        ImmutableArray<SlotSelection> selections =
        [
            .. Body(),
            Selection(AssetSlot.Weapon, Partial(AssetSlot.Weapon, "bow1")),
        ];

        var recipe = ExpandOrFail(selections, [SkinRamps.Source])[0];

        Assert.True(recipe.Layers[0].IsSkin);   // bottom
        Assert.True(recipe.Layers[1].IsSkin);   // top
        Assert.True(recipe.Layers[2].IsSkin);   // head
        Assert.False(recipe.Layers[3].IsSkin);  // weapon keeps its wooden tan
    }

    [Fact]
    public void Expand_ReportsRequiredSlotEmpty_WhenTheBodyIsIncomplete()
    {
        var selections = ImmutableArray.Create(Selection(AssetSlot.Top, Partial(AssetSlot.Top, "top11")));

        var result = BatchPlan.Expand(selections, [SkinRamps.Source], SheetGeometry.Curated);

        Assert.False(result.IsSuccessful);
        Assert.Equal(PlanFailure.RequiredSlotEmpty, result.Error);
    }

    /// <summary>A required slot offering "(none)" is the same error, stated differently.</summary>
    [Fact]
    public void Expand_ReportsRequiredSlotEmpty_WhenARequiredSlotOffersNone()
    {
        ImmutableArray<SlotSelection> selections =
        [
            Selection(AssetSlot.Bottom, Partial(AssetSlot.Bottom, "bottom1")),
            WithNone(AssetSlot.Top, Partial(AssetSlot.Top, "top11")),
            Selection(AssetSlot.Head, Partial(AssetSlot.Head, "head1")),
        ];

        var result = BatchPlan.Expand(selections, [SkinRamps.Source], SheetGeometry.Curated);

        Assert.False(result.IsSuccessful);
        Assert.Equal(PlanFailure.RequiredSlotEmpty, result.Error);
    }

    /// <summary>An invalid selection plans nothing, so the label cannot advertise a run that fails.</summary>
    [Fact]
    public void Count_IsZero_WhenTheSelectionCannotBeExpanded()
    {
        var selections = ImmutableArray.Create(Selection(AssetSlot.Top, Partial(AssetSlot.Top, "top11")));

        Assert.Equal(0, BatchPlan.Count(selections, SkinRamps.All));
    }

    [Fact]
    public void Expand_ReportsNothingSelected_WhenThereAreNoSelectionsAtAll()
    {
        var result = BatchPlan.Expand([], [SkinRamps.Source], SheetGeometry.Curated);

        Assert.False(result.IsSuccessful);
        Assert.Equal(PlanFailure.NothingSelected, result.Error);
    }

    [Fact]
    public void StemFor_JoinsSlotsInDrawOrderAndAppendsTheTone()
    {
        AssetPartial[] chosen =
        [
            Partial(AssetSlot.Bottom, "bottom1"),
            Partial(AssetSlot.Top, "top11"),
            Partial(AssetSlot.Head, "head1"),
            Partial(AssetSlot.Hair, "hair15", 3),
        ];

        var stem = BatchPlan.StemFor(chosen, SkinRamps.All[4]);

        Assert.Equal("bottom1_top11_head1_hair15c3_tone-4-green", stem);
    }

    /// <summary>
    /// The default tone is the ramp the art is already authored in, so naming it would put a
    /// redundant segment on the majority of files.
    /// </summary>
    [Fact]
    public void StemFor_OmitsTheToneSegment_ForTheSourceToneAndForNoTone()
    {
        AssetPartial[] chosen = [Partial(AssetSlot.Hair, "hair1")];

        Assert.Equal("hair1", BatchPlan.StemFor(chosen, SkinRamps.Source));
        Assert.Equal("hair1", BatchPlan.StemFor(chosen, Optional<SkinRamp>.None));
    }

    [Fact]
    public void Expand_ProducesDistinctNames()
    {
        ImmutableArray<SlotSelection> selections =
        [
            .. Body(),
            Selection(AssetSlot.Hair, Partial(AssetSlot.Hair, "hair1"), Partial(AssetSlot.Hair, "hair1", 2)),
        ];

        var recipes = ExpandOrFail(selections, SkinRamps.All);
        var names = recipes.AsSpan().Select(static recipe => recipe.Name).ToArray();

        Assert.Equal(names.Length, new HashSet<string>(names, StringComparer.Ordinal).Count);
    }

    [Fact]
    public void Expand_StampsTheRequestedGeometryOnEveryRecipe()
    {
        var result = BatchPlan.Expand(Body(), [SkinRamps.Source], SheetGeometry.Full);

        Assert.All(result.Value, recipe => Assert.Equal(SheetGeometry.Full, recipe.Geometry));
    }
}
