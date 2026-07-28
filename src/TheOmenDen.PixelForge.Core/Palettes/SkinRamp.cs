using System.Collections.Frozen;
using System.Collections.Immutable;
using ColorHelper;
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

/// <summary>
/// The seven palette swaps the Elements generator ships, verbatim from its
/// <c>Settings.json</c> <c>PaletteSwaps</c> block.
/// <para>
/// Declared as hex strings so they diff directly against the pack's own guide, which prints
/// these same four human ramps beside the heads they produce. Parsing is
/// <see cref="ColorConverter.HexToRgb"/> rather than hand-rolled bit shifting.
/// </para>
/// </summary>
public static class SkinRamps
{
    public const int StepCount = 5;

    private static SKColor FromHex(string hex)
    {
        var rgb = ColorConverter.HexToRgb(new HEX(hex));

        return new SKColor(rgb.R, rgb.G, rgb.B);
    }

    private static ImmutableArray<SKColor> Ramp(params ReadOnlySpan<string> hexes)
    {
        var steps = ImmutableArray.CreateBuilder<SKColor>(hexes.Length);

        foreach (var hex in hexes)
        {
            steps.Add(FromHex(hex));
        }

        return steps.ToImmutable();
    }

    /// <summary>"Default Tone" — the ramp every shipped partial is already authored in.</summary>
    public static SkinRamp Source { get; } = new()
    {
        Name = "Default Tone",
        IsHuman = true,
        Steps = Ramp("#73172D", "#BB7547", "#DBA463", "#F4D29C", "#FAF4D6"),
    };

    /// <summary>All seven tones, in <c>body-01..07</c> order.</summary>
    public static ImmutableArray<SkinRamp> All { get; } =
    [
        Source,
        new()
        {
            Name = "Tone 1",
            IsHuman = true,
            Steps = Ramp("#561F2D", "#9D5534", "#B4723C", "#D49149", "#F0C175"),
        },
        new()
        {
            Name = "Tone 2",
            IsHuman = true,
            Steps = Ramp("#481C0E", "#774128", "#955123", "#B97E50", "#DBAA76"),
        },
        new()
        {
            Name = "Tone 3",
            IsHuman = true,
            Steps = Ramp("#36150C", "#583322", "#7B4C2D", "#986743", "#C78E52"),
        },
        new()
        {
            Name = "Tone 4 (Green)",
            IsHuman = false,
            Steps = Ramp("#184E3A", "#3D8A3E", "#6CB328", "#AFE356", "#E4FCA2"),
        },
        new()
        {
            Name = "Tone 5 (Red)",
            IsHuman = false,
            Steps = Ramp("#4E2218", "#8F2416", "#C74934", "#E96564", "#FCA2AB"),
        },
        new()
        {
            Name = "Tone 6 (Bone)",
            IsHuman = false,
            Steps = Ramp("#51393F", "#89786C", "#B6ACAA", "#E6E1E1", "#F6FFFF"),
        },
    ];

    /// <summary>The pool the hashed default draws from — fantasy tones stay opt-in.</summary>
    /// <remarks>
    /// <c>AsSpan()</c> is load-bearing: <see cref="ImmutableArray{T}"/> is not one of the types
    /// the ZLinq drop-in generator covers, so a bare <c>.Where(...)</c> would bind to
    /// System.Linq. A span is covered.
    /// </remarks>
    public static ImmutableArray<SkinRamp> Human { get; } = [.. All.AsSpan().Where(static r => r.IsHuman)];
}
