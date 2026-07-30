using System.Collections.Immutable;
using SkiaSharp;

namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// finalbossblues' published procedure for matching Time Fantasy art to Elements, as a value.
/// <para>
/// The steps are his: replace the darkest colour with pure black, raise contrast, then lift the
/// Levels input black point. What is different here is <em>where</em> it runs —
/// <see cref="Derive"/> evaluates it over a palette rather than over pixels, because both packs
/// draw with a handful of colours and the result of a per-colour curve is therefore a lookup
/// table.
/// </para>
/// <para>
/// That matters beyond tidiness. A <see cref="RampSubstitution"/> is byte-exact and survives the
/// encoders' round-trip verification; a shader works in float and would not, which is the reason
/// <see cref="Baking.SheetBaker.Recolor"/> already rejects <c>SKRuntimeEffect</c>. Deriving the
/// table buys the tonal transform without giving up that guarantee.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <strong>The numbers are defaults, not constants, and they are not yet fitted.</strong> The
/// artist works in Photoshop and says plainly to trust your eyes on the sliders. Photoshop's
/// non-legacy Brightness/Contrast is a soft S-curve that avoids clipping; <see cref="Contrast"/>
/// below is the standard linear formula, which on the four Time Fantasy skin shades drove three of
/// them to 255. For skin that does not matter — <see cref="TimeFantasyRamps.SubstitutionTo"/> has
/// an exact target and never consults this. It matters for clothing, hair, weapons and tiles, which
/// have no target, and the curve wants fitting against one reference sheet exported from Photoshop
/// before it is trusted there.
/// </para>
/// </remarks>
public sealed record TimeFantasyTone
{
    /// <summary>
    /// The colour step one replaces with black. Time Fantasy's outline, which is also its shadow.
    /// </summary>
    public SKColor Outline { get; init; } = TimeFantasyRamps.Outline;

    /// <summary>Contrast on Photoshop's -100..100 scale. The artist's figure is 44.</summary>
    public int Contrast { get; init; } = 44;

    /// <summary>Levels input black point, 0..254. The artist's figure is 12.</summary>
    public int InputBlack { get; init; } = 12;

    /// <summary>
    /// The curve applied to one colour.
    /// </summary>
    /// <param name="color">A colour from the source palette.</param>
    /// <returns>
    /// <see cref="SKColors.Black"/> for <see cref="Outline"/>, otherwise the contrast and levels
    /// steps applied per channel. Alpha is carried through untouched.
    /// </returns>
    public SKColor Apply(SKColor color)
    {
        if (color.Red == Outline.Red && color.Green == Outline.Green && color.Blue == Outline.Blue)
        {
            return new SKColor(0, 0, 0, color.Alpha);
        }

        return new SKColor(Channel(color.Red), Channel(color.Green), Channel(color.Blue), color.Alpha);
    }

    /// <summary>
    /// The substitution this curve amounts to over <paramref name="palette"/>.
    /// </summary>
    /// <param name="palette">The distinct opaque colours the art is drawn with.</param>
    /// <returns>A table with one entry per colour, ready for the vectorised recolour.</returns>
    /// <remarks>
    /// The caller supplies the palette rather than a bitmap, so this stays a pure function of two
    /// values and the scan that finds those colours can be tested on its own.
    /// </remarks>
    public RampSubstitution Derive(ReadOnlySpan<SKColor> palette)
    {
        var from = ImmutableArray.CreateBuilder<uint>(palette.Length);
        var to = ImmutableArray.CreateBuilder<uint>(palette.Length);

        foreach (var color in palette)
        {
            from.Add(SkinRamp.PackedRgba(color));
            to.Add(SkinRamp.PackedRgba(Apply(color)));
        }

        return new()
        {
            From = from.ToImmutable(),
            To = to.ToImmutable(),
        };
    }

    /// <summary>Contrast then levels, on one channel.</summary>
    private byte Channel(byte value)
    {
        // The standard contrast formula. Photoshop's non-legacy curve is an S-shape rather than
        // this line — see the remarks on the type; swapping this out is the fitting knob.
        var factor = (259.0 * (Contrast + 255.0)) / (255.0 * (259.0 - Contrast));
        var contrasted = (factor * (value - 128.0)) + 128.0;

        var lifted = (contrasted - InputBlack) * 255.0 / (255.0 - InputBlack);

        return Clamp(lifted);
    }

    private static byte Clamp(double value) => value switch
    {
        <= 0 => 0,
        >= 255 => 255,
        _ => (byte)Math.Round(value, MidpointRounding.AwayFromZero),
    };
}
