using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Spritesheets;

/// <summary>
/// Geometry of the Time Fantasy diagonal sheet: 26x36 cells on a 6x4 grid, cardinals on the left
/// half and diagonals on the right.
/// <para>
/// The cardinal half was established by correlation rather than by eye — every cell in columns 0-2
/// is a pixel-exact silhouette match against the pack's own <c>$tf_template.png</c>, whose row
/// order is RPG Maker's down/left/right/up. Columns 3-5 match nothing in it, and are separately
/// drawn poses rather than mirrors.
/// </para>
/// </summary>
public sealed class TimeFantasyLayoutTests
{
    [Fact]
    public void SheetGeometry_IsSixCellsAcrossAndFourDown() =>
        Assert.Equal((156, 144), (TimeFantasyLayout.SheetWidth, TimeFantasyLayout.SheetHeight));

    /// <summary>
    /// The invariant that makes the direction table checkable rather than four loose labels: every
    /// diagonal is its own row's cardinal minus 45 degrees of compass bearing. A transcription slip
    /// breaks the rule here instead of shipping a character that strafes.
    /// </summary>
    [Fact]
    public void EveryDiagonal_IsItsRowsCardinalRotatedFortyFiveDegrees()
    {
        for (var row = 0; row < TimeFantasyLayout.Rows; row++)
        {
            var cardinal = TimeFantasyLayout.Facing(row, column: 0);
            var diagonal = TimeFantasyLayout.Facing(row, column: TimeFantasyLayout.DiagonalColumn);

            var expected = ((cardinal.Bearing - 45) + 360) % 360;

            Assert.Equal(expected, diagonal.Bearing);
        }
    }

    /// <summary>All eight compass directions, each exactly once.</summary>
    [Fact]
    public void Facings_CoverAllEightDirectionsWithoutRepeating()
    {
        Assert.Equal(8, TimeFantasyLayout.Facings.Length);
        Assert.Equal(8, TimeFantasyLayout.Facings.Select(static f => f.Bearing).Distinct().Count());
        Assert.All(TimeFantasyLayout.Facings, static f => Assert.True(f.Bearing % 45 is 0));
    }

    /// <summary>
    /// The left half is the four cardinals in the template's own row order, which is what the
    /// silhouette correlation established.
    /// </summary>
    [Theory]
    [InlineData(0, 180)]   // down
    [InlineData(1, 270)]   // left
    [InlineData(2, 90)]    // right
    [InlineData(3, 0)]     // up
    public void CardinalHalf_FollowsTheTemplateRowOrder(int row, int bearing) =>
        Assert.Equal(bearing, TimeFantasyLayout.Facing(row, column: 0).Bearing);

    /// <summary>
    /// RPG Maker's three-frame walk is a ping-pong with the stand pose in the middle, not a loop.
    /// Playing it 0-1-2 gives a limp; the pack's own frames/base naming (down_stand, down_walk1,
    /// down_walk2) is what settles it.
    /// </summary>
    [Fact]
    public void WalkCycle_PingPongsThroughTheStandFrame()
    {
        Assert.Equal([0, 1, 2, 1], TimeFantasyLayout.WalkCycle);
        Assert.Equal(1, TimeFantasyLayout.StandFrame);
    }
}
