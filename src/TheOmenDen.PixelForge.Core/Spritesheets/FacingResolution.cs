using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;

namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>
/// Which facing to play for a heading the art does not have.
/// <para>
/// Time Elements ships four facings — south, west, east, north — and no diagonal frames exist in
/// the core pack, either expansion, or the <c>tdsm</c> variant. Nor can they be synthesised: a
/// three-quarter pose is different art, not a transform of the front and side views, and shearing
/// 48px pixel art produces mush. Time Fantasy's diagonal sheet cannot lend them either, being a
/// different character at 26x36.
/// </para>
/// <para>
/// So eight-way movement against four-way art means resolving a heading to the nearest facing that
/// exists. Deciding that once, here, is what stops Unity and MonoGame each inventing an answer —
/// and it costs no art, which the alternative very much does.
/// </para>
/// </summary>
public static class FacingResolution
{
    /// <summary>Degrees between adjacent compass points on an eight-way rose.</summary>
    public const int Step = 45;

    /// <summary>A full turn, in degrees.</summary>
    private const int Turn = 360;

    /// <summary>The eight headings a consumer can ask about, clockwise from north.</summary>
    public static ImmutableArray<int> Bearings { get; } = [0, 45, 90, 135, 180, 225, 270, 315];

    /// <summary>
    /// The shorter of the two ways round between two bearings.
    /// </summary>
    /// <param name="from">A bearing in degrees.</param>
    /// <param name="to">A bearing in degrees.</param>
    /// <returns>The separation, 0 to 180.</returns>
    /// <remarks>
    /// Wrapping is the trap: 315 is 45 degrees from 0, not 315. Getting it wrong sends north-west
    /// to south, which reads as an animation glitch rather than as arithmetic.
    /// </remarks>
    public static int AngularDistance(int from, int to)
    {
        var separation = Math.Abs(Normalize(from) - Normalize(to));

        return Math.Min(separation, Turn - separation);
    }

    /// <summary>
    /// The available bearing to play for <paramref name="bearing"/>.
    /// </summary>
    /// <param name="bearing">The heading a consumer wants.</param>
    /// <param name="available">The bearings the art actually has. Must not be empty.</param>
    /// <returns>The nearest available bearing, resolving ties toward the horizontal.</returns>
    /// <remarks>
    /// <para>
    /// Every exact diagonal is equidistant from two cardinals, so the tie-break <em>is</em> the
    /// decision. It goes to the more horizontal candidate: in top-down pixel art a side view reads
    /// as movement where a front or back view reads as standing still, so a character moving
    /// north-east looks better walking east than walking north.
    /// </para>
    /// <para>
    /// A remaining tie — north against a set holding only east and west, which is the curated
    /// geometry, since it drops north — breaks to the lower bearing. Arbitrary, but deterministic,
    /// which is the property that matters when two engines read the same table.
    /// </para>
    /// </remarks>
    public static int Resolve(int bearing, ReadOnlySpan<int> available)
    {
        Guard.IsNotEmpty(available);

        var wanted = Normalize(bearing);
        var best = Normalize(available[0]);

        foreach (var candidate in available)
        {
            if (Prefers(Normalize(candidate), best, wanted))
            {
                best = Normalize(candidate);
            }
        }

        return best;
    }

    /// <summary>Whether <paramref name="candidate"/> beats <paramref name="best"/> for a heading.</summary>
    private static bool Prefers(int candidate, int best, int wanted)
    {
        var byDistance = AngularDistance(candidate, wanted).CompareTo(AngularDistance(best, wanted));

        if (byDistance is not 0)
        {
            return byDistance < 0;
        }

        var byHorizontality = Horizontality(candidate).CompareTo(Horizontality(best));

        return byHorizontality is not 0 ? byHorizontality < 0 : candidate < best;
    }

    /// <summary>
    /// How far a bearing is from due east or due west, in degrees — 0 for horizontal, 90 for
    /// vertical. Smaller is more horizontal.
    /// </summary>
    /// <remarks>
    /// Integer arithmetic rather than <see cref="Math.Sin(double)"/>: the comparison must be exact,
    /// and every bearing here is a multiple of <see cref="Step"/>.
    /// </remarks>
    private static int Horizontality(int bearing) => Math.Abs(90 - (Normalize(bearing) % 180));

    /// <summary>Brings any bearing into 0..359, negatives included.</summary>
    private static int Normalize(int bearing) => ((bearing % Turn) + Turn) % Turn;
}
