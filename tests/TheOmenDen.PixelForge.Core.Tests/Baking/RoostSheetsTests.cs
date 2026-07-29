using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The spec-079 table seeds every layer's <see cref="AssetLayer.IsSkin"/> flag from its slot, so
/// these pin the seeding rather than the art. The body/hair cross product used to live here as
/// <c>Flattened</c>; it moves to the batch planner, which builds it from a per-slot selection.
/// </summary>
public sealed class RoostSheetsTests
{
    private static SourcePacks Packs { get; } = new()
    {
        CoreAssets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "core")),
        Expansion1Assets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "x1")),
        Expansion2Assets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "x2")),
    };

    /// <summary>
    /// Bottom, top and head are all skin-bearing, so a body's every layer takes the tone. The
    /// tone itself must be present, or the flags would have nothing to act on.
    /// </summary>
    [Fact]
    public void Bodies_MarkEveryLayerAsSkinAndCarryATone()
    {
        var bodies = RoostSheets.Bodies(Packs);

        Assert.Equal(SkinRamps.All.Length, bodies.Length);

        foreach (var body in bodies)
        {
            Assert.All(body.Layers, layer => Assert.True(layer.IsSkin));
            Assert.True(body.Tone.HasValue);
        }

        Assert.True(bodies[0].Tone.TryGet(out var ramp));
        Assert.Equal(SkinRamps.All[0].Name, ramp.Name);
    }

    /// <summary>
    /// Hair keeps its authored colour — some styles use skin-ramp hexes as highlights — so no
    /// hair layer may be marked skin and no hair recipe may carry a tone.
    /// </summary>
    [Fact]
    public void Hair_MarksNoLayerAsSkinAndCarriesNoTone()
    {
        var hair = RoostSheets.Hair(Packs);

        Assert.NotEmpty(hair);

        foreach (var style in hair)
        {
            Assert.All(style.Layers, layer => Assert.False(layer.IsSkin));
            Assert.False(style.Tone.HasValue);
        }
    }

    [Fact]
    public void All_IsBodiesThenHair()
    {
        var all = RoostSheets.All(Packs);

        Assert.Equal(RoostSheets.Bodies(Packs).Length + RoostSheets.Hair(Packs).Length, all.Length);
        Assert.Equal("body-01", all[0].Name);
        Assert.StartsWith("hair-", all[^1].Name, StringComparison.Ordinal);
    }
}
