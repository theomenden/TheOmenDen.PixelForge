using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;

namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>
/// Geometry of the Time Fantasy diagonal character sheet.
/// <para>
/// 26x36 cells on a 6x4 grid: the four cardinals occupy columns 0-2 of each row and the four
/// diagonals columns 3-5. Unlike <see cref="SheetLayout"/> there is no curated counterpart — the
/// sheet is already the shape Unity and MonoGame want, so nothing is remapped and this type only
/// describes.
/// </para>
/// <para>
/// The cardinal half is not inferred from the art. Every cell in columns 0-2 is a pixel-exact
/// silhouette match against the pack's own <c>$tf_template.png</c> (0 mismatched pixels of 936),
/// whose rows are RPG Maker's down, left, right, up. Columns 3-5 match nothing in that template and
/// are separately drawn poses rather than mirrored ones.
/// </para>
/// </summary>
public static class TimeFantasyLayout
{
    public const int CellWidth = 26;
    public const int CellHeight = 36;

    public const int Columns = 6;
    public const int Rows = 4;

    public const int SheetWidth = Columns * CellWidth;
    public const int SheetHeight = Rows * CellHeight;

    /// <summary>The column each row's diagonal block starts at.</summary>
    public const int DiagonalColumn = 3;

    /// <summary>Frames per direction — RPG Maker's three-frame walk.</summary>
    public const int WalkFrames = 3;

    /// <summary>
    /// Which of the three frames is the standing pose.
    /// </summary>
    /// <remarks>
    /// The middle one, which is what makes <see cref="WalkCycle"/> a ping-pong. The pack's
    /// <c>frames/base/</c> folder names them <c>down_stand</c>, <c>down_walk1</c> and
    /// <c>down_walk2</c>, which is what settles it.
    /// </remarks>
    public const int StandFrame = 1;

    /// <summary>
    /// Frame order for one walk loop: out, centre, out, centre.
    /// </summary>
    /// <remarks>
    /// Playing 0-1-2 instead produces a limp, because the character would step onto the same foot
    /// twice per cycle. This belongs here rather than in each consumer.
    /// </remarks>
    public static ImmutableArray<int> WalkCycle { get; } = [0, StandFrame, 2, StandFrame];

    /// <summary>
    /// Every direction on the sheet, cardinals first within each row.
    /// </summary>
    /// <remarks>
    /// Bearings are compass degrees — north 0, east 90 — which is what lets the diagonal rule be
    /// asserted rather than trusted: each diagonal is its row's cardinal minus 45.
    /// </remarks>
    public static ImmutableArray<TimeFantasyFacing> Facings { get; } =
    [
        new() { Name = "south",      Bearing = 180, Row = 0, Column = 0 },
        new() { Name = "south_east", Bearing = 135, Row = 0, Column = DiagonalColumn },
        new() { Name = "west",       Bearing = 270, Row = 1, Column = 0 },
        new() { Name = "south_west", Bearing = 225, Row = 1, Column = DiagonalColumn },
        new() { Name = "east",       Bearing =  90, Row = 2, Column = 0 },
        new() { Name = "north_east", Bearing =  45, Row = 2, Column = DiagonalColumn },
        new() { Name = "north",      Bearing =   0, Row = 3, Column = 0 },
        new() { Name = "north_west", Bearing = 315, Row = 3, Column = DiagonalColumn },
    ];

    /// <summary>
    /// The direction whose frames start at <paramref name="row"/> and <paramref name="column"/>.
    /// </summary>
    /// <param name="row">Sheet row, 0 to <see cref="Rows"/> - 1.</param>
    /// <param name="column">Either 0 or <see cref="DiagonalColumn"/>.</param>
    /// <returns>The matching facing.</returns>
    public static TimeFantasyFacing Facing(int row, int column)
    {
        foreach (var facing in Facings)
        {
            if (facing.Row == row && facing.Column == column)
            {
                return facing;
            }
        }

        return ThrowHelper.ThrowArgumentOutOfRangeException<TimeFantasyFacing>(nameof(row));
    }
}
