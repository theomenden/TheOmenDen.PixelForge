using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Spritesheets;

public sealed class SheetIndexTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Rows_DescribeEveryClipOnEveryFacing() =>
        Assert.Equal(SheetLayout.ClipCount * SheetLayout.FacingCount, SheetIndex.Rows.Length);

    /// <summary>
    /// The manifest must agree with the remap the baker actually performs, or it is worse than
    /// no manifest at all.
    /// </summary>
    [Fact]
    public void Rows_MatchTheLayoutRowMap()
    {
        for (var clipIndex = 0; clipIndex < SheetLayout.Clips.Length; clipIndex++)
        {
            var clip = SheetLayout.Clips[clipIndex];

            for (var facing = 0; facing < SheetLayout.FacingCount; facing++)
            {
                var expectedRow = SheetLayout.RowFor(clipIndex, facing);

                var row = SheetIndex.Rows.AsSpan()
                    .First(r => r.Clip == clip.Name && r.Facing == SheetIndex.Facings[facing]);

                Assert.Equal(expectedRow, row.Row);
                Assert.Equal(clip.FrameCount, row.FrameCount);
                Assert.Equal(clip.SourceColumn, row.FirstColumn);
                Assert.Equal(SheetLayout.CellSize, row.CellSize);
            }
        }
    }

    [Fact]
    public void Facings_AreSouthWestEast_AndNeverNorth()
    {
        Assert.Equal<string[]>(["south", "west", "east"], [.. SheetIndex.Facings]);
        Assert.DoesNotContain("north", SheetIndex.Facings);
    }

    [Fact]
    public void Write_EmitsAHeaderAndOneLinePerRow()
    {
        using var writer = new StringWriter();

        var count = SheetIndex.Write(writer);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(SheetIndex.Rows.Length, count);
        Assert.Equal(SheetIndex.Rows.Length + 1, lines.Length);
        Assert.StartsWith("Clip,Facing,Row,FrameCount,FirstColumn,CellSize", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void WriteTo_PutsIndexCsvBesideTheSheets()
    {
        var result = SheetIndex.WriteTo(_directory.FullPath);

        Assert.True(result.IsSuccessful, $"write failed with {result.Error}");
        Assert.True(File.Exists((_directory.FullPath / "index.csv").Value));
    }

    [Fact]
    public void WriteTo_ReportsOutputDirectoryUnavailable_WhenTheDirectoryIsMissing()
    {
        var result = SheetIndex.WriteTo(_directory.FullPath / "nope");

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.OutputDirectoryUnavailable, result.Error);
    }
}
