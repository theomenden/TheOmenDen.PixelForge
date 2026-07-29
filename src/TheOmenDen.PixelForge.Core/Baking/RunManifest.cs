using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Diagnostics;
using Corvus.Text.Json;
using DotNext;
using Meziantou.Framework;
using Microsoft.IO;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Buffers;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;

// Every property name written below comes from one of these. They are generated from the schema,
// so renaming a property there is a compile error here rather than a manifest a consumer silently
// cannot read. Aliased because JsonPropertyNames is a static class — it cannot be held in a local.
using LayoutNames = TheOmenDen.PixelForge.Core.Baking.RunManifestDocument.Layouts.JsonPropertyNames;
using PaletteNames = TheOmenDen.PixelForge.Core.Baking.RunManifestDocument.Palette.JsonPropertyNames;
using RampNames = TheOmenDen.PixelForge.Core.Baking.RunManifestDocument.Ramp.JsonPropertyNames;
using RootNames = TheOmenDen.PixelForge.Core.Baking.RunManifestDocument.JsonPropertyNames;
using SchemaSlotNames = TheOmenDen.PixelForge.Core.Baking.RunManifestDocument.Slots.JsonPropertyNames;
using SheetNames = TheOmenDen.PixelForge.Core.Baking.RunManifestDocument.Sheet.JsonPropertyNames;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Writes <c>manifest.json</c>: one schema-validated document describing a whole bake run.
/// </summary>
/// <remarks>
/// <para>
/// The three CSVs beside it stay exactly as they are. They are the spreadsheet view — "every sheet
/// wearing hat4" is a column filter there and nothing else does that as well. This is the consumer
/// view: nested rather than denormalised, and carrying two things the CSVs never have — the
/// playback rate for curated sheets, and the actual colours behind a tone name.
/// </para>
/// <para>
/// <b>Composed by hand, released only if it validates.</b> Property names come from
/// <see cref="RunManifestDocument"/>'s generated <c>JsonPropertyNames</c>, so renaming a property
/// in the schema breaks this file at compile time; the <em>shape</em> is then proven at run time by
/// parsing the composed bytes back and calling <c>EvaluateSchema()</c> before anything is written.
/// An invalid manifest never reaches the disk. That is the same bargain
/// <see cref="LosslessWebp.EncodeVerified"/> strikes — verify the artifact, and make the verified
/// path the only one that produces a value.
/// </para>
/// <para>
/// The generated <c>Source</c>/<c>Builder</c> API was the alternative and was weighed against this:
/// it makes an invalid document unrepresentable rather than merely undeliverable, but its values
/// are <see langword="ref"/> <see langword="struct"/>s, so the nested arrays here — sheets of
/// slots, layouts of clips of rows — cannot be held in locals or projected with ZLinq. The verified
/// path buys the same guarantee at the boundary that actually matters, which is the file.
/// </para>
/// </remarks>
public static class RunManifest
{
    /// <summary>Name of the manifest written beside a run's sheets.</summary>
    public const string FileName = "manifest.json";

    /// <summary>Name of the schema copied in beside it.</summary>
    public const string SchemaFileName = "pixelforge-manifest-v1.json";

    /// <summary>
    /// Version of the manifest format, read from the schema rather than restated here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The schema declares <c>schemaVersion</c> as a <c>const</c>, so this is the generated
    /// constant behind it and there is no second literal to disagree with. That also upgrades what
    /// validation buys: <c>EvaluateSchema()</c> now enforces the version's <em>value</em>, not just
    /// its shape, so a writer that drifted would fail its own schema rather than emit a plausible
    /// but wrong version.
    /// </para>
    /// <para>
    /// The major component additionally appears in <see cref="SchemaFileName"/> and in the schema's
    /// <c>$id</c>. Those three are held together by a test, because no compiler spans them.
    /// </para>
    /// </remarks>
    public static string SchemaVersion { get; } =
        (string)RunManifestDocument.SchemaVersionEntity.ConstInstance;

    /// <summary>
    /// The schema itself, read once from the assembly.
    /// <para>
    /// Corvus consumes baked artifacts only — no package reference, no submodule, no build
    /// coupling — so there is deliberately no compiler spanning the seam. Shipping the schema into
    /// the export directory is what replaces it: the folder carries its own contract, and the
    /// manifest's <c>$schema</c> points at the copy sitting next to it, so an editor validates the
    /// pair with no network access.
    /// </para>
    /// </summary>
    public static string SchemaText { get; } = ReadEmbeddedSchema();

    /// <summary>Schema property name for each <see cref="AssetSlot"/>, in draw order.</summary>
    /// <remarks>
    /// Indexed by <see cref="AssetSlot"/>'s value, which <em>is</em> its draw order, so this is
    /// positional rather than a lookup — and every entry is a generated name, so a slot renamed in
    /// the schema fails the build here rather than silently dropping a property.
    /// </remarks>
    private static ImmutableArray<string> SlotNames { get; } =
    [
        SchemaSlotNames.Shadow,
        SchemaSlotNames.BackExtra,
        SchemaSlotNames.BackHair,
        SchemaSlotNames.Bottom,
        SchemaSlotNames.Top,
        SchemaSlotNames.Head,
        SchemaSlotNames.Hair,
        SchemaSlotNames.FrontExtra,
        SchemaSlotNames.Hat,
        SchemaSlotNames.Weapon,
    ];

    /// <summary>
    /// Writes <c>manifest.json</c> and <c>pixelforge-manifest-v1.json</c> into an export directory.
    /// </summary>
    /// <param name="directory">Where the run's sheets were written.</param>
    /// <param name="runId">The run identifier, from <see cref="BatchManifest.NewRunId"/>.</param>
    /// <param name="recipes">The recipes the run was given, in bake order.</param>
    /// <returns>
    /// The sheet count, or <see cref="BakeFailure.OutputDirectoryUnavailable"/> when the folder is
    /// not there, <see cref="BakeFailure.OutputWriteFailed"/> when it cannot be written, or
    /// <see cref="BakeFailure.ManifestSchemaViolation"/> when the composed document fails its own
    /// schema — in which case nothing is written at all.
    /// </returns>
    public static Result<int, BakeFailure> WriteTo(
        FullPath directory,
        Guid runId,
        ImmutableArray<SheetRecipe> recipes)
    {
        if (!Directory.Exists(directory.Value))
        {
            return new(BakeFailure.OutputDirectoryUnavailable);
        }

        // Composed and validated before either file is opened, so a schema violation cannot leave
        // a truncated manifest or an orphaned schema copy behind.
        using var buffer = PooledStreams.New(nameof(RunManifest));

        var validated = Validated(buffer, runId, recipes);

        if (!validated.TryGet(out var count))
        {
            return validated;
        }

        try
        {
            using (var manifest = File.Create((directory / FileName).Value))
            {
                buffer.WriteTo(manifest);
            }

            File.WriteAllText((directory / SchemaFileName).Value, SchemaText);

            return count;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(BakeFailure.OutputWriteFailed);
        }
    }

    /// <summary>
    /// Composes the manifest, proves it against the schema, and writes it — no filesystem
    /// involved, so the format is testable on its own.
    /// </summary>
    /// <param name="destination">Where the validated bytes go. Untouched if validation fails.</param>
    /// <param name="runId">The run identifier stamped into the document.</param>
    /// <param name="recipes">The recipes the run was given, in bake order.</param>
    /// <returns>The sheet count, or <see cref="BakeFailure.ManifestSchemaViolation"/>.</returns>
    /// <remarks>
    /// The same split <see cref="Spritesheets.SheetIndex.Write(TextWriter)"/> and
    /// <see cref="RampStore"/> use: a stream-taking core with a <see cref="FullPath"/>-taking
    /// wrapper over it.
    /// </remarks>
    public static Result<int, BakeFailure> Write(
        Stream destination,
        Guid runId,
        ImmutableArray<SheetRecipe> recipes)
    {
        Guard.IsNotNull(destination);

        using var buffer = PooledStreams.New(nameof(RunManifest));

        var validated = Validated(buffer, runId, recipes);

        if (!validated.TryGet(out var count))
        {
            return validated;
        }

        buffer.WriteTo(destination);

        return count;
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> with the manifest and parses it straight back to prove it
    /// satisfies the schema it declares.
    /// </summary>
    /// <remarks>
    /// The buffer stays the caller's to dispose. Validating here rather than at each call site is
    /// what makes the verified path the only one that yields a count — a caller who ignores the
    /// failure gets nothing to write rather than something silently malformed.
    /// </remarks>
    private static Result<int, BakeFailure> Validated(
        RecyclableMemoryStream buffer,
        Guid runId,
        ImmutableArray<SheetRecipe> recipes)
    {
        var sheets = recipes.IsDefault ? [] : recipes;

        Compose(buffer, runId, sheets);

        // GetBuffer is zero-copy; ToArray throws by design on this manager because it would copy
        // the pooled buffer straight back onto the managed heap.
        var utf8 = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);

        using var parsed = ParsedJsonDocument<RunManifestDocument>.Parse(utf8);

        if (!parsed.RootElement.EvaluateSchema())
        {
            return new(BakeFailure.ManifestSchemaViolation);
        }

        return sheets.Length;
    }

    private static void Compose(Stream destination, Guid runId, ImmutableArray<SheetRecipe> recipes)
    {
        using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        writer.WriteString(RootNames.SchemaUtf8, SchemaFileName);

        // The schema's own constant, written as the typed value rather than through a string, so
        // the bytes on disk come from the contract itself.
        writer.WritePropertyName(RootNames.SchemaVersionUtf8);
        RunManifestDocument.SchemaVersionEntity.ConstInstance.WriteTo(writer);

        writer.WriteString(RootNames.RunIdUtf8, runId);

        WritePalette(writer, recipes);
        RunManifestLayouts.Write(writer, recipes);
        WriteSheets(writer, recipes);

        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>Writes the ramp every partial is authored in, then the tones this run applied.</summary>
    private static void WritePalette(Utf8JsonWriter writer, ImmutableArray<SheetRecipe> recipes)
    {
        writer.WriteStartObject(RootNames.PaletteValueUtf8);

        writer.WritePropertyName(PaletteNames.SourceRampUtf8);
        WriteRamp(writer, SkinRamps.Source);

        writer.WriteStartArray(PaletteNames.RampsUtf8);

        foreach (var ramp in TonesIn(recipes))
        {
            WriteRamp(writer, ramp);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteRamp(Utf8JsonWriter writer, SkinRamp ramp)
    {
        writer.WriteStartObject();
        writer.WriteString(RampNames.NameUtf8, ramp.Name);
        writer.WriteBoolean(RampNames.IsHumanUtf8, ramp.IsHuman);

        writer.WriteStartArray(RampNames.StepsUtf8);

        foreach (var step in ramp.Steps)
        {
            writer.WriteStringValue(Hex(step));
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSheets(Utf8JsonWriter writer, ImmutableArray<SheetRecipe> recipes)
    {
        writer.WriteStartArray(RootNames.SheetsUtf8);

        foreach (var recipe in recipes)
        {
            WriteSheet(writer, recipe);
        }

        writer.WriteEndArray();
    }

    private static void WriteSheet(Utf8JsonWriter writer, SheetRecipe recipe)
    {
        writer.WriteStartObject();
        writer.WriteString(SheetNames.NameUtf8, recipe.Name);
        writer.WriteString(SheetNames.FileUtf8, recipe.Name + SheetWriter.Extension);
        writer.WriteString(SheetNames.GeometryUtf8, GeometryName(recipe.Geometry));

        // Absent rather than blank when the sheet carries no skin. The CSV writes an empty cell
        // because a spreadsheet shows the text "null" as data; JSON can say "not applicable".
        if (recipe.Tone.TryGet(out var tone))
        {
            writer.WriteString(SheetNames.ToneUtf8, tone.Name);
        }

        writer.WriteStartObject(SheetNames.SlotsValueUtf8);

        var stems = BatchManifest.StemsBySlot(recipe);

        for (var slot = 0; slot < stems.Length; slot++)
        {
            if (!string.IsNullOrEmpty(stems[slot]))
            {
                writer.WriteString(SlotNames[slot], stems[slot]);
            }
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>
    /// The distinct tones this run applied, in first-use order.
    /// </summary>
    /// <remarks>
    /// Matched by name case-insensitively, which is the identity rule
    /// <see cref="SkinRamps.IsBuiltIn"/> enforces — so a custom ramp appears here exactly once and
    /// can never be confused with the built-in it is forbidden from shadowing.
    /// </remarks>
    private static ImmutableArray<SkinRamp> TonesIn(ImmutableArray<SheetRecipe> recipes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tones = ImmutableArray.CreateBuilder<SkinRamp>();

        foreach (var recipe in recipes)
        {
            if (recipe.Tone.TryGet(out var ramp) && seen.Add(ramp.Name))
            {
                tones.Add(ramp);
            }
        }

        return tones.ToImmutable();
    }

    /// <summary>
    /// The key under <c>layouts</c> that describes this geometry.
    /// </summary>
    /// <remarks>
    /// Deliberately the generated property name rather than <see cref="Enum.ToString()"/>: a
    /// sheet's <c>geometry</c> value and the layout it points at are then the same string by
    /// construction, and neither can drift to <c>"Curated"</c> against a schema that says
    /// <c>"curated"</c>.
    /// </remarks>
    private static string GeometryName(SheetGeometry geometry) => geometry switch
    {
        SheetGeometry.Curated => LayoutNames.Curated,
        SheetGeometry.Full => LayoutNames.Full,
        _ => ThrowHelper.ThrowArgumentOutOfRangeException<string>(nameof(geometry)),
    };

    /// <summary>Formats a colour as the schema's uppercase <c>#RRGGBB</c>. Alpha is never carried.</summary>
    private static string Hex(SKColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}");

    private static string ReadEmbeddedSchema()
    {
        var assembly = typeof(RunManifest).Assembly;

        // GetManifestResourceNames returns an array, which the ZLinq drop-in generator covers, so
        // this First binds to ZLinq without an AsValueEnumerable.
        var name = assembly.GetManifestResourceNames()
            .First(candidate => candidate.EndsWith(SchemaFileName, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
