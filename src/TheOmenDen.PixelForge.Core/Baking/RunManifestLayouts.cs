using System.Collections.Immutable;
using Corvus.Text.Json;
using TheOmenDen.PixelForge.Core.Spritesheets;

using CuratedClipNames = TheOmenDen.PixelForge.Schema.RunManifestDocument.CuratedClip.JsonPropertyNames;
using CuratedNames = TheOmenDen.PixelForge.Schema.RunManifestDocument.CuratedLayout.JsonPropertyNames;
using CuratedRowNames = TheOmenDen.PixelForge.Schema.RunManifestDocument.CuratedClip.RowsEntity.JsonPropertyNames;
using FullClipNames = TheOmenDen.PixelForge.Schema.RunManifestDocument.FullClip.JsonPropertyNames;
using FullFacingNames = TheOmenDen.PixelForge.Schema.RunManifestDocument.FullLayout.FacingRowsEntity.JsonPropertyNames;
using FullNames = TheOmenDen.PixelForge.Schema.RunManifestDocument.FullLayout.JsonPropertyNames;
using LayoutNames = TheOmenDen.PixelForge.Schema.RunManifestDocument.Layouts.JsonPropertyNames;
using RootNames = TheOmenDen.PixelForge.Schema.RunManifestDocument.JsonPropertyNames;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// The <c>layouts</c> half of <see cref="RunManifest"/>: how to read a sheet of each geometry.
/// </summary>
/// <remarks>
/// <para>
/// Split from <see cref="RunManifest"/> along the line that matters — everything here is derived
/// from the static tables (<see cref="SheetLayout"/>, <see cref="GeneratorClips"/>,
/// <see cref="SheetIndex.Facings"/>) and is identical for every run, while
/// <see cref="RunManifest"/> writes what is specific to one: the run id, the tones applied, and
/// the sheets produced.
/// </para>
/// <para>
/// Derived rather than restated, for the same reason <see cref="SheetIndex"/> and
/// <see cref="ClipIndex"/> are: a manifest that repeated these numbers could drift from the remap
/// the baker actually performs.
/// </para>
/// </remarks>
internal static class RunManifestLayouts
{
    /// <summary>
    /// Writes a layout per geometry the run actually produced.
    /// </summary>
    /// <remarks>
    /// Mirrors how the CSVs are written — a curated-only export leaves no <c>clips.csv</c> — so a
    /// manifest never describes geometry that no file in the folder uses. The schema requires at
    /// least one, which an empty run cannot satisfy; callers do not write a manifest for one.
    /// </remarks>
    internal static void Write(Utf8JsonWriter writer, ImmutableArray<SheetRecipe> recipes)
    {
        writer.WriteStartObject(RootNames.LayoutsValueUtf8);

        if (recipes.AsSpan().Any(static recipe => recipe.Geometry is SheetGeometry.Curated))
        {
            WriteCurated(writer);
        }

        if (recipes.AsSpan().Any(static recipe => recipe.Geometry is SheetGeometry.Full))
        {
            WriteFull(writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteCurated(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(LayoutNames.CuratedUtf8);

        writer.WriteNumber(CuratedNames.WidthUtf8, SheetLayout.OutputWidth);
        writer.WriteNumber(CuratedNames.HeightUtf8, SheetLayout.OutputHeight);
        writer.WriteNumber(CuratedNames.CellSizeUtf8, SheetLayout.CellSize);
        writer.WriteNumber(CuratedNames.ColumnsUtf8, SheetLayout.OutputColumns);
        writer.WriteNumber(CuratedNames.RowsUtf8, SheetLayout.OutputRows);

        // The curated index has never carried a playback rate — it lives only in the full-geometry
        // table, which describes the other geometry — so a consumer of a curated sheet had to
        // guess the cadence the art was authored for. Stated here.
        writer.WriteNumber(CuratedNames.FrameDurationMsUtf8, GeneratorClips.FrameDurationMilliseconds);

        writer.WriteStartArray(CuratedNames.FacingsUtf8);

        foreach (var facing in SheetIndex.Facings)
        {
            writer.WriteStringValue(facing);
        }

        writer.WriteEndArray();

        writer.WriteStartArray(CuratedNames.ClipsUtf8);

        for (var clipIndex = 0; clipIndex < SheetLayout.Clips.Length; clipIndex++)
        {
            WriteCuratedClip(writer, clipIndex);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// One curated clip, with its output row stated per facing rather than left to the
    /// <c>clip * 3 + facing</c> formula a consumer would otherwise have to know.
    /// </summary>
    private static void WriteCuratedClip(Utf8JsonWriter writer, int clipIndex)
    {
        var clip = SheetLayout.Clips[clipIndex];

        writer.WriteStartObject();
        writer.WriteString(CuratedClipNames.NameUtf8, clip.Name);
        writer.WriteNumber(CuratedClipNames.FrameCountUtf8, clip.FrameCount);

        // Provenance, not an output column: Curate left-aligns every clip, so its frames occupy
        // output columns 0..FrameCount-1 whatever this says.
        writer.WriteNumber(CuratedClipNames.SourceColumnUtf8, clip.SourceColumn);

        writer.WriteStartObject(CuratedClipNames.RowsUtf8);
        writer.WriteNumber(CuratedRowNames.SouthUtf8, SheetLayout.RowFor(clipIndex, 0));
        writer.WriteNumber(CuratedRowNames.WestUtf8, SheetLayout.RowFor(clipIndex, 1));
        writer.WriteNumber(CuratedRowNames.EastUtf8, SheetLayout.RowFor(clipIndex, 2));
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void WriteFull(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(LayoutNames.FullUtf8);

        writer.WriteNumber(FullNames.WidthUtf8, SheetLayout.SourceWidth);
        writer.WriteNumber(FullNames.HeightUtf8, SheetLayout.SourceHeight);
        writer.WriteNumber(FullNames.CellSizeUtf8, SheetLayout.CellSize);
        writer.WriteNumber(FullNames.ColumnsUtf8, SheetLayout.SourceColumns);
        writer.WriteNumber(FullNames.RowsUtf8, SheetLayout.SourceRows);
        writer.WriteNumber(FullNames.FrameDurationMsUtf8, GeneratorClips.FrameDurationMilliseconds);

        // Fixed for every clip in this geometry — the facing IS the row — which is why it sits on
        // the layout rather than being repeated on each clip as the curated geometry needs.
        writer.WriteStartObject(FullNames.FacingRowsUtf8);
        writer.WriteNumber(FullFacingNames.SouthUtf8, 0);
        writer.WriteNumber(FullFacingNames.WestUtf8, 1);
        writer.WriteNumber(FullFacingNames.EastUtf8, 2);
        writer.WriteNumber(FullFacingNames.NorthUtf8, 3);
        writer.WriteEndObject();

        writer.WriteStartArray(FullNames.ClipsUtf8);

        foreach (var clip in GeneratorClips.All)
        {
            WriteFullClip(writer, clip);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteFullClip(Utf8JsonWriter writer, GeneratorClip clip)
    {
        writer.WriteStartObject();
        writer.WriteString(FullClipNames.NameUtf8, clip.Name);

        // Playback order, which repeats and descends — walk is 1, 2, 1, 0. Written in clip order
        // and never sorted; the schema says so too, because sorting it is the obvious mistake.
        writer.WriteStartArray(FullClipNames.ColumnsUtf8);

        foreach (var column in clip.Frames)
        {
            writer.WriteNumberValue(column);
        }

        writer.WriteEndArray();

        writer.WriteBoolean(FullClipNames.IsRenderedByDefaultUtf8, clip.IsRenderedByDefault);
        writer.WriteBoolean(FullClipNames.ReverseDrawOrderUtf8, clip.ReverseDrawOrder);
        writer.WriteEndObject();
    }
}
