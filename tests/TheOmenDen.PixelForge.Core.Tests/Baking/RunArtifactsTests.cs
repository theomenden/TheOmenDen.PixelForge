using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The rule deciding <em>which</em> manifests a run writes, and the run id they share.
/// </summary>
/// <remarks>
/// Every writer beneath this is covered, but the choice between them used to live in a Windows-only
/// view model that this project cannot reference — so it was the one part of the export with no
/// tests at all. That is why it moved into <see cref="RunArtifacts"/>.
/// </remarks>
public sealed class RunArtifactsTests
{
    private static FullPath Partial(AssetSlot slot, string stem) =>
        FullPath.FromPath(Path.Combine(Path.GetTempPath(), "pack", AssetSlots.FolderName(slot), stem + ".png"));

    private static SheetRecipe Recipe(string name, SheetGeometry geometry) => new()
    {
        Name = name,
        Geometry = geometry,
        Layers = [new(Partial(AssetSlot.Head, "head1"), IsSkin: true)],
    };

    private static bool Exists(TemporaryDirectory root, string file) =>
        File.Exists((root.FullPath / file).Value);

    /// <summary>
    /// A curated-only run must not leave a <c>clips.csv</c> describing files that are not there.
    /// </summary>
    [Fact]
    public async Task WriteAll_OmitsTheFullIndex_ForACuratedOnlyRun()
    {
        using var root = TemporaryDirectory.Create();

        var failures = await RunArtifacts.WriteAllAsync(
            root.FullPath,
            new LayerRun { RunId = BatchManifest.NewRunId() },
            [Recipe("body-01", SheetGeometry.Curated)],
            TestContext.Current.CancellationToken);

        Assert.Empty(failures);
        Assert.True(Exists(root, SheetIndex.FileName));
        Assert.False(Exists(root, ClipIndex.FileName));
    }

    [Fact]
    public async Task WriteAll_OmitsTheCuratedIndex_ForAFullOnlyRun()
    {
        using var root = TemporaryDirectory.Create();

        var failures = await RunArtifacts.WriteAllAsync(
            root.FullPath,
            new LayerRun { RunId = BatchManifest.NewRunId() },
            [Recipe("raw-01", SheetGeometry.Full)],
            TestContext.Current.CancellationToken);

        Assert.Empty(failures);
        Assert.True(Exists(root, ClipIndex.FileName));
        Assert.False(Exists(root, SheetIndex.FileName));
    }

    /// <summary>Both indexes appear when the run produced both geometries.</summary>
    [Fact]
    public async Task WriteAll_WritesEveryArtifact_ForAMixedRun()
    {
        using var root = TemporaryDirectory.Create();

        var failures = await RunArtifacts.WriteAllAsync(
            root.FullPath,
            new LayerRun { RunId = BatchManifest.NewRunId() },
            [Recipe("body-01", SheetGeometry.Curated), Recipe("raw-01", SheetGeometry.Full)],
            TestContext.Current.CancellationToken);

        Assert.Empty(failures);
        Assert.True(Exists(root, SheetIndex.FileName));
        Assert.True(Exists(root, ClipIndex.FileName));
        Assert.True(Exists(root, BatchManifest.FileName));
        Assert.True(Exists(root, RunManifest.FileName));
        Assert.True(Exists(root, RunManifest.SchemaFileName));
    }

    /// <summary>
    /// One run, one id. Minting per writer would stamp <c>sheets.csv</c> and <c>manifest.json</c>
    /// with different ids and nothing would catch it, because both files would still parse.
    /// </summary>
    [Fact]
    public async Task WriteAll_StampsTheSameRunIdIntoBothManifests()
    {
        using var root = TemporaryDirectory.Create();

        var runId = BatchManifest.NewRunId();

        await RunArtifacts.WriteAllAsync(root.FullPath, new LayerRun { RunId = runId }, [Recipe("body-01", SheetGeometry.Curated)], TestContext.Current.CancellationToken);

        var csv = await File.ReadAllTextAsync((root.FullPath / BatchManifest.FileName).Value, TestContext.Current.CancellationToken);
        var json = await File.ReadAllTextAsync((root.FullPath / RunManifest.FileName).Value, TestContext.Current.CancellationToken);

        Assert.Contains(runId.ToString("D"), csv, StringComparison.Ordinal);
        Assert.Contains(runId.ToString("D"), json, StringComparison.Ordinal);
    }

    /// <summary>
    /// A missing directory is reported per artifact rather than thrown, so one unwritable manifest
    /// cannot cost the others — the sheets are already on disk by the time this runs.
    /// </summary>
    [Fact]
    public async Task WriteAll_ReportsEveryFailure_WhenTheDirectoryIsAbsent()
    {
        using var root = TemporaryDirectory.Create();

        var failures = await RunArtifacts.WriteAllAsync(
            root.FullPath / "absent",
            new LayerRun { RunId = BatchManifest.NewRunId() },
            [Recipe("body-01", SheetGeometry.Curated), Recipe("raw-01", SheetGeometry.Full)],
            TestContext.Current.CancellationToken);

        // Both indexes plus both manifests.
        Assert.Equal(4, failures.Length);
        Assert.All(failures, f => Assert.Equal(BakeFailure.OutputDirectoryUnavailable, f.Failure));

        Assert.Contains(failures, f => string.Equals(f.File, SheetIndex.FileName, StringComparison.Ordinal));
        Assert.Contains(failures, f => string.Equals(f.File, ClipIndex.FileName, StringComparison.Ordinal));
        Assert.Contains(failures, f => string.Equals(f.File, BatchManifest.FileName, StringComparison.Ordinal));
        Assert.Contains(failures, f => string.Equals(f.File, RunManifest.FileName, StringComparison.Ordinal));
    }

    /// <summary>An empty run still writes the run manifests, just with no sheets in them.</summary>
    [Fact]
    public async Task WriteAll_HandlesARunWithNoRecipes()
    {
        using var root = TemporaryDirectory.Create();

        var failures = await RunArtifacts.WriteAllAsync(root.FullPath, new LayerRun { RunId = BatchManifest.NewRunId() }, [], TestContext.Current.CancellationToken);

        Assert.False(Exists(root, SheetIndex.FileName));
        Assert.False(Exists(root, ClipIndex.FileName));
        Assert.True(Exists(root, BatchManifest.FileName));

        // manifest.json requires at least one layout, which an empty run cannot satisfy — so it is
        // reported as a schema violation rather than written describing nothing.
        Assert.Contains(
            failures,
            f => string.Equals(f.File, RunManifest.FileName, StringComparison.Ordinal) && f.Failure is BakeFailure.ManifestSchemaViolation);
    }

    /// <summary>The spec-079 deliverable is curated, whatever the page's mode toggle says.</summary>
    [Fact]
    public void RoostSheets_All_AreCuratedAndNamedForTheRegistry()
    {
        var packs = new SourcePacks
        {
            CoreAssets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "core")),
            Expansion1Assets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "e1")),
            Expansion2Assets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "e2")),
        };

        var all = RoostSheets.All(packs);

        Assert.Equal(44, all.Length);
        Assert.All(all, r => Assert.Equal(SheetGeometry.Curated, r.Geometry));

        var names = all.AsValueEnumerable().Select(static r => r.Name).ToArray();

        Assert.Equal("body-01", names[0]);
        Assert.Equal("body-07", names[6]);
        Assert.Equal("hair-01", names[7]);
        Assert.Equal("hair-09", names[15]);

        // Hair bakes as its own sheet with no body under it — one layer, not a composite.
        Assert.All(all.AsSpan()[7..].ToArray(), r => Assert.Single(r.Layers));
    }

    /// <summary>
    /// The registry is written only when the run has heroes. A curated-only export has none, and
    /// must not leave an empty one that the next run would read back as authoritative.
    /// </summary>
    [Fact]
    public async Task WriteAll_WritesNoRegistry_WhenTheRunHasNoHeroes()
    {
        using var root = TemporaryDirectory.Create();

        await RunArtifacts.WriteAllAsync(
            root.FullPath,
            new LayerRun { RunId = BatchManifest.NewRunId() },
            [Recipe("body-01", SheetGeometry.Curated)],
            TestContext.Current.CancellationToken);

        Assert.False(Exists(root, HeroRegistry.FileName));
        Assert.False(Exists(root, LoadoutWriter.CsvFileName));
    }

    /// <summary>
    /// One run, one id — across the registry as well as the two manifests.
    /// </summary>
    /// <remarks>
    /// A real export caught this: the view model minted an id for the registry and a second one for
    /// the run, so heroes.json claimed a hero was assigned in a run no manifest carried. Every file
    /// still parsed, which is exactly why nothing noticed. This is the same hazard
    /// <see cref="WriteAll_StampsTheSameRunIdIntoBothManifests"/> guards one level down.
    /// </remarks>
    [Fact]
    public async Task WriteAll_StampsTheRunIdIntoTheRegistryToo()
    {
        using var root = TemporaryDirectory.Create();

        var runId = BatchManifest.NewRunId();

        var heroes = HeroRegistry.Assign(
            [], [new HeroKey("bottom1", "top11", "head1")], "villager", runId);

        await RunArtifacts.WriteAllAsync(
            root.FullPath,
            new LayerRun { RunId = runId, Heroes = heroes },
            [Recipe("villager_01", SheetGeometry.Curated)],
            TestContext.Current.CancellationToken);

        var registry = await File.ReadAllTextAsync(
            (root.FullPath / HeroRegistry.FileName).Value, TestContext.Current.CancellationToken);
        var manifest = await File.ReadAllTextAsync(
            (root.FullPath / RunManifest.FileName).Value, TestContext.Current.CancellationToken);

        Assert.Contains(runId.ToString("D"), registry, StringComparison.Ordinal);
        Assert.Contains(runId.ToString("D"), manifest, StringComparison.Ordinal);
    }

    /// <summary>A run that knows heroes leaves the registry and its schema beside the manifests.</summary>
    [Fact]
    public async Task WriteAll_WritesTheRegistry_WhenTheRunHasHeroes()
    {
        using var root = TemporaryDirectory.Create();

        var heroes = HeroRegistry.Assign(
            [], [new HeroKey("bottom1", "top11", "head1")], "villager", BatchManifest.NewRunId());

        var failures = await RunArtifacts.WriteAllAsync(
            root.FullPath,
            new LayerRun { RunId = BatchManifest.NewRunId(), Heroes = heroes },
            [Recipe("villager_01", SheetGeometry.Curated)],
            TestContext.Current.CancellationToken);

        Assert.Empty(failures);
        Assert.True(Exists(root, HeroRegistry.FileName));
        Assert.True(Exists(root, HeroRegistry.CsvFileName));
        Assert.True(Exists(root, HeroRegistry.SchemaFileName));
    }
}
