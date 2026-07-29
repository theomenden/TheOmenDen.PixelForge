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
    /// </summary>
    public FrozenDictionary<uint, SKColor> SubstitutionFrom(SkinRamp source)
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
    /// Packs a colour's RGB into a lookup key. Alpha is deliberately ignored — this is a
    /// dictionary key, not a colour conversion.
    /// </summary>
    public static uint Pack(SKColor color) => ((uint)color.Red << 16) | ((uint)color.Green << 8) | color.Blue;
}
