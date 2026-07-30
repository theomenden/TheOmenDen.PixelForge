using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using Corvus.Text.Json;
using DotNext;
using Meziantou.Framework;
using Microsoft.IO;
using TheOmenDen.PixelForge.Core.Buffers;
using TheOmenDen.PixelForge.Schema;

using HeroNames = TheOmenDen.PixelForge.Schema.HeroRegistryDocument.Hero.JsonPropertyNames;
using BodyNames = TheOmenDen.PixelForge.Schema.HeroRegistryDocument.Body.JsonPropertyNames;
using RootNames = TheOmenDen.PixelForge.Schema.HeroRegistryDocument.JsonPropertyNames;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// The hero registry: which body owns which directory name, accumulated across every run.
/// </summary>
/// <remarks>
/// <para>
/// The only file this solution reads back as well as writes, and that is what earns it a schema.
/// Hero numbering is stable across runs precisely because the previous registry is parsed before the
/// next one is composed — if <c>villager_01</c> named one body last run and a different one this
/// run, anything referencing it by path would break with no error anywhere.
/// </para>
/// <para>
/// Numbers are never reused. An entry stays even when a later run does not produce that hero,
/// because the alternative is a path quietly coming to mean something else.
/// </para>
/// </remarks>
public static class HeroRegistry
{
    /// <summary>Name of the registry written at the export root.</summary>
    public const string FileName = "heroes.json";

    /// <summary>Name of the schema copied in beside it.</summary>
    public const string SchemaFileName = "pixelforge-heroes-v1.json";

    /// <summary>Name of the spreadsheet view written beside it.</summary>
    public const string CsvFileName = "heroes.csv";

    /// <summary>The schema itself, read once from the assembly.</summary>
    public static string SchemaText { get; } = EmbeddedSchemas.Read(SchemaFileName);

    /// <summary>
    /// Reads the registry an export folder already holds.
    /// </summary>
    /// <param name="directory">The export root.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The entries, empty when there is no registry yet, or
    /// <see cref="PlanFailure.HeroRegistryUnreadable"/> when one is there but cannot be trusted.
    /// </returns>
    /// <remarks>
    /// A missing file is a first run, not a fault. A file that will not parse or fails its schema
    /// is the one case where guessing is dangerous, so it stops the run instead.
    /// </remarks>
    public static async Task<Result<ImmutableArray<HeroEntry>, PlanFailure>> ReadAsync(
        FullPath directory,
        CancellationToken cancellationToken = default)
    {
        var path = directory / FileName;

        if (!File.Exists(path.Value))
        {
            return ImmutableArray<HeroEntry>.Empty;
        }

        try
        {
            var text = await File.ReadAllBytesAsync(path.Value, cancellationToken);

            return Parse(text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(PlanFailure.HeroRegistryUnreadable);
        }
    }

    /// <summary>
    /// Gives every body a directory name, keeping the ones already assigned.
    /// </summary>
    /// <param name="existing">What the registry already holds, from <see cref="ReadAsync"/>.</param>
    /// <param name="keys">The bodies this run produces, from <see cref="LayerPlan.HeroKeys"/>.</param>
    /// <param name="prefix">The slugged archetype new heroes are named after.</param>
    /// <param name="runId">Stamped onto entries this call mints.</param>
    /// <returns>
    /// Every entry the registry should now hold: the existing ones untouched and in order, then any
    /// newly assigned.
    /// </returns>
    /// <remarks>
    /// The high-water mark is per prefix, so each archetype starts at 1 and its heroes sort
    /// adjacently in a flat listing. A body already known keeps its name whatever prefix is typed
    /// now — renaming it would break every path that referenced it.
    /// </remarks>
    public static ImmutableArray<HeroEntry> Assign(
        ImmutableArray<HeroEntry> existing,
        ImmutableArray<HeroKey> keys,
        string prefix,
        Guid runId)
    {
        Guard.IsNotNullOrWhiteSpace(prefix);

        var known = existing.IsDefault ? [] : existing;
        var assigned = new Dictionary<HeroKey, HeroEntry>(known.Length);
        var highest = 0;

        foreach (var entry in known)
        {
            assigned[entry.Key] = entry;

            if (string.Equals(entry.Prefix, prefix, StringComparison.Ordinal) && entry.Number > highest)
            {
                highest = entry.Number;
            }
        }

        var minted = ImmutableArray.CreateBuilder<HeroEntry>();

        foreach (var key in keys.IsDefault ? [] : keys)
        {
            if (assigned.ContainsKey(key))
            {
                continue;
            }

            var entry = new HeroEntry(prefix, ++highest, key, runId);

            assigned[key] = entry;
            minted.Add(entry);
        }

        return [.. known, .. minted];
    }

    /// <summary>The directory name for each body, for handing to <see cref="LayerPlan.Expand"/>.</summary>
    /// <param name="heroes">The registry, after <see cref="Assign"/>.</param>
    /// <returns>One entry per hero, keyed by body.</returns>
    public static IReadOnlyDictionary<HeroKey, string> Labels(ImmutableArray<HeroEntry> heroes)
    {
        var labels = new Dictionary<HeroKey, string>(heroes.IsDefault ? 0 : heroes.Length);

        foreach (var entry in heroes.IsDefault ? [] : heroes)
        {
            labels[entry.Key] = entry.Name;
        }

        return labels;
    }

    /// <summary>
    /// Writes <c>heroes.json</c>, its schema copy, and the <c>heroes.csv</c> view.
    /// </summary>
    /// <param name="directory">The export root.</param>
    /// <param name="heroes">Every entry the registry should hold.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>
    /// The hero count, or <see cref="BakeFailure.OutputDirectoryUnavailable"/>,
    /// <see cref="BakeFailure.HeroRegistrySchemaViolation"/> when the composed document fails its
    /// own schema — in which case nothing is written — or
    /// <see cref="BakeFailure.OutputWriteFailed"/>.
    /// </returns>
    public static async Task<Result<int, BakeFailure>> WriteToAsync(
        FullPath directory,
        ImmutableArray<HeroEntry> heroes,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory.Value))
        {
            return new(BakeFailure.OutputDirectoryUnavailable);
        }

        var entries = heroes.IsDefault ? [] : heroes;

        // Composed and validated before any file is opened, so a schema violation cannot leave a
        // truncated registry behind — which the next run would then refuse to read.
        using var buffer = PooledStreams.New(nameof(HeroRegistry));

        Compose(buffer, entries);

        using (var parsed = ParsedJsonDocument<HeroRegistryDocument>.Parse(Composed(buffer)))
        {
            if (!parsed.RootElement.EvaluateSchema())
            {
                return new(BakeFailure.HeroRegistrySchemaViolation);
            }
        }

        try
        {
            await using (var file = AsyncFiles.Create(directory / FileName))
            {
                await file.WriteAsync(Composed(buffer), cancellationToken);
            }

            await File.WriteAllTextAsync(
                (directory / SchemaFileName).Value, SchemaText, cancellationToken);

            await WriteCsvAsync(directory, entries, cancellationToken);

            return entries.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(BakeFailure.OutputWriteFailed);
        }
    }

    /// <summary>Parses and validates registry bytes.</summary>
    private static Result<ImmutableArray<HeroEntry>, PlanFailure> Parse(ReadOnlyMemory<byte> text)
    {
        try
        {
            using var parsed = ParsedJsonDocument<HeroRegistryDocument>.Parse(text);

            if (!parsed.RootElement.EvaluateSchema())
            {
                return new(PlanFailure.HeroRegistryUnreadable);
            }

            var entries = ImmutableArray.CreateBuilder<HeroEntry>();

            foreach (var hero in parsed.RootElement.Heroes.EnumerateArray())
            {
                entries.Add(new(
                    (string)hero.Prefix,
                    (int)hero.Number,
                    // BodyValue, not Body: the generator suffixes a property whose name would
                    // collide with a nested type, exactly as it does for the manifest's SlotsValue.
                    new((string)hero.BodyValue.Bottom, (string)hero.BodyValue.Top, (string)hero.BodyValue.Head),
                    (Guid)hero.AssignedInRun));
            }

            return entries.ToImmutable();
        }
        catch (JsonException)
        {
            // Corvus.Text.Json.JsonException, NOT System.Text.Json's — this document is parsed
            // through Corvus's stack end to end, and the two hierarchies are unrelated: Corvus's
            // derives straight from Exception. Catching the BCL's lets a malformed registry escape
            // as a crash instead of arriving as a failure value, which is what a test caught.
            // The concrete JsonReaderException it throws is internal, so this base is the hook.
            return new(PlanFailure.HeroRegistryUnreadable);
        }
    }

    /// <summary>The composed bytes, as memory over the pooled buffer rather than a copy of it.</summary>
    private static ReadOnlyMemory<byte> Composed(RecyclableMemoryStream buffer) =>
        buffer.GetBuffer().AsMemory(0, (int)buffer.Length);

    private static void Compose(Stream destination, ImmutableArray<HeroEntry> heroes)
    {
        using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        writer.WriteString(RootNames.SchemaUtf8, SchemaFileName);

        // The schema's own constant, written as the typed value rather than through a string, so
        // the bytes on disk come from the contract itself.
        writer.WritePropertyName(RootNames.SchemaVersionUtf8);
        HeroRegistryDocument.SchemaVersionEntity.ConstInstance.WriteTo(writer);

        writer.WriteStartArray(RootNames.HeroesUtf8);

        foreach (var hero in heroes)
        {
            WriteHero(writer, hero);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteHero(Utf8JsonWriter writer, HeroEntry hero)
    {
        writer.WriteStartObject();

        writer.WriteString(HeroNames.NameUtf8, hero.Name);
        writer.WriteString(HeroNames.PrefixUtf8, hero.Prefix);
        writer.WriteNumber(HeroNames.NumberUtf8, hero.Number);

        writer.WriteStartObject(HeroNames.BodyValueUtf8);
        writer.WriteString(BodyNames.BottomUtf8, hero.Key.Bottom);
        writer.WriteString(BodyNames.TopUtf8, hero.Key.Top);
        writer.WriteString(BodyNames.HeadUtf8, hero.Key.Head);
        writer.WriteEndObject();

        writer.WriteString(HeroNames.AssignedInRunUtf8, hero.AssignedInRun);

        writer.WriteEndObject();
    }

    /// <summary>
    /// The spreadsheet view. Write-only — nothing reads it back, exactly as nothing reads back
    /// <c>sheets.csv</c>.
    /// </summary>
    private static async Task WriteCsvAsync(
        FullPath directory,
        ImmutableArray<HeroEntry> heroes,
        CancellationToken cancellationToken)
    {
        await using var text = AsyncFiles.CreateText(directory / CsvFileName);
        await using var csv = Csv.Writer(text);

        foreach (var hero in heroes)
        {
            await using var row = csv.NewRow(cancellationToken);

            row["Hero"].Set(hero.Name);
            row["Prefix"].Set(hero.Prefix);
            row["Number"].Format(hero.Number);
            row["Bottom"].Set(hero.Key.Bottom);
            row["Top"].Set(hero.Key.Top);
            row["Head"].Set(hero.Key.Head);
            row["AssignedInRun"].Format(hero.AssignedInRun, "D");
        }
    }
}
