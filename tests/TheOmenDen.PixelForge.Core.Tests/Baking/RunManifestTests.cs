using System.Text;
using System.Text.Json;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;

// Corvus.Text.Json is aliased rather than imported: it ships its own JsonElement, and importing the
// namespace makes every System.Text.Json.JsonElement in this file ambiguous (CS0104).
using ParsedManifest =
    Corvus.Text.Json.ParsedJsonDocument<TheOmenDen.PixelForge.Core.Baking.RunManifestDocument>;

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

    /// <summary>
    /// Asserts an object carries exactly <paramref name="expected"/> — no more, no fewer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because <c>additionalProperties: false</c> was deliberately lifted from every
    /// object that can plausibly grow, so that adding an optional field — or a third geometry — is a
    /// minor version bump rather than a break for consumers holding an older schema.
    /// </para>
    /// <para>
    /// That trade costs the producer a real check, though not the obvious one. A misspelled name is
    /// not the risk — every name the writer emits comes from a generated <c>JsonPropertyNames</c>
    /// constant, so <c>tonne</c> for <c>tone</c> cannot compile. What an open object no longer
    /// catches is a <em>correctly spelled</em> property written at the <em>wrong nesting level</em>:
    /// <c>frameDurationMs</c> emitted onto a clip instead of its layout validates perfectly
    /// cleanly. Verified by doing exactly that — schema validation passed and only this test failed.
    /// </para>
    /// <para>
    /// So these assertions are the only thing standing between that mistake and a shipped manifest.
    /// <b>Do not delete them as redundant with schema validation; validation can no longer see
    /// this class of error at all.</b>
    /// </para>
    /// <para>
    /// Reports the symmetric difference rather than a count, so a failure names the key.
    /// </para>
    /// </remarks>
    private static void AssertExactProperties(JsonElement element, string where, params string[] expected)
    {
        var actual = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            actual.Add(property.Name);
        }

        var wanted = new HashSet<string>(expected, StringComparer.Ordinal);

        var unexpected = new HashSet<string>(actual, StringComparer.Ordinal);
        unexpected.ExceptWith(wanted);

        var missing = new HashSet<string>(wanted, StringComparer.Ordinal);
        missing.ExceptWith(actual);

        Assert.True(
            unexpected.Count is 0 && missing.Count is 0,
            $"{where} — unexpected: [{string.Join(", ", unexpected)}], missing: [{string.Join(", ", missing)}]");
    }

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
    /// The version is a <c>const</c> in the schema, not a <c>pattern</c>, so validation enforces
    /// its value rather than merely its shape.
    /// </summary>
    /// <remarks>
    /// This is the only assertion that distinguishes the two. Every other test here would pass
    /// equally well against a schema accepting any semver at all, so without this one the change
    /// from <c>pattern</c> to <c>const</c> is untested — and a writer emitting <c>"2.0.0"</c>
    /// against a v1 schema would ship looking perfectly well-formed.
    /// </remarks>
    [Fact]
    public void EvaluateSchema_RejectsAManifestCarryingTheWrongVersion()
    {
        using var stream = new MemoryStream();

        RunManifest.Write(stream, BatchManifest.NewRunId(), [Recipe("body-01")]);

        var json = Encoding.UTF8.GetString(stream.ToArray());

        // Sanity first, so a false below is about the version and nothing else.
        using (var honest = ParsedManifest.Parse(json))
        {
            Assert.True(honest.RootElement.EvaluateSchema());
        }

        var tampered = json.Replace($"\"{RunManifest.SchemaVersion}\"", "\"9.9.9\"", StringComparison.Ordinal);

        // Proves the substitution landed — otherwise this test would assert against an untouched
        // document and pass for the wrong reason.
        Assert.Contains("\"9.9.9\"", tampered, StringComparison.Ordinal);

        using var parsed = ParsedManifest.Parse(tampered);

        Assert.False(parsed.RootElement.EvaluateSchema());
    }

    /// <summary>
    /// Pins the property set at every object the schema leaves open for extension — run level.
    /// </summary>
    /// <remarks>See <see cref="AssertExactProperties"/> for why this cannot be left to the schema.</remarks>
    [Fact]
    public void Write_EmitsExactlyTheExpectedProperties_AtRunLevel()
    {
        using var manifest = Manifest(
            Recipe("body-01", tone: SkinRamps.All[1]),
            Recipe("hair-01"));

        var palette = manifest.RootElement.GetProperty("palette");
        var sheets = manifest.RootElement.GetProperty("sheets");

        AssertExactProperties(
            manifest.RootElement,
            "root",
            "$schema", "schemaVersion", "runId", "palette", "layouts", "sheets");

        AssertExactProperties(palette, "palette", "sourceRamp", "ramps");
        AssertExactProperties(palette.GetProperty("sourceRamp"), "ramp", "name", "isHuman", "steps");

        // tone is optional, so both shapes are pinned — a toned sheet and a bare one.
        AssertExactProperties(sheets[0], "sheet (toned)", "name", "file", "geometry", "tone", "slots");
        AssertExactProperties(sheets[1], "sheet (no tone)", "name", "file", "geometry", "slots");

        AssertExactProperties(sheets[0].GetProperty("slots"), "slots", "bottom", "top", "head");
    }

    /// <summary>
    /// Pins the property set at every object the schema leaves open for extension — layout level.
    /// </summary>
    /// <remarks>See <see cref="AssertExactProperties"/> for why this cannot be left to the schema.</remarks>
    [Fact]
    public void Write_EmitsExactlyTheExpectedProperties_AtLayoutLevel()
    {
        using var manifest = Manifest(Recipe("body-01"), Recipe("raw-01", SheetGeometry.Full));

        var curated = Layouts(manifest).GetProperty("curated");
        var full = Layouts(manifest).GetProperty("full");

        AssertExactProperties(Layouts(manifest), "layouts", "curated", "full");

        AssertExactProperties(
            curated,
            "curatedLayout",
            "width", "height", "cellSize", "columns", "rows", "frameDurationMs", "facings", "clips");

        AssertExactProperties(
            curated.GetProperty("clips")[0],
            "curatedClip",
            "name", "frameCount", "sourceColumn", "rows");

        AssertExactProperties(
            curated.GetProperty("clips")[0].GetProperty("rows"),
            "curatedClip.rows",
            "south", "west", "east");

        AssertExactProperties(
            full,
            "fullLayout",
            "width", "height", "cellSize", "columns", "rows", "frameDurationMs", "facingRows", "clips");

        AssertExactProperties(
            full.GetProperty("facingRows"),
            "fullLayout.facingRows",
            "south", "west", "east", "north");

        AssertExactProperties(
            full.GetProperty("clips")[0],
            "fullClip",
            "name", "columns", "isRenderedByDefault", "reverseDrawOrder");
    }

    /// <summary>The version is declared once, in the schema, and read back out of it.</summary>
    [Fact]
    public void SchemaVersion_ComesFromTheSchemaItself()
    {
        Assert.Equal("1.0.0", RunManifest.SchemaVersion);
        Assert.Contains(
            $"\"const\": \"{RunManifest.SchemaVersion}\"",
            RunManifest.SchemaText,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The schema's <c>$id</c>, <see cref="RunManifest.SchemaFileName"/> and the major component of
    /// <see cref="RunManifest.SchemaVersion"/> all carry the major version, and all three are
    /// maintained by hand. This is the only thing holding them together.
    /// </summary>
    /// <remarks>
    /// The major is derived from <see cref="RunManifest.SchemaVersion"/> rather than written as
    /// <c>1</c>, or the assertion would pass vacuously after a v2 bump and defeat its own purpose.
    /// </remarks>
    [Fact]
    public void SchemaId_AgreesWithTheFileNameAndTheMajorVersion()
    {
        using var schema = JsonDocument.Parse(RunManifest.SchemaText);

        var id = schema.RootElement.GetProperty("$id").GetString();

        Assert.NotNull(id);

        // The $id's last segment IS the file shipped beside the manifest.
        var segment = id[(id.LastIndexOf('/') + 1)..];

        Assert.Equal(RunManifest.SchemaFileName, segment);

        var major = RunManifest.SchemaVersion.Split('.')[0];

        Assert.EndsWith($"-v{major}.json", segment, StringComparison.Ordinal);
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
