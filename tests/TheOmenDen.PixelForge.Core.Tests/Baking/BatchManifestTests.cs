using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// At 168 files a run's filenames are not a usable index. This manifest is what maps a baked
/// sheet back to the partials and tone that produced it.
/// </summary>
public sealed class BatchManifestTests
{
    // "Curated" is a string literal rather than nameof(SheetGeometry.Curated) because the geometry
    // enum lands in a later task; Geometry is a string column either way.
    private static BatchManifestRow Row(string name) => new()
    {
        Name = name,
        File = name + ".webp",
        Geometry = "Curated",
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
    public void WriteTo_RecordsEveryRowAgainstTheRunId()
    {
        using var root = TemporaryDirectory.Create();

        var runId = BatchManifest.NewRunId();
        var written = BatchManifest.WriteTo(root.FullPath, runId, [Row("a"), Row("b")]);

        Assert.True(written.IsSuccessful, $"write failed with {written.Error}");
        Assert.Equal(2, written.Value);

        var text = File.ReadAllText((root.FullPath / BatchManifest.FileName).Value);

        Assert.Contains(runId.ToString("D"), text, StringComparison.Ordinal);
        Assert.Contains("top11", text, StringComparison.Ordinal);
        Assert.Contains("hair15c3", text, StringComparison.Ordinal);

        // The run id leads, so rows stay attributable when two runs' manifests are concatenated.
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("RunId,Name,File,Geometry,Tone,", lines[0], StringComparison.Ordinal);
        Assert.StartsWith(runId.ToString("D") + ",a,", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void WriteTo_ReportsOutputDirectoryUnavailable_WhenTheFolderIsAbsent()
    {
        using var root = TemporaryDirectory.Create();

        var result = BatchManifest.WriteTo(root.FullPath / "absent", BatchManifest.NewRunId(), [Row("a")]);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.OutputDirectoryUnavailable, result.Error);
    }

    /// <summary>An empty slot must be an empty cell, not the string "null".</summary>
    [Fact]
    public void WriteTo_LeavesUnusedSlotsBlank()
    {
        using var root = TemporaryDirectory.Create();

        BatchManifest.WriteTo(root.FullPath, BatchManifest.NewRunId(), [Row("a")]);

        var text = File.ReadAllText((root.FullPath / BatchManifest.FileName).Value);

        Assert.DoesNotContain("null", text, StringComparison.OrdinalIgnoreCase);
    }
}
