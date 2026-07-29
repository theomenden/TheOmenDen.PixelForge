using System.Text;
using System.Text.Json;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// <c>manifest.json</c> is the contract handed to consumers, and there is no compiler spanning
/// that seam — so these tests are the seam.
/// </summary>
/// <remarks>
/// <para>
/// A successful <see cref="RunManifest.Write"/> is itself the schema assertion: the writer parses
/// its own output and runs <c>EvaluateSchema()</c> before yielding a count, so a document that
/// violates <c>pixelforge-manifest-v1.json</c> comes back as
/// <see cref="BakeFailure.ManifestSchemaViolation"/> with nothing written. Every
/// <c>Assert.True(IsSuccessful)</c> below therefore carries the whole schema behind it.
/// </para>
/// <para>
/// Reading the result back with <see cref="JsonDocument"/> rather than the generated type is
/// deliberate: it is an independent reader, so it also proves Corvus's own writer emits JSON that
/// an ordinary System.Text.Json consumer can parse.
/// </para>
/// </remarks>
public sealed class RunManifestTests
{
    /// <summary>A synthetic <c>&lt;pack&gt;/&lt;slot&gt;/&lt;stem&gt;.png</c> path; nothing is written.</summary>
    private static FullPath Partial(AssetSlot slot, string stem) =>
        FullPath.FromPath(Path.Combine(Path.GetTempPath(), "pack", AssetSlots.FolderName(slot), stem + ".png"));

    private static SheetRecipe Recipe(
        string name,
        SheetGeometry geometry = SheetGeometry.Curated,
        SkinRamp? tone = null) => new()
        {
            Name = name,
            Geometry = geometry,
            Layers =
            [
                new(Partial(AssetSlot.Bottom, "bottom1"), IsSkin: true),
                new(Partial(AssetSlot.Top, "top11"), IsSkin: true),
                new(Partial(AssetSlot.Head, "head1"), IsSkin: true),
            ],
            Tone = tone is null ? default : tone,
        };

    /// <summary>Writes the manifest and hands back its parsed root.</summary>
    private static JsonDocument Manifest(params SheetRecipe[] recipes)
    {
        using var stream = new MemoryStream();

        var written = RunManifest.Write(stream, BatchManifest.NewRunId(), [.. recipes]);

        Assert.True(written.IsSuccessful, $"manifest failed its own schema with {written.Error}");
        Assert.Equal(recipes.Length, written.Value);

        return JsonDocument.Parse(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static JsonElement Layouts(JsonDocument manifest) => manifest.RootElement.GetProperty("layouts");

    [Fact]
    public void Write_StampsTheRunIdAndSchemaVersion()
    {
        using var stream = new MemoryStream();

        var runId = BatchManifest.NewRunId();
        var written = RunManifest.Write(stream, runId, [Recipe("body-01")]);

        Assert.True(written.IsSuccessful, $"manifest failed its own schema with {written.Error}");

        using var manifest = JsonDocument.Parse(Encoding.UTF8.GetString(stream.ToArray()));

        Assert.Equal(runId.ToString("D"), manifest.RootElement.GetProperty("runId").GetString());
        Assert.Equal(RunManifest.SchemaVersion, manifest.RootElement.GetProperty("schemaVersion").GetString());

        // A relative $schema so an editor validates against the copy sitting beside it.
        Assert.Equal(RunManifest.SchemaFileName, manifest.RootElement.GetProperty("$schema").GetString());
    }

    /// <summary>
    /// A curated-only run must not describe the full geometry, exactly as it leaves no
    /// <c>clips.csv</c> — a manifest never describes files that are not in the folder.
    /// </summary>
    [Fact]
    public void Write_OmitsTheFullLayout_ForACuratedOnlyRun()
    {
        using var manifest = Manifest(Recipe("body-01"), Recipe("body-02"));

        Assert.True(Layouts(manifest).TryGetProperty("curated", out _));
        Assert.False(Layouts(manifest).TryGetProperty("full", out _));
    }

    [Fact]
    public void Write_OmitsTheCuratedLayout_ForAFullOnlyRun()
    {
        using var manifest = Manifest(Recipe("raw-01", SheetGeometry.Full));

        Assert.True(Layouts(manifest).TryGetProperty("full", out _));
        Assert.False(Layouts(manifest).TryGetProperty("curated", out _));
    }

    [Fact]
    public void Write_DescribesBothGeometries_WhenTheRunProducedBoth()
    {
        using var manifest = Manifest(Recipe("body-01"), Recipe("raw-01", SheetGeometry.Full));

        Assert.True(Layouts(manifest).TryGetProperty("curated", out _));
        Assert.True(Layouts(manifest).TryGetProperty("full", out _));
    }

    /// <summary>
    /// The gap this manifest closes: <c>index.csv</c> has never carried a playback rate, so a
    /// consumer of a curated sheet had to guess the cadence the art was authored for.
    /// </summary>
    [Fact]
    public void Write_StatesTheFrameDuration_ForCuratedSheets()
    {
        using var manifest = Manifest(Recipe("body-01"));

        var curated = Layouts(manifest).GetProperty("curated");

        Assert.Equal(GeneratorClips.FrameDurationMilliseconds, curated.GetProperty("frameDurationMs").GetInt32());
        Assert.Equal(SheetLayout.OutputWidth, curated.GetProperty("width").GetInt32());
        Assert.Equal(SheetLayout.OutputHeight, curated.GetProperty("height").GetInt32());
        Assert.Equal(SheetLayout.CellSize, curated.GetProperty("cellSize").GetInt32());
    }

    /// <summary>
    /// Rows are stated per facing so a consumer never has to know the <c>clip * 3 + facing</c>
    /// formula. Derived from <see cref="SheetLayout.RowFor"/>, so the manifest cannot drift from
    /// the remap the baker performs.
    /// </summary>
    [Fact]
    public void Write_StatesEachCuratedClipRowPerFacing()
    {
        using var manifest = Manifest(Recipe("body-01"));

        var clips = Layouts(manifest).GetProperty("curated").GetProperty("clips");

        Assert.Equal(SheetLayout.ClipCount, clips.GetArrayLength());

        for (var index = 0; index < SheetLayout.Clips.Length; index++)
        {
            var clip = clips[index];
            var rows = clip.GetProperty("rows");

            Assert.Equal(SheetLayout.Clips[index].Name, clip.GetProperty("name").GetString());
            Assert.Equal(SheetLayout.Clips[index].FrameCount, clip.GetProperty("frameCount").GetInt32());
            Assert.Equal(SheetLayout.RowFor(index, 0), rows.GetProperty("south").GetInt32());
            Assert.Equal(SheetLayout.RowFor(index, 1), rows.GetProperty("west").GetInt32());
            Assert.Equal(SheetLayout.RowFor(index, 2), rows.GetProperty("east").GetInt32());
        }
    }

    /// <summary>
    /// Full-geometry frames are playback order, which repeats and descends — <c>walk</c> is
    /// 1, 2, 1, 0. Re-sorting them is the obvious mistake, so the order is asserted verbatim.
    /// </summary>
    [Fact]
    public void Write_KeepsFullClipColumnsInPlaybackOrder()
    {
        using var manifest = Manifest(Recipe("raw-01", SheetGeometry.Full));

        var clips = Layouts(manifest).GetProperty("full").GetProperty("clips");
        var walk = GeneratorClips.All.AsSpan().First(clip => clip.Name is "walk");

        Assert.Equal(GeneratorClips.All.Length, clips.GetArrayLength());

        for (var index = 0; index < clips.GetArrayLength(); index++)
        {
            if (clips[index].GetProperty("name").GetString() is not "walk")
            {
                continue;
            }

            var columns = clips[index].GetProperty("columns");

            Assert.Equal(walk.Frames.Length, columns.GetArrayLength());

            for (var frame = 0; frame < walk.Frames.Length; frame++)
            {
                Assert.Equal(walk.Frames[frame], columns[frame].GetInt32());
            }

            return;
        }

        Assert.Fail("the full layout carried no walk clip");
    }

    /// <summary>
    /// The colours were never exported at all before this — <c>sheets.csv</c> carries a tone name
    /// and nothing else, leaving a consumer no way to match a UI swatch to the art.
    /// </summary>
    [Fact]
    public void Write_CarriesTheSourceRampAsHexSteps()
    {
        using var manifest = Manifest(Recipe("body-01"));

        var source = manifest.RootElement.GetProperty("palette").GetProperty("sourceRamp");
        var steps = source.GetProperty("steps");

        Assert.Equal(SkinRamps.Source.Name, source.GetProperty("name").GetString());
        Assert.True(source.GetProperty("isHuman").GetBoolean());
        Assert.Equal(SkinRamps.StepCount, steps.GetArrayLength());

        // Verbatim from SkinRamps.Source — uppercase, opaque, no alpha.
        Assert.Equal("#73172D", steps[0].GetString());
        Assert.Equal("#FAF4D6", steps[4].GetString());
    }

    /// <summary>Only the tones the run actually applied, and each of them exactly once.</summary>
    [Fact]
    public void Write_CarriesEachAppliedToneOnce()
    {
        var green = SkinRamps.All.AsSpan().First(ramp => !ramp.IsHuman);

        using var manifest = Manifest(
            Recipe("body-01", tone: green),
            Recipe("body-02", tone: green),
            Recipe("body-03", tone: SkinRamps.All[1]));

        var ramps = manifest.RootElement.GetProperty("palette").GetProperty("ramps");

        Assert.Equal(2, ramps.GetArrayLength());
        Assert.Equal(green.Name, ramps[0].GetProperty("name").GetString());
        Assert.False(ramps[0].GetProperty("isHuman").GetBoolean());
        Assert.Equal(SkinRamps.All[1].Name, ramps[1].GetProperty("name").GetString());
    }

    /// <summary>A run that applied no tone carries an empty array, never a missing property.</summary>
    [Fact]
    public void Write_CarriesAnEmptyRampArray_WhenNoToneWasApplied()
    {
        using var manifest = Manifest(Recipe("hair-01"));

        Assert.Equal(0, manifest.RootElement.GetProperty("palette").GetProperty("ramps").GetArrayLength());
    }

    /// <summary>
    /// A sheet's <c>geometry</c> is the key under <c>layouts</c> that describes it, so the two are
    /// the same string by construction — not <c>"Curated"</c> against a schema saying
    /// <c>"curated"</c>.
    /// </summary>
    [Fact]
    public void Write_NamesGeometryWithTheLayoutKeyItPointsAt()
    {
        using var manifest = Manifest(Recipe("body-01"), Recipe("raw-01", SheetGeometry.Full));

        var sheets = manifest.RootElement.GetProperty("sheets");

        Assert.Equal("curated", sheets[0].GetProperty("geometry").GetString());
        Assert.Equal("full", sheets[1].GetProperty("geometry").GetString());

        foreach (var sheet in sheets.EnumerateArray())
        {
            Assert.True(Layouts(manifest).TryGetProperty(sheet.GetProperty("geometry").GetString()!, out _));
        }
    }

    /// <summary>
    /// JSON can say "not applicable", so an unfilled slot is absent rather than the empty string
    /// the CSV has to write.
    /// </summary>
    [Fact]
    public void Write_OmitsSlotsTheRecipeNeverFilled()
    {
        using var manifest = Manifest(Recipe("body-01"));

        var slots = manifest.RootElement.GetProperty("sheets")[0].GetProperty("slots");

        Assert.Equal("bottom1", slots.GetProperty("bottom").GetString());
        Assert.Equal("top11", slots.GetProperty("top").GetString());
        Assert.Equal("head1", slots.GetProperty("head").GetString());
        Assert.False(slots.TryGetProperty("hat", out _));
        Assert.False(slots.TryGetProperty("weapon", out _));
    }

    /// <summary>A sheet with no skin carries no tone property at all.</summary>
    [Fact]
    public void Write_OmitsToneForASheetWithNoSkin()
    {
        using var manifest = Manifest(Recipe("hair-01"));

        Assert.False(manifest.RootElement.GetProperty("sheets")[0].TryGetProperty("tone", out _));
    }

    [Fact]
    public void Write_NamesTheOutputFileWithItsExtension()
    {
        using var manifest = Manifest(Recipe("body-01"));

        var sheet = manifest.RootElement.GetProperty("sheets")[0];

        Assert.Equal("body-01", sheet.GetProperty("name").GetString());
        Assert.Equal("body-01" + SheetWriter.Extension, sheet.GetProperty("file").GetString());
    }

    /// <summary>
    /// The export folder has to carry its own contract: Corvus consumes baked artifacts with no
    /// build coupling, so the schema travels with the manifest that declares it.
    /// </summary>
    [Fact]
    public void WriteTo_CopiesTheSchemaBesideTheManifest()
    {
        using var root = TemporaryDirectory.Create();

        var written = RunManifest.WriteTo(root.FullPath, BatchManifest.NewRunId(), [Recipe("body-01")]);

        Assert.True(written.IsSuccessful, $"write failed with {written.Error}");
        Assert.Equal(1, written.Value);

        var schema = (root.FullPath / RunManifest.SchemaFileName).Value;

        Assert.True(File.Exists((root.FullPath / RunManifest.FileName).Value));
        Assert.True(File.Exists(schema));

        // The copy is the schema the generator compiled against, not a paraphrase of it.
        Assert.Equal(RunManifest.SchemaText, File.ReadAllText(schema));
        Assert.Contains("\"$id\"", RunManifest.SchemaText, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteTo_ReportsOutputDirectoryUnavailable_WhenTheFolderIsAbsent()
    {
        using var root = TemporaryDirectory.Create();

        var result = RunManifest.WriteTo(root.FullPath / "absent", BatchManifest.NewRunId(), [Recipe("body-01")]);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.OutputDirectoryUnavailable, result.Error);
    }
}
