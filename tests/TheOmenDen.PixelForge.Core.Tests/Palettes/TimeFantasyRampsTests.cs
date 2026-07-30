using SkiaSharp;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Palettes;

/// <summary>
/// The substitution that puts Time Fantasy art into a Time Elements skin ramp.
/// <para>
/// The two packs share no colour — <c>#BB6749</c> against <c>#BB7547</c> is the closest pair and
/// still differs in three channels — so without this table the existing exact-match recolour
/// touches nothing at all, silently.
/// </para>
/// </summary>
public sealed class TimeFantasyRampsTests
{
    /// <summary>
    /// <c>#354048</c> is Time Fantasy's outline <em>and</em> its shadow: it traces the whole
    /// silhouette and then fills solid under the feet. Time Elements draws opaque black outlines
    /// and ships a shadow slot that is nothing but opaque black, so one entry is correct for both
    /// roles at once.
    /// </summary>
    [Fact]
    public void SubstitutionTo_SendsTheOutlineToBlack()
    {
        var substitution = TimeFantasyRamps.SubstitutionTo(SkinRamps.Source);

        var index = substitution.From.IndexOf(SkinRamp.PackedRgba(TimeFantasyRamps.Outline));

        Assert.True(index >= 0, "the outline colour must appear in the table");
        Assert.Equal(SkinRamp.PackedRgba(SKColors.Black), substitution.To[index]);
    }

    /// <summary>
    /// Four source shades onto five target steps, skipping <c>#F4D29C</c>. Collapsing a middle step
    /// keeps both endpoints, which a rendered comparison showed mattered most: dropping the
    /// brightest highlight instead flattened Tone 3 into one muddy brown.
    /// </summary>
    [Fact]
    public void SubstitutionTo_MapsEverySkinShadeOntoItsTargetStep()
    {
        // Tone 4 is green — it shares no channel with the source art, so a shade that failed to be
        // remapped would be obvious rather than coincidentally close.
        var target = SkinRamps.All[4];

        var substitution = TimeFantasyRamps.SubstitutionTo(target);

        for (var step = 0; step < TimeFantasyRamps.StepCount; step++)
        {
            var index = substitution.From.IndexOf(SkinRamp.PackedRgba(TimeFantasyRamps.Skin[step]));

            Assert.True(index >= 0, $"skin shade {step} must appear in the table");
            Assert.Equal(
                SkinRamp.PackedRgba(target.Steps[TimeFantasyRamps.TargetSteps[step]]),
                substitution.To[index]);
        }
    }

    /// <summary>The dropped step is genuinely dropped, not quietly aliased onto a neighbour.</summary>
    [Fact]
    public void SubstitutionTo_NeverEmitsTheDroppedTargetStep()
    {
        var target = SkinRamps.All[4];

        var substitution = TimeFantasyRamps.SubstitutionTo(target);

        Assert.DoesNotContain(3, TimeFantasyRamps.TargetSteps);
        Assert.DoesNotContain(SkinRamp.PackedRgba(target.Steps[3]), substitution.To);
    }

    /// <summary>
    /// Never identity, even against the ramp Time Elements art is authored in. The two packs share
    /// no colour, so a table that came back identity would mean the recolour had been handed the
    /// wrong source palette and would silently do nothing.
    /// </summary>
    [Fact]
    public void SubstitutionTo_IsNeverIdentity()
    {
        foreach (var ramp in SkinRamps.All)
        {
            Assert.False(TimeFantasyRamps.SubstitutionTo(ramp).IsIdentity, ramp.Name);
        }
    }
}
