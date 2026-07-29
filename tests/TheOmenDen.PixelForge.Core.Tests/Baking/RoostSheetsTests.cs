using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

public sealed class RoostSheetsTests
{
    private static SourcePacks Packs { get; } = new()
    {
        CoreAssets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "core")),
        Expansion1Assets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "x1")),
        Expansion2Assets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "x2")),
    };

    [Fact]
    public void Flattened_ProducesOneRecipePerBodyAndHairPair()
    {
        var bodies = RoostSheets.Bodies(Packs);
        var hair = RoostSheets.Hair(Packs);

        var flattened = RoostSheets.Flattened(bodies, hair);

        Assert.Equal(bodies.Length * hair.Length, flattened.Length);
    }

    [Fact]
    public void Flattened_NamesEachSheetForItsBodyAndHair()
    {
        var bodies = RoostSheets.Bodies(Packs);
        var hair = RoostSheets.Hair(Packs);

        var flattened = RoostSheets.Flattened(bodies, hair);

        Assert.Equal("body-01_hair-01", flattened[0].Name);
        Assert.Equal($"{bodies[^1].Name}_{hair[^1].Name}", flattened[^1].Name);
    }

    /// <summary>
    /// The body's layers and ramp carry over; the hair becomes an overlay so the recolour
    /// cannot reach it.
    /// </summary>
    [Fact]
    public void Flattened_CarriesTheBodyRamp_AndPutsHairInOverlays()
    {
        var bodies = RoostSheets.Bodies(Packs);
        var hair = RoostSheets.Hair(Packs);

        var flattened = RoostSheets.Flattened(bodies, hair);

        Assert.Equal(bodies[0].Layers, flattened[0].Layers);
        Assert.Equal(hair[0].Layers, flattened[0].Overlays);

        Assert.True(flattened[0].Recolor.TryGet(out var ramp));
        Assert.Equal(SkinRamps.All[0].Name, ramp.Name);
    }

    [Fact]
    public void Flattened_ReturnsEmpty_WhenEitherSideIsEmpty()
    {
        var bodies = RoostSheets.Bodies(Packs);

        Assert.Empty(RoostSheets.Flattened(bodies, []));
        Assert.Empty(RoostSheets.Flattened([], RoostSheets.Hair(Packs)));
    }
}
