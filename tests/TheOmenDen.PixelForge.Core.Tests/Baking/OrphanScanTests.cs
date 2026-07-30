using System.Collections.Immutable;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The scan that surfaces sheets a shrinking run leaves behind, instead of letting them go
/// unindexed the way the original bug did.
/// </summary>
public sealed class OrphanScanTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    private FullPath Root => _directory.FullPath;

    private static SheetRecipe Recipe(string name, string directory) => new()
    {
        Name = name,
        Directory = directory,
        Layers = [],
    };

    /// <summary>Puts a sheet on disk without baking one.</summary>
    private void Place(string relative)
    {
        var path = Root / relative;

        Directory.CreateDirectory(Path.GetDirectoryName(path.Value)!);
        File.WriteAllText(path.Value, "not really a webp");
    }

    [Fact]
    public void Find_IsEmpty_WhenEveryFileWasWrittenByTheRun()
    {
        Place("heroes/villager_01/villager_01.webp");
        Place("attachments/hair/hair1.webp");

        ImmutableArray<SheetRecipe> recipes =
        [
            Recipe("villager_01", "heroes/villager_01"),
            Recipe("hair1", "attachments/hair"),
        ];

        Assert.Empty(OrphanScan.Find(Root, recipes));
    }

    /// <summary>The case that motivates the scan: a later run ticks fewer hats.</summary>
    [Fact]
    public void Find_ReportsSheetsANarrowerRunNoLongerWrites()
    {
        Place("attachments/hat/hat1.webp");
        Place("attachments/hat/hat2.webp");
        Place("attachments/hat/hat5.webp");

        var orphans = OrphanScan.Find(Root, [Recipe("hat1", "attachments/hat")]);

        Assert.Equal(["attachments/hat/hat2.webp", "attachments/hat/hat5.webp"], orphans);
    }

    /// <summary>A hero directory a later run stops producing is stale in full.</summary>
    [Fact]
    public void Find_ReachesIntoEveryHeroDirectory()
    {
        Place("heroes/villager_01/villager_01.webp");
        Place("heroes/villager_02/villager_02.webp");

        var orphans = OrphanScan.Find(Root, [Recipe("villager_01", "heroes/villager_01")]);

        Assert.Equal(["heroes/villager_02/villager_02.webp"], orphans);
    }

    /// <summary>
    /// curated/ belongs to a different command with its own manifests, so a batch export must not
    /// report the whole deliverable as orphaned.
    /// </summary>
    [Fact]
    public void Find_IgnoresTheCuratedDeliverable()
    {
        Place("curated/body-01.webp");
        Place("curated/hair-01.webp");
        Place("attachments/hair/hair1.webp");

        Assert.Empty(OrphanScan.Find(Root, [Recipe("hair1", "attachments/hair")]));
    }

    /// <summary>Manifests and registries are not sheets.</summary>
    [Fact]
    public void Find_IgnoresFilesThatAreNotSheets()
    {
        Place("attachments/hair/hair1.webp");
        Place("attachments/hair/notes.txt");

        Assert.Empty(OrphanScan.Find(Root, [Recipe("hair1", "attachments/hair")]));
    }

    /// <summary>An untouched folder has nothing to report.</summary>
    [Fact]
    public void Find_IsEmpty_WhenNothingHasBeenWrittenYet() =>
        Assert.Empty(OrphanScan.Find(Root, [Recipe("hair1", "attachments/hair")]));

    /// <summary>
    /// The reported paths are the manifests' own vocabulary, so a user can find the row a stale
    /// file used to occupy.
    /// </summary>
    [Fact]
    public void Find_ReportsForwardSlashPathsRelativeToTheRoot()
    {
        Place("heroes/villager_09/villager_09.webp");

        var orphan = Assert.Single(OrphanScan.Find(Root, []));

        Assert.Equal("heroes/villager_09/villager_09.webp", orphan);
        Assert.DoesNotContain('\\', orphan);
    }
}
