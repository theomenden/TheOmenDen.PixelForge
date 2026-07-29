using System.Collections.Frozen;
using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using SkiaSharp;

namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// A five-step skin ramp, darkest shadow to lightest highlight.
/// <para>
/// Every shipped Time Elements partial is authored in <see cref="SkinRamps.Source"/>.
/// A recolour is a straight index-for-index substitution of those five colours — the pack's
/// own guide proves it, rendering all four human tones from one sprite with identical pixel
/// counts per step. No blending, no dithering, no colour-space maths.
/// </para>
/// </summary>
public sealed record SkinRamp
{
    public required string Name { get; init; }

    /// <summary>Exactly <see cref="SkinRamps.StepCount"/> colours, darkest first.</summary>
    public required ImmutableArray<SKColor> Steps { get; init; }

    /// <summary>
    /// Whether this reads as human skin. The three fantasy tones are reachable only by an
    /// explicit choice, never by the hashed default, so an un-customized flock stays human.
    /// </summary>
    public required bool IsHuman { get; init; }

    /// <summary>The mid tone — what a viewer thinks of as "the" colour of this ramp.</summary>
    public SKColor BaseTone => Steps[3];

    /// <summary>
    /// Substitution table taking <paramref name="source"/>'s colours to this ramp's, keyed on
    /// packed RGB. Identity when this ramp is the source.
    /// <para>
    /// Superseded by <see cref="SubstitutionFrom(SkinRamp)"/>, which returns the packed-pixel
    /// <see cref="RampSubstitution"/> the vectorised recolour consumes. This member exists only
    /// while the dictionary-driven <see cref="Baking.SheetBaker.Recolor"/> is still in place and
    /// is deleted with it — do not add callers.
    /// </para>
    /// </summary>
    public FrozenDictionary<uint, SKColor> LegacySubstitutionFrom(SkinRamp source)
    {
        Guard.IsNotNull(source);
        Guard.IsEqualTo(source.Steps.Length, SkinRamps.StepCount);
        Guard.IsEqualTo(Steps.Length, SkinRamps.StepCount);

        var map = new Dictionary<uint, SKColor>(SkinRamps.StepCount);

        for (var i = 0; i < SkinRamps.StepCount; i++)
        {
            map[Pack(source.Steps[i])] = Steps[i];
        }

        return map.ToFrozenDictionary();
    }

    /// <summary>
    /// Substitution table taking <paramref name="source"/>'s colours to this ramp's, as packed
    /// pixels ready for the vectorised recolour. Identity when this ramp <em>is</em>
    /// <paramref name="source"/>.
    /// </summary>
    public RampSubstitution SubstitutionFrom(SkinRamp source)
    {
        Guard.IsNotNull(source);
        Guard.IsEqualTo(source.Steps.Length, SkinRamps.StepCount);
        Guard.IsEqualTo(Steps.Length, SkinRamps.StepCount);

        var from = ImmutableArray.CreateBuilder<uint>(SkinRamps.StepCount);
        var to = ImmutableArray.CreateBuilder<uint>(SkinRamps.StepCount);

        for (var step = 0; step < SkinRamps.StepCount; step++)
        {
            from.Add(PackedRgba(source.Steps[step]));
            to.Add(PackedRgba(Steps[step]));
        }

        return new()
        {
            From = from.ToImmutable(),
            To = to.ToImmutable(),
        };
    }

    /// <summary>
    /// Packs a colour's RGB into a lookup key. Alpha is deliberately ignored — this is a
    /// dictionary key, not a colour conversion.
    /// </summary>
    public static uint Pack(SKColor color) => ((uint)color.Red << 16) | ((uint)color.Green << 8) | color.Blue;

    /// <summary>
    /// Packs a colour into a whole RGBA8888 pixel with alpha forced opaque.
    /// <para>
    /// The byte order is the trap. RGBA8888 lays out R,G,B,A in ascending addresses, so reading
    /// that memory as a little-endian <see cref="uint"/> yields <c>0xAABBGGRR</c> — red in the
    /// <em>low</em> byte. <see cref="Pack"/>'s dictionary key is the opposite, <c>0xRRGGBB</c>.
    /// Confusing the two swaps red and blue in every baked sheet, and the round-trip verification
    /// would not catch it because it compares an encode against its own decode.
    /// </para>
    /// <para>
    /// Alpha is forced to <c>0xFF</c> rather than taken from <paramref name="color"/> because these
    /// values are compared against opaque pixels only; see <see cref="RampSubstitution"/>.
    /// </para>
    /// </summary>
    public static uint PackedRgba(SKColor color) =>
        0xFF000000u | ((uint)color.Blue << 16) | ((uint)color.Green << 8) | color.Red;
}
