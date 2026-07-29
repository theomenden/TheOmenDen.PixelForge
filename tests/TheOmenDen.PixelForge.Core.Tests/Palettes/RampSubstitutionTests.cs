using SkiaSharp;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Palettes;

/// <summary>
/// Pins the packed pixel layout the vectorised recolour depends on. Getting the byte order
/// wrong here swaps red and blue in every baked sheet, and nothing downstream would notice —
/// the round-trip check compares an encode against its own decode, not against expected art.
/// </summary>
public sealed class RampSubstitutionTests
{
    /// <summary>
    /// RGBA8888 stores R,G,B,A in ascending address order, so a little-endian <c>uint</c> read of
    /// that memory is <c>0xAABBGGRR</c> — red in the low byte, the reverse of the
    /// <see cref="SkinRamp.Pack"/> key's <c>0xRRGGBB</c>.
    /// </summary>
    [Fact]
    public void PackedRgba_PutsRedInTheLowByteAndOpaqueAlphaInTheHigh()
    {
        var packed = SkinRamp.PackedRgba(new SKColor(0x73, 0x17, 0x2D, 0xFF));

        Assert.Equal(0xFF2D1773u, packed);
    }

    [Fact]
    public void PackedRgba_ForcesOpaqueAlpha_RegardlessOfTheColoursOwn()
    {
        var packed = SkinRamp.PackedRgba(new SKColor(0x12, 0x34, 0x56, 0x00));

        Assert.Equal(0xFFu, packed >> 24);
    }

    [Fact]
    public void PackedRgba_AndPack_DescribeTheSameColour()
    {
        foreach (var step in SkinRamps.Source.Steps)
        {
            var packed = SkinRamp.PackedRgba(step);

            Assert.Equal((uint)step.Red, packed & 0xFF);
            Assert.Equal((uint)step.Green, (packed >> 8) & 0xFF);
            Assert.Equal((uint)step.Blue, (packed >> 16) & 0xFF);
            Assert.Equal(SkinRamp.Pack(step), ((uint)step.Red << 16) | ((uint)step.Green << 8) | step.Blue);
        }
    }

    [Fact]
    public void SubstitutionFrom_PairsEveryStepInOrder()
    {
        var target = SkinRamps.All[4];
        var substitution = target.SubstitutionFrom(SkinRamps.Source);

        Assert.Equal(SkinRamps.StepCount, substitution.Length);

        for (var step = 0; step < SkinRamps.StepCount; step++)
        {
            Assert.Equal(SkinRamp.PackedRgba(SkinRamps.Source.Steps[step]), substitution.From[step]);
            Assert.Equal(SkinRamp.PackedRgba(target.Steps[step]), substitution.To[step]);
        }
    }

    /// <summary>
    /// The default tone is the ramp the art is already authored in, so substituting it is a no-op.
    /// The baker uses this to skip a pass over 212,000 pixels per layer.
    /// </summary>
    [Fact]
    public void IsIdentity_IsTrueOnlyWhenSourceAndTargetMatch()
    {
        Assert.True(SkinRamps.Source.SubstitutionFrom(SkinRamps.Source).IsIdentity);
        Assert.False(SkinRamps.All[3].SubstitutionFrom(SkinRamps.Source).IsIdentity);
    }
}
