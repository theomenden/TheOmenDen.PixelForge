using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// At 168 files a run's filenames are not a usable index. This manifest is what maps a baked
/// sheet back to the partials and tone that produced it.
/// </summary>
public sealed class BatchManifestTests
{
    private static BatchManifestRow Row(string name) => new()
    {
        Name = name,
        File = name + ".webp",
        Geometry = nameof(SheetGeometry.Curated),
        Format = nameof(SheetFormat.Webp),
        Tone = "Tone 3",
        Bottom = "bottom1",
        Top = "top11",
        Head = "head1",
        Hair = "hair15c3",
        Hat = string.Empty,
    };

    /// <summary>
    /// UUIDv7, not v4: run ids are stamped into the manifest and the log, and v7's leading
    /// timestamp makes them sort chronologically instead of scattering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This single assertion is the whole guard, and it is deliberately not accompanied by an
    /// "ids sort in creation order" test. Only the leading 48 bits of a v7 UUID are the timestamp
    /// and the BCL does not promise monotonicity inside one millisecond, so such a test either
    /// ties and fails at random or has to sleep across a millisecond boundary — a flake or a delay
    /// in exchange for no coverage this does not already have. Swapping
    /// <see cref="Guid.CreateVersion7()"/> for <see cref="Guid.NewGuid"/> is the regression worth
    /// catching, and a v4 id fails right here on <see cref="Guid.Version"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void NewRunId_IsAVersion7Uuid() => Assert.Equal(7, BatchManifest.NewRunId().Version);

    [Fact]
    public async Task WriteTo_RecordsEveryRowAgainstTheRunId()
    {
        using var root = TemporaryDirectory.Create();

        var runId = BatchManifest.NewRunId();
        var written = await BatchManifest.WriteToAsync(root.FullPath, runId, [Row("a"), Row("b")], TestContext.Current.CancellationToken);

        Assert.True(written.IsSuccessful, $"write failed with {written.Error}");
        Assert.Equal(2, written.Value);

        var text = await File.ReadAllTextAsync((root.FullPath / BatchManifest.FileName).Value, TestContext.Current.CancellationToken);

        Assert.Contains(runId.ToString("D"), text, StringComparison.Ordinal);
        Assert.Contains("top11", text, StringComparison.Ordinal);
        Assert.Contains("hair15c3", text, StringComparison.Ordinal);

        // The run id leads, so rows stay attributable when two runs' manifests are concatenated.
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        // Format sits beside Geometry because it is its peer, and because Sep derives the header
        // from the order columns are first set — so this string is the column order, not a summary
        // of it.
        Assert.StartsWith("RunId,Name,File,Geometry,Format,Tone,", lines[0], StringComparison.Ordinal);
        Assert.StartsWith(runId.ToString("D") + ",a,", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteTo_ReportsOutputDirectoryUnavailable_WhenTheFolderIsAbsent()
    {
        using var root = TemporaryDirectory.Create();

        var result = await BatchManifest.WriteToAsync(root.FullPath / "absent", BatchManifest.NewRunId(), [Row("a")], TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.OutputDirectoryUnavailable, result.Error);
    }

    /// <summary>
    /// The slot columns come from each layer's parent folder, which is the only place the slot
    /// survives — a recipe carries paths and skin flags by the time it reaches the baker.
    /// </summary>
    [Fact]
    public void RowFor_PutsEachLayerInItsOwnSlotColumn()
    {
        var row = BatchManifest.RowFor(new()
        {
            Name = "bottom1_top11_head1_hair15",
            Layers =
            [
                new(Partial(AssetSlot.Bottom, "bottom1"), IsSkin: true),
                new(Partial(AssetSlot.Top, "top11"), IsSkin: true),
                new(Partial(AssetSlot.Head, "head1"), IsSkin: true),
                new(Partial(AssetSlot.Hair, "hair15_c3"), IsSkin: false),
            ],
            Tone = SkinRamps.All[2],
        });

        Assert.Equal("bottom1", row.Bottom);
        Assert.Equal("top11", row.Top);
        Assert.Equal("head1", row.Head);
        Assert.Equal("hair15_c3", row.Hair);
        Assert.Equal(SkinRamps.All[2].Name, row.Tone);
        Assert.Equal("bottom1_top11_head1_hair15.webp", row.File);
        Assert.Equal(nameof(SheetGeometry.Curated), row.Geometry);
    }

    /// <summary>A slot the recipe never filled writes a blank cell, never a stale one.</summary>
    [Fact]
    public void RowFor_LeavesUnfilledSlotsEmpty()
    {
        var row = BatchManifest.RowFor(new()
        {
            Name = "hair15",
            Layers = [new(Partial(AssetSlot.Hair, "hair15"), IsSkin: false)],
            Geometry = SheetGeometry.Full,
        });

        Assert.Equal(string.Empty, row.Head);
        Assert.Equal(string.Empty, row.Weapon);
        Assert.Equal(string.Empty, row.Tone);
        Assert.Equal(nameof(SheetGeometry.Full), row.Geometry);
    }

    /// <summary>A path that is not under a slot folder contributes no column rather than faulting.</summary>
    [Fact]
    public void RowFor_IgnoresAPathFromOutsideThePacks()
    {
        var row = BatchManifest.RowFor(new()
        {
            Name = "stray",
            Layers = [new(FullPath.FromPath(Path.Combine(Path.GetTempPath(), "elsewhere", "stray.png")), IsSkin: false)],
        });

        Assert.Equal("stray", row.Name);
        Assert.Equal(string.Empty, row.Bottom);
        Assert.Equal(string.Empty, row.Top);
        Assert.Equal(string.Empty, row.Head);
        Assert.Equal(string.Empty, row.Hair);
    }

    /// <summary>A synthetic <c>&lt;pack&gt;/&lt;slot&gt;/&lt;stem&gt;.png</c> path; nothing is written.</summary>
    private static FullPath Partial(AssetSlot slot, string stem) =>
        FullPath.FromPath(Path.Combine(Path.GetTempPath(), "pack", AssetSlots.FolderName(slot), stem + ".png"));

    /// <summary>An empty slot must be an empty cell, not the string "null".</summary>
    [Fact]
    public async Task WriteTo_LeavesUnusedSlotsBlank()
    {
        using var root = TemporaryDirectory.Create();

        await BatchManifest.WriteToAsync(root.FullPath, BatchManifest.NewRunId(), [Row("a")], TestContext.Current.CancellationToken);

        var text = await File.ReadAllTextAsync((root.FullPath / BatchManifest.FileName).Value, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("null", text, StringComparison.OrdinalIgnoreCase);
    }
}
