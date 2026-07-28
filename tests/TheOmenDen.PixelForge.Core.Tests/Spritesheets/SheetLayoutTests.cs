using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Spritesheets;

/// <summary>
/// Pins the curated layout to the contract Corvus consumes. Nothing here is inferred from the
/// art — the column spans come from the Elements generator's own Settings.json, and the row
/// assignments come from spec 079's remap table.
/// </summary>
public sealed class SheetLayoutTests
{
    [Fact]
    public void OutputGeometry_MatchesTheCorvusContract()
    {
        Assert.Equal(240, SheetLayout.OutputWidth);
        Assert.Equal(1152, SheetLayout.OutputHeight);
        Assert.Equal(5, SheetLayout.OutputColumns);
        Assert.Equal(24, SheetLayout.OutputRows);
    }

    [Fact]
    public void SourceGeometry_MatchesTheElementsPartials()
    {
        Assert.Equal(1104, SheetLayout.SourceWidth);
        Assert.Equal(192, SheetLayout.SourceHeight);
        Assert.Equal(23, SheetLayout.SourceColumns);
        Assert.Equal(4, SheetLayout.SourceRows);
    }

    [Fact]
    public void Clips_AreTheEightKeptAnimations_InOutputOrder()
    {
        var names = SheetLayout.Clips.AsSpan().Select(static c => c.Name).ToArray();

        Assert.Equal(
            ["walk", "idle", "arms_up", "crouch", "jump", "attack", "heavy_attack", "sleep_ko"],
            names);
    }

    /// <summary>
    /// heavy_attack is the widest clip at 5 frames. That is what sets the 240px width, so if a
    /// clip ever grew past it the sheet would silently truncate frames.
    /// </summary>
    [Fact]
    public void WidestClip_SetsTheColumnCount()
    {
        var widest = SheetLayout.Clips.AsSpan().Max(static c => c.FrameCount);

        Assert.Equal(SheetLayout.OutputColumns, widest);
    }

    [Fact]
    public void NoClip_ReadsPastTheSourceSheet()
    {
        foreach (var clip in SheetLayout.Clips)
        {
            Assert.InRange(clip.SourceColumn, 0, SheetLayout.SourceColumns - 1);
            Assert.InRange(clip.SourceColumnEnd, 1, SheetLayout.SourceColumns);
        }
    }

    [Theory]
    [InlineData("walk", 0, 3)]
    [InlineData("idle", 3, 1)]
    [InlineData("arms_up", 6, 3)]
    [InlineData("crouch", 9, 1)]
    [InlineData("jump", 12, 4)]
    [InlineData("attack", 15, 4)]
    [InlineData("heavy_attack", 18, 5)]
    [InlineData("sleep_ko", 21, 1)]
    public void RowFor_PlacesEachClipOnItsContractRow(string name, int expectedFirstRow, int expectedFrames)
    {
        var index = SheetLayout.Clips.AsSpan().Select(static c => c.Name).ToArray().AsSpan().IndexOf(name);

        Assert.True(index >= 0, $"clip '{name}' is missing");
        Assert.Equal(expectedFirstRow, SheetLayout.RowFor(index, 0));
        Assert.Equal(expectedFrames, SheetLayout.Clips[index].FrameCount);
    }

    /// <summary>AC-14: every row is claimed, none twice, nothing points past row 23.</summary>
    [Fact]
    public void EveryOutputRow_IsClaimedExactlyOnce()
    {
        var claimed = new bool[SheetLayout.OutputRows];

        for (var clipIndex = 0; clipIndex < SheetLayout.Clips.Length; clipIndex++)
        {
            for (var facing = 0; facing < SheetLayout.FacingCount; facing++)
            {
                var row = SheetLayout.RowFor(clipIndex, facing);

                Assert.InRange(row, 0, SheetLayout.OutputRows - 1);
                Assert.False(claimed[row], $"row {row} claimed twice");
                claimed[row] = true;
            }
        }

        Assert.DoesNotContain(false, claimed);
    }
}
