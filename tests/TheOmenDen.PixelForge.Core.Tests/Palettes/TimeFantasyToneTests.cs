using System.Collections.Immutable;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Palettes;

/// <summary>
/// finalbossblues' own "matching Time Fantasy to Elements" procedure, evaluated over a palette
/// instead of over pixels.
/// <para>
/// That substitution is the entire design. Both packs draw with five to eleven colours, so a tonal
/// curve collapses into a lookup table — which keeps the recolour a byte-exact
/// <see cref="RampSubstitution"/> and therefore keeps the encoders' round-trip verification
/// meaningful. A shader would work in float and destroy that, which is why
/// <see cref="SheetBaker.Recolor"/> already rejects <c>SKRuntimeEffect</c>.
/// </para>
/// </summary>
public sealed class TimeFantasyToneTests
{
    private static ImmutableArray<SKColor> Palette { get; } = [TimeFantasyRamps.Outline, .. TimeFantasyRamps.Skin];

    /// <summary>
    /// A bitmap drawn only in <see cref="Palette"/> plus transparency — which is exactly what real
    /// Time Fantasy art is. Colours outside the table would diverge between the two paths by
    /// design, since a substitution leaves them alone and a curve would not.
    /// </summary>
    private static SKBitmap PaletteBitmap()
    {
        var bitmap = new SKBitmap(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var pixels = bitmap.Pixels;

        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index] = index % 7 is 0
                ? SKColors.Transparent
                : Palette[index % Palette.Length];
        }

        bitmap.Pixels = pixels;
        return bitmap;
    }

    /// <summary>The curve applied one pixel at a time — the reference the table must match.</summary>
    private static SKBitmap ApplyPerPixel(SKBitmap source, TimeFantasyTone tone)
    {
        var target = source.Copy();
        var pixels = target.Pixels;

        for (var index = 0; index < pixels.Length; index++)
        {
            // Transparent pixels are untouched on both paths: the substitution compares whole
            // pixels with alpha forced opaque, so it can never match one.
            if (pixels[index].Alpha is not 0)
            {
                pixels[index] = tone.Apply(pixels[index]);
            }
        }

        target.Pixels = pixels;
        return target;
    }

    /// <summary>
    /// The load-bearing test. If these two ever disagree, deriving the table is not a valid stand-in
    /// for running the curve, and the byte-exactness the encoders verify would be resting on nothing.
    /// </summary>
    [Fact]
    public void Derive_ProducesASubstitutionEqualToApplyingTheCurvePerPixel()
    {
        var tone = new TimeFantasyTone();

        using var source = PaletteBitmap();

        var recolored = SheetBaker.Recolor(source, tone.Derive(Palette.AsSpan()));

        Assert.True(recolored.IsSuccessful, $"recolour failed with {recolored.Error}");

        using var viaTable = recolored.Value;
        using var viaCurve = ApplyPerPixel(source, tone);

        using var expected = viaCurve.PeekPixels();
        using var actual = viaTable.PeekPixels();

        Assert.Equal<byte[]>(expected.GetPixelSpan().ToArray(), actual.GetPixelSpan().ToArray());
    }

    /// <summary>Step one of the procedure, and the only part of it that is exact.</summary>
    [Fact]
    public void Apply_SendsTheOutlineToBlackWhateverTheCurve()
    {
        var tone = new TimeFantasyTone { Contrast = 0, InputBlack = 0 };

        Assert.Equal(SKColors.Black, tone.Apply(TimeFantasyRamps.Outline));
    }

    /// <summary>
    /// With the curve neutral, every colour but the outline is left exactly as authored. This is
    /// what makes the two knobs meaningful in isolation — a change in output is attributable.
    /// </summary>
    [Fact]
    public void Apply_WithANeutralCurve_ChangesNothingButTheOutline()
    {
        var tone = new TimeFantasyTone { Contrast = 0, InputBlack = 0 };

        foreach (var shade in TimeFantasyRamps.Skin)
        {
            Assert.Equal(shade, tone.Apply(shade));
        }
    }
}
