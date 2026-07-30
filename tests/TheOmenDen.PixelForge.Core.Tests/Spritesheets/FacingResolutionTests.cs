using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Spritesheets;

/// <summary>
/// Serving eight-way movement from art that does not have eight facings.
/// <para>
/// Time Elements ships four — south, west, east, north — and no diagonals exist in any pack, core
/// or expansion. A three-quarter pose cannot be derived from the front and side views, so a
/// consumer moving at 315 degrees has to be told which row to play. Deciding that here rather than
/// in each engine is what stops Unity and MonoGame answering it differently.
/// </para>
/// </summary>
public sealed class FacingResolutionTests
{
    /// <summary>Time Elements' source rows, in order: south, west, east, north.</summary>
    private static int[] Elements => [180, 270, 90, 0];

    /// <summary>Every direction Time Fantasy's diagonal sheet carries.</summary>
    private static int[] Fantasy => [0, 45, 90, 135, 180, 225, 270, 315];

    [Fact]
    public void Resolve_ReturnsTheBearingItself_WhenTheArtHasIt()
    {
        foreach (var bearing in Fantasy)
        {
            Assert.Equal(bearing, FacingResolution.Resolve(bearing, Fantasy));
        }
    }

    /// <summary>
    /// The table this exists to produce. Each diagonal is equidistant from two cardinals, so the
    /// tie-break is the whole decision — and it goes to the horizontal one, because a side view
    /// reads as movement in top-down pixel art where a front or back view reads as standing.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]       // north  -> north
    [InlineData(45, 90)]     // NE     -> east
    [InlineData(90, 90)]     // east   -> east
    [InlineData(135, 90)]    // SE     -> east
    [InlineData(180, 180)]   // south  -> south
    [InlineData(225, 270)]   // SW     -> west
    [InlineData(270, 270)]   // west   -> west
    [InlineData(315, 270)]   // NW     -> west
    public void Resolve_SendsEveryDiagonalToTheHorizontalCardinal(int bearing, int expected) =>
        Assert.Equal(expected, FacingResolution.Resolve(bearing, Elements));

    /// <summary>
    /// Wrapping is real: 315 is 45 degrees from 0, not 315. Getting this wrong sends north-west to
    /// south, which is the kind of bug that looks like an animation glitch rather than arithmetic.
    /// </summary>
    [Theory]
    [InlineData(0, 315, 45)]
    [InlineData(315, 0, 45)]
    [InlineData(0, 180, 180)]
    [InlineData(90, 270, 180)]
    public void AngularDistance_TakesTheShortWayRound(int from, int to, int expected) =>
        Assert.Equal(expected, FacingResolution.AngularDistance(from, to));

    /// <summary>
    /// The curated Corvus geometry drops north entirely, so a consumer asking for it gets an answer
    /// rather than an exception — deterministically, since both candidates are equally horizontal.
    /// </summary>
    [Fact]
    public void Resolve_IsDeterministic_WhenEveryCandidateIsEquallyClose()
    {
        int[] withoutNorth = [180, 270, 90];

        Assert.Equal(90, FacingResolution.Resolve(0, withoutNorth));
        Assert.Equal(90, FacingResolution.Resolve(0, [90, 270]));
        Assert.Equal(90, FacingResolution.Resolve(0, [270, 90]));
    }

    /// <summary>Every one of the eight compass points resolves to something the art actually has.</summary>
    [Fact]
    public void Resolve_AlwaysLandsOnAnAvailableBearing()
    {
        foreach (var bearing in FacingResolution.Bearings)
        {
            Assert.Contains(FacingResolution.Resolve(bearing, Elements), Elements);
        }
    }

    /// <summary>
    /// The end-to-end answer a consumer needs: a heading in, a source row out. Rows are
    /// south 0, west 1, east 2, north 3.
    /// </summary>
    [Theory]
    [InlineData(0, 3)]      // north  -> north row
    [InlineData(45, 2)]     // NE     -> east row
    [InlineData(90, 2)]     // east   -> east row
    [InlineData(135, 2)]    // SE     -> east row
    [InlineData(180, 0)]    // south  -> south row
    [InlineData(225, 1)]    // SW     -> west row
    [InlineData(270, 1)]    // west   -> west row
    [InlineData(315, 1)]    // NW     -> west row
    public void RowForBearing_MapsEveryHeadingOntoASourceRow(int bearing, int row) =>
        Assert.Equal(row, SheetLayout.RowForBearing(bearing));

    /// <summary>
    /// The bearings match the row order the geometry documents, and the curated set is the source
    /// set minus north — which is the one facing Corvus deliberately drops.
    /// </summary>
    [Fact]
    public void SourceBearings_AreFourAndCuratedDropsNorth()
    {
        Assert.Equal(SheetLayout.SourceRows, SheetLayout.SourceBearings.Length);
        Assert.Equal(SheetLayout.FacingCount, SheetLayout.CuratedBearings.Length);
        Assert.DoesNotContain(0, SheetLayout.CuratedBearings);
    }

    /// <summary>
    /// Time Fantasy needs no approximation at all — it has every direction, so resolution is the
    /// identity. The same resolver serving both packs is the point.
    /// </summary>
    [Fact]
    public void Resolve_IsIdentity_ForAPackThatHasEveryDirection()
    {
        foreach (var facing in TimeFantasyLayout.Facings)
        {
            Assert.Equal(facing.Bearing, FacingResolution.Resolve(facing.Bearing, Fantasy));
        }
    }
}
