using SkiaSharp;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Palettes;

/// <summary>
/// Pins the seven palette swaps to the values in the Elements generator's Settings.json, and
/// the four human base tones to the hexes spec 079 names.
/// </summary>
public sealed class SkinRampTests
{
    [Fact]
    public void All_HasSevenTones()
        => Assert.Equal(7, SkinRamps.All.Length);

    [Fact]
    public void EveryRamp_HasFiveSteps()
    {
        foreach (var ramp in SkinRamps.All)
        {
            Assert.Equal(SkinRamps.StepCount, ramp.Steps.Length);
        }
    }

    [Fact]
    public void Source_IsTheRampTheShippedArtIsAuthoredIn()
    {
        Assert.Same(SkinRamps.Source, SkinRamps.All[0]);
        Assert.Equal(new SKColor(0xF4, 0xD2, 0x9C), SkinRamps.Source.BaseTone);
    }

    /// <summary>
    /// The hashed default draws from this pool only. If a fantasy tone leaked in, an
    /// un-customized flock would render random green and bone viewers.
    /// </summary>
    [Fact]
    public void Human_ExcludesTheThreeFantasyTones()
    {
        Assert.Equal(4, SkinRamps.Human.Length);
        Assert.DoesNotContain(SkinRamps.Human, static r => !r.IsHuman);
        Assert.Equal(
            ["Default Tone", "Tone 1", "Tone 2", "Tone 3"],
            SkinRamps.Human.AsSpan().Select(static r => r.Name).ToArray());
    }

    [Theory]
    [InlineData(0, 0xF4, 0xD2, 0x9C)]
    [InlineData(1, 0xD4, 0x91, 0x49)]
    [InlineData(2, 0xB9, 0x7E, 0x50)]
    [InlineData(3, 0x98, 0x67, 0x43)]
    public void BaseTone_MatchesTheSpecifiedHex(int index, byte r, byte g, byte b)
        => Assert.Equal(new SKColor(r, g, b), SkinRamps.All[index].BaseTone);

    [Fact]
    public void SubstitutionFrom_IsIdentity_WhenTargetIsTheSourceRamp()
    {
        var substitution = SkinRamps.Source.SubstitutionFrom(SkinRamps.Source);

        foreach (var step in SkinRamps.Source.Steps)
        {
            Assert.Equal(step, substitution[SkinRamp.Pack(step)]);
        }
    }

    [Fact]
    public void SubstitutionFrom_MapsEachStepToTheSameIndex()
    {
        var target = SkinRamps.All[3];
        var substitution = target.SubstitutionFrom(SkinRamps.Source);

        Assert.Equal(SkinRamps.StepCount, substitution.Count);

        for (var i = 0; i < SkinRamps.StepCount; i++)
        {
            var key = SkinRamp.Pack(SkinRamps.Source.Steps[i]);

            Assert.Equal(target.Steps[i], substitution[key]);
        }
    }

    /// <summary>
    /// A recolour must be reversible index-for-index, which only holds if no two steps in a
    /// ramp collapse onto the same colour.
    /// </summary>
    [Fact]
    public void EveryRamp_HasFiveDistinctSteps()
    {
        foreach (var ramp in SkinRamps.All)
        {
            var packed = ramp.Steps.AsSpan().Select(SkinRamp.Pack).ToArray();

            Assert.Equal(SkinRamps.StepCount, packed.AsSpan().Distinct().ToArray().Length);
        }
    }
}
