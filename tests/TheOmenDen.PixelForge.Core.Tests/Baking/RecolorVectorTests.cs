using System.Numerics;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The vector path is an optimisation of the scalar one, so the scalar one is the oracle: every
/// assertion here is "these two agree", plus the boundary conditions that a hand-written SIMD
/// loop gets wrong — a length that is not a whole number of vectors, and the alpha assumption the
/// whole-pixel comparison rests on.
/// </summary>
public sealed class RecolorVectorTests
{
    private static RampSubstitution Substitution() => SkinRamps.All[3].SubstitutionFrom(SkinRamps.Source);

    /// <summary>
    /// A buffer of every ramp colour, some non-ramp colours, and transparent pixels, at a length
    /// deliberately coprime with any vector width so the scalar tail is always exercised.
    /// </summary>
    private static uint[] MixedBuffer(int length)
    {
        var buffer = new uint[length];

        for (var i = 0; i < length; i++)
        {
            buffer[i] = (i % 8) switch
            {
                0 or 1 or 2 or 3 or 4 => SkinRamp.PackedRgba(SkinRamps.Source.Steps[i % 5]),
                5 => 0xFF563412u,                              // opaque, not in the ramp
                6 => 0x00000000u,                              // fully transparent
                _ => SkinRamp.PackedRgba(SkinRamps.Source.Steps[0]) & 0x00FFFFFFu,  // ramp RGB, alpha 0
            };
        }

        return buffer;
    }

    [Fact]
    public void Substitute_AgreesWithTheScalarReference()
    {
        var substitution = Substitution();

        // 1003 is odd, so it is never a whole multiple of Vector<uint>.Count.
        var vectorised = MixedBuffer(1003);
        var scalar = (uint[])vectorised.Clone();

        SheetBaker.Substitute(vectorised, substitution);
        SheetBaker.SubstituteScalar(scalar, substitution);

        Assert.Equal(scalar, vectorised);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    [InlineData(65)]
    public void Substitute_AgreesWithTheScalarReference_AtEveryAwkwardLength(int length)
    {
        var substitution = Substitution();
        var vectorised = MixedBuffer(length);
        var scalar = (uint[])vectorised.Clone();

        SheetBaker.Substitute(vectorised, substitution);
        SheetBaker.SubstituteScalar(scalar, substitution);

        Assert.Equal(scalar, vectorised);
    }

    /// <summary>
    /// The whole-pixel comparison is what makes the loop cheap, and this is the property it buys:
    /// a transparent pixel is never rewritten, even when its RGB happens to equal a ramp colour.
    /// </summary>
    [Fact]
    public void Substitute_LeavesTransparentPixelsAlone_EvenWhenTheirRgbMatchesTheRamp()
    {
        var substitution = Substitution();
        var rampRgbButTransparent = SkinRamp.PackedRgba(SkinRamps.Source.Steps[0]) & 0x00FFFFFFu;

        uint[] buffer = [rampRgbButTransparent];

        SheetBaker.Substitute(buffer, substitution);

        Assert.Equal(rampRgbButTransparent, buffer[0]);
    }

    /// <summary>
    /// Documents the boundary rather than guarding live input: a semi-transparent ramp pixel is
    /// <em>not</em> substituted. No such pixel exists in the shipped packs — all 995 partials
    /// decode with strictly binary alpha — but art authored with antialiased edges would both
    /// break this and break <see cref="SheetBaker.Assemble"/>'s exact premultiplied round trip.
    /// </summary>
    [Fact]
    public void Substitute_SkipsSemiTransparentRampPixels_WhichTheShippedPacksNeverContain()
    {
        var substitution = Substitution();
        var halfAlpha = (SkinRamp.PackedRgba(SkinRamps.Source.Steps[0]) & 0x00FFFFFFu) | 0x80000000u;

        uint[] buffer = [halfAlpha];

        SheetBaker.Substitute(buffer, substitution);

        Assert.Equal(halfAlpha, buffer[0]);
    }

    [Fact]
    public void Substitute_ReplacesEveryRampStepWithItsTarget()
    {
        var target = SkinRamps.All[5];
        var substitution = target.SubstitutionFrom(SkinRamps.Source);
        var buffer = new uint[SkinRamps.StepCount];

        for (var step = 0; step < SkinRamps.StepCount; step++)
        {
            buffer[step] = SkinRamp.PackedRgba(SkinRamps.Source.Steps[step]);
        }

        SheetBaker.Substitute(buffer, substitution);

        for (var step = 0; step < SkinRamps.StepCount; step++)
        {
            Assert.Equal(SkinRamp.PackedRgba(target.Steps[step]), buffer[step]);
        }
    }

    /// <summary>Sanity check that the vector width is what the loop thinks it is.</summary>
    [Fact]
    public void VectorWidth_IsAtLeastOnePixel() => Assert.True(Vector<uint>.Count >= 1);

    /// <summary>The pixel-facing entry point still returns a bitmap and still honours geometry.</summary>
    [Fact]
    public void Recolor_ReplacesRampColoursThroughTheBitmapApi()
    {
        var target = SkinRamps.All[3];

        using var source = new SKBitmap(new SKImageInfo(4, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var pixels = source.Pixels;

        pixels[0] = SkinRamps.Source.Steps[0];
        pixels[1] = SkinRamps.Source.Steps[3];
        pixels[2] = new SKColor(0x12, 0x34, 0x56, 0xFF);
        pixels[3] = SKColors.Transparent;
        source.Pixels = pixels;

        var result = SheetBaker.Recolor(source, target.SubstitutionFrom(SkinRamps.Source));

        Assert.True(result.IsSuccessful, $"recolor failed with {result.Error}");

        using var recolored = result.Value;

        Assert.Equal(target.Steps[0], recolored.GetPixel(0, 0));
        Assert.Equal(target.Steps[3], recolored.GetPixel(1, 0));
        Assert.Equal(new SKColor(0x12, 0x34, 0x56, 0xFF), recolored.GetPixel(2, 0));
        Assert.Equal(0, recolored.GetPixel(3, 0).Alpha);
    }
}
