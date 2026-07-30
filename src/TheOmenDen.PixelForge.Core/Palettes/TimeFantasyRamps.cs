using System.Collections.Immutable;
using ColorHelper;
using CommunityToolkit.Diagnostics;
using SkiaSharp;

namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// Time Fantasy's character palette, and the substitution that puts it into a Time Elements
/// skin ramp.
/// <para>
/// The two packs are the same artist and share no colour. <c>#BB6749</c> against
/// <c>#BB7547</c> is the closest pair and still differs in three channels, so
/// <see cref="RampSubstitution"/> — which compares whole pixels exactly — recolours nothing at all
/// when a Time Elements ramp is pointed at Time Fantasy art. That failure is silent, which is why
/// this table exists rather than a tolerance.
/// </para>
/// </summary>
public static class TimeFantasyRamps
{
    /// <summary>How many skin shades Time Fantasy draws with. One fewer than Time Elements.</summary>
    public const int StepCount = 4;

    private static SKColor FromHex(string hex)
    {
        var rgb = ColorConverter.HexToRgb(new HEX(hex));

        return new SKColor(rgb.R, rgb.G, rgb.B);
    }

    /// <summary>
    /// The outline colour, which is also the drop shadow.
    /// </summary>
    /// <remarks>
    /// Verified by mapping every pixel of a cell: it traces the silhouette from the top of the head
    /// down, then fills solid for the shadow ellipse under the feet. Both roles want the same
    /// answer here, because Time Elements draws opaque <c>#000000</c> outlines and its
    /// <c>shadow.png</c> is nothing but opaque <c>#000000</c>.
    /// </remarks>
    public static SKColor Outline { get; } = FromHex("#354048");

    /// <summary>
    /// The four skin shades, darkest first — the counterpart to <see cref="SkinRamp.Steps"/>.
    /// </summary>
    /// <remarks>
    /// Four rather than five because a 26x36 sprite has fewer pixels to shade with than a 48x48
    /// one. Which Time Elements step goes unused is <see cref="TargetSteps"/>'s business.
    /// </remarks>
    public static ImmutableArray<SKColor> Skin { get; } =
    [
        FromHex("#6C3C4A"),
        FromHex("#BB6749"),
        FromHex("#DEBC70"),
        FromHex("#F2F0C5"),
    ];

    /// <summary>
    /// Which <see cref="SkinRamp.Steps"/> index each of <see cref="Skin"/>'s shades maps onto.
    /// </summary>
    /// <remarks>
    /// Step 3 (<c>#F4D29C</c>) is the one dropped. Chosen from a rendered comparison of all three
    /// candidates across Default Tone, Tone 3 and Tone 6: collapsing a middle step preserves both
    /// endpoints, where dropping the brightest highlight visibly flattened Tone 3 into a single
    /// muddy brown and dropping <c>#DBA463</c> cost the shadow its definition.
    /// </remarks>
    public static ImmutableArray<int> TargetSteps { get; } = [0, 1, 2, 4];

    /// <summary>
    /// The substitution taking Time Fantasy's palette into <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The Time Elements skin ramp to land in. Never <see langword="null"/>.</param>
    /// <returns>
    /// A five-entry table: the outline to black, then each skin shade to its
    /// <see cref="TargetSteps"/> counterpart.
    /// </returns>
    /// <remarks>
    /// Built as a <see cref="RampSubstitution"/> like every other recolour here, so the vectorised
    /// loop, the binary-alpha assumption it rests on, and the encoders' byte-exact round trip all
    /// apply unchanged. Nothing about this path is special beyond the table it fills in.
    /// </remarks>
    public static RampSubstitution SubstitutionTo(SkinRamp target)
    {
        Guard.IsNotNull(target);
        Guard.IsEqualTo(target.Steps.Length, SkinRamps.StepCount);

        var from = ImmutableArray.CreateBuilder<uint>(StepCount + 1);
        var to = ImmutableArray.CreateBuilder<uint>(StepCount + 1);

        from.Add(SkinRamp.PackedRgba(Outline));
        to.Add(SkinRamp.PackedRgba(SKColors.Black));

        for (var step = 0; step < StepCount; step++)
        {
            from.Add(SkinRamp.PackedRgba(Skin[step]));
            to.Add(SkinRamp.PackedRgba(target.Steps[TargetSteps[step]]));
        }

        return new()
        {
            From = from.ToImmutable(),
            To = to.ToImmutable(),
        };
    }
}
