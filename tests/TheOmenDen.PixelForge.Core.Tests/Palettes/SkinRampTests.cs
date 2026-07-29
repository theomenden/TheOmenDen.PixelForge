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

    // The two LegacySubstitutionFrom_* tests that stood here went with the member they named,
    // rather than being rewritten: RampSubstitutionTests already asserts both properties against
    // the surviving API, index alignment in SubstitutionFrom_PairsEveryStepInOrder and the
    // no-op case in IsIdentity_IsTrueOnlyWhenSourceAndTargetMatch.

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

    [Fact]
    public void IsBuiltIn_RecognisesEveryShippedRamp()
    {
        foreach (var ramp in SkinRamps.All)
        {
            Assert.True(SkinRamps.IsBuiltIn(ramp), $"{ramp.Name} should be built-in");
        }
    }

    [Fact]
    public void IsBuiltIn_MatchesOnNameIgnoringCase_SoACustomCannotShadowAShippedRamp()
    {
        var shadow = SkinRamps.Source with { Name = SkinRamps.Source.Name.ToUpperInvariant() };

        Assert.True(SkinRamps.IsBuiltIn(shadow));
    }

    [Fact]
    public void IsBuiltIn_IsFalse_ForANameThatIsNotShipped()
        => Assert.False(SkinRamps.IsBuiltIn(SkinRamps.Source with { Name = "Definitely Not Shipped" }));
}
