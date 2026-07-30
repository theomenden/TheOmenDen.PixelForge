using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Spritesheets;

/// <summary>
/// A full-geometry sheet is 23x4 cells with nothing in-band saying which column is the bow draw.
/// This manifest is the difference between an atlas a consumer can load and one it has to be told
/// about out of band.
/// </summary>
public sealed class ClipIndexTests
{
    [Fact]
    public void Rows_CoverEveryClipOnEveryFacing()
    {
        var expected = 0;

        foreach (var clip in GeneratorClips.All)
        {
            expected += clip.FrameCount * GeneratorClips.Facings.Length;
        }

        Assert.Equal(expected, ClipIndex.Rows.Length);
    }

    [Fact]
    public void Rows_CarryThePlaybackOrderNotAscendingColumns()
    {
        var walkSouth = ClipIndex.Rows
            .AsSpan()
            .Where(static row => string.Equals(row.Clip, "walk", StringComparison.Ordinal)
                && string.Equals(row.Facing, "south", StringComparison.Ordinal))
            .OrderBy(static row => row.FrameIndex)
            .ToArray();

        Assert.Equal([1, 2, 1, 0], walkSouth.Select(static row => row.SourceColumn).ToArray());
    }

    [Fact]
    public void Rows_MapFacingsOntoSourceRowsInOrder()
    {
        foreach (var row in ClipIndex.Rows)
        {
            Assert.Equal(GeneratorClips.Facings.IndexOf(row.Facing), row.SourceRow);
        }
    }

    [Fact]
    public void Rows_CarryTheAuthoredFrameDuration()
        => Assert.All(ClipIndex.Rows, row => Assert.Equal(GeneratorClips.FrameDurationMilliseconds, row.FrameDurationMs));

    [Fact]
    public async Task WriteTo_WritesTheManifestAndReportsTheRowCount()
    {
        using var root = TemporaryDirectory.Create();

        var written = await ClipIndex.WriteToAsync(root.FullPath, TestContext.Current.CancellationToken);

        Assert.True(written.IsSuccessful, $"write failed with {written.Error}");
        Assert.Equal(ClipIndex.Rows.Length, written.Value);
        Assert.True(File.Exists((root.FullPath / ClipIndex.FileName).Value));
    }

    [Fact]
    public async Task WriteTo_ReportsOutputDirectoryUnavailable_WhenTheFolderIsAbsent()
    {
        using var root = TemporaryDirectory.Create();

        var result = await ClipIndex.WriteToAsync(root.FullPath / "absent", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.OutputDirectoryUnavailable, result.Error);
    }
}
