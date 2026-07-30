using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using Corvus.Text.Json;
using DotNext;
using Meziantou.Framework;
using Microsoft.IO;
using TheOmenDen.PixelForge.Core.Buffers;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Schema;

using RootNames = TheOmenDen.PixelForge.Schema.LoadoutDocument.JsonPropertyNames;
using SlotNames = TheOmenDen.PixelForge.Schema.LoadoutDocument.Slots.JsonPropertyNames;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Writes a class's equipment pool: <c>loadouts/&lt;class&gt;.json</c>, and the
/// <c>classes.csv</c> view over every class the folder holds.
/// </summary>
/// <remarks>
/// <para>
/// A loadout names <em>which</em> attachment layers a class offers per slot. It holds no pixels and
/// references no hero, because an attachment is tone-independent and stacks onto any body — which
/// is why loadouts live once at the export root rather than once under every hero.
/// </para>
/// <para>
/// Several stems in one slot is a <em>pool</em>, not a kit worn all at once: a slot draws at most
/// one layer, and the consumer picks per character. That is what <c>!equip</c> against owned
/// inventory wants.
/// </para>
/// </remarks>
public static class LoadoutWriter
{
    /// <summary>Where loadouts go, under the export root.</summary>
    public const string Folder = "loadouts";

    /// <summary>Name of the schema copied in beside them.</summary>
    public const string SchemaFileName = "pixelforge-loadouts-v1.json";

    /// <summary>Name of the spreadsheet view written at the root.</summary>
    public const string CsvFileName = "classes.csv";

    /// <summary>The schema itself, read once from the assembly.</summary>
    public static string SchemaText { get; } = EmbeddedSchemas.Read(SchemaFileName);

    /// <summary>
    /// The schema property name for each optional <see cref="AssetSlot"/>, indexed by slot.
    /// </summary>
    /// <remarks>
    /// From the generated constants rather than restated, so a renamed schema property breaks the
    /// build here instead of silently writing a key no consumer reads. The required trio is blank:
    /// bottom, top and head make a hero, not a loadout, and the schema forbids them.
    /// </remarks>
    private static readonly ImmutableArray<string> SlotKeys = BuildSlotKeys();

    private static ImmutableArray<string> BuildSlotKeys()
    {
        var keys = new string[AssetSlots.DrawOrder.Length];

        Array.Fill(keys, string.Empty);

        keys[(int)AssetSlot.Shadow] = SlotNames.Shadow;
        keys[(int)AssetSlot.BackExtra] = SlotNames.BackExtra;
        keys[(int)AssetSlot.BackHair] = SlotNames.BackHair;
        keys[(int)AssetSlot.Hair] = SlotNames.Hair;
        keys[(int)AssetSlot.FrontExtra] = SlotNames.FrontExtra;
        keys[(int)AssetSlot.Hat] = SlotNames.Hat;
        keys[(int)AssetSlot.Weapon] = SlotNames.Weapon;

        return [.. keys];
    }

    /// <summary>
    /// The stems a selection offers for each optional slot, indexed by <see cref="AssetSlot"/>.
    /// </summary>
    /// <param name="selections">What is ticked, one entry per slot.</param>
    /// <returns>
    /// An array of <see cref="AssetSlots.DrawOrder"/>'s length, empty where a slot contributes
    /// nothing. <c>(none)</c> contributes nothing — the absence of a hat is not equipment.
    /// </returns>
    public static ImmutableArray<string>[] PoolOf(ImmutableArray<SlotSelection> selections)
    {
        var pool = new ImmutableArray<string>[AssetSlots.DrawOrder.Length];

        Array.Fill(pool, []);

        if (selections.IsDefaultOrEmpty)
        {
            return pool;
        }

        foreach (var selection in selections)
        {
            if (AssetSlots.IsRequired(selection.Slot) || selection.Choices.IsDefaultOrEmpty)
            {
                continue;
            }

            var stems = ImmutableArray.CreateBuilder<string>();

            foreach (var choice in selection.Choices)
            {
                if (choice.TryGet(out var partial))
                {
                    stems.Add(partial.Stem);
                }
            }

            pool[(int)selection.Slot] = stems.ToImmutable();
        }

        return pool;
    }

    /// <summary>Whether a pool holds anything at all.</summary>
    /// <param name="pool">From <see cref="PoolOf"/>.</param>
    /// <returns><see langword="true"/> when at least one slot offers a stem.</returns>
    public static bool IsEmpty(ImmutableArray<string>[] pool)
    {
        Guard.IsNotNull(pool);

        foreach (var stems in pool)
        {
            if (!stems.IsDefaultOrEmpty)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Writes one class's loadout, its schema copy, and rebuilds <c>classes.csv</c>.
    /// </summary>
    /// <param name="directory">The export root.</param>
    /// <param name="className">The slugged class name.</param>
    /// <param name="pool">The equipment this class offers, from <see cref="PoolOf"/>.</param>
    /// <param name="runId">Stamped into the document.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>
    /// The number of slots the class fills, or the failure that stopped it.
    /// </returns>
    public static async Task<Result<int, BakeFailure>> WriteToAsync(
        FullPath directory,
        string className,
        ImmutableArray<string>[] pool,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        Guard.IsNotNullOrWhiteSpace(className);
        Guard.IsNotNull(pool);

        if (!Directory.Exists(directory.Value))
        {
            return new(BakeFailure.OutputDirectoryUnavailable);
        }

        // Composed and validated before any file is opened, so a schema violation cannot leave a
        // truncated loadout behind for a consumer to trust.
        using var buffer = PooledStreams.New(nameof(LoadoutWriter));

        Compose(buffer, className, pool, runId);

        using (var parsed = ParsedJsonDocument<LoadoutDocument>.Parse(Composed(buffer)))
        {
            if (!parsed.RootElement.EvaluateSchema())
            {
                return new(BakeFailure.HeroRegistrySchemaViolation);
            }
        }

        try
        {
            var folder = directory / Folder;

            Directory.CreateDirectory(folder.Value);

            await using (var file = AsyncFiles.Create(folder / (className + ".json")))
            {
                await file.WriteAsync(Composed(buffer), cancellationToken);
            }

            await File.WriteAllTextAsync(
                (directory / SchemaFileName).Value, SchemaText, cancellationToken);

            await WriteCsvAsync(directory, cancellationToken);

            return Filled(pool);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return new(BakeFailure.OutputWriteFailed);
        }
    }

    private static int Filled(ImmutableArray<string>[] pool)
    {
        var filled = 0;

        foreach (var stems in pool)
        {
            if (!stems.IsDefaultOrEmpty)
            {
                filled++;
            }
        }

        return filled;
    }

    /// <summary>The composed bytes, as memory over the pooled buffer rather than a copy of it.</summary>
    private static ReadOnlyMemory<byte> Composed(RecyclableMemoryStream buffer) =>
        buffer.GetBuffer().AsMemory(0, (int)buffer.Length);

    private static void Compose(
        Stream destination,
        string className,
        ImmutableArray<string>[] pool,
        Guid runId)
    {
        using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        // Loadouts sit one directory down, so the schema beside them is a level up.
        writer.WriteString(RootNames.SchemaUtf8, "../" + SchemaFileName);

        writer.WritePropertyName(RootNames.SchemaVersionUtf8);
        LoadoutDocument.SchemaVersionEntity.ConstInstance.WriteTo(writer);

        writer.WriteString(RootNames.ClassUtf8, className);

        writer.WriteStartObject(RootNames.SlotsValueUtf8);

        foreach (var slot in AssetSlots.DrawOrder)
        {
            var stems = pool[(int)slot];

            if (stems.IsDefaultOrEmpty || SlotKeys[(int)slot].Length is 0)
            {
                continue;
            }

            writer.WriteStartArray(SlotKeys[(int)slot]);

            foreach (var stem in stems)
            {
                writer.WriteStringValue(stem);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();

        writer.WriteString(RootNames.AssignedInRunUtf8, runId);

        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>
    /// Rebuilds <c>classes.csv</c> from every loadout the folder holds, so the view describes all
    /// of them rather than only the class this run wrote.
    /// </summary>
    /// <remarks>
    /// The walk is materialised before the writing loop begins. ZLinq's enumerator is a
    /// <see langword="ref"/> <see langword="struct"/> and cannot cross an <c>await</c> — the
    /// compiler says so outright, which is the good version of this trap.
    /// </remarks>
    private static async Task WriteCsvAsync(FullPath directory, CancellationToken cancellationToken)
    {
        var folder = new DirectoryInfo((directory / Folder).Value);

        if (!folder.Exists)
        {
            return;
        }

        var files = Loadouts(folder);

        await using var text = AsyncFiles.CreateText(directory / CsvFileName);
        await using var csv = Csv.Writer(text);

        foreach (var file in files)
        {
            await WriteRowAsync(csv, file, cancellationToken);
        }
    }

    /// <summary>Every loadout document in the folder, materialised so the caller may await.</summary>
    /// <remarks>
    /// ZLinq.FileSystem's value-enumerable walk, this project's replacement for
    /// <c>Directory.EnumerateFiles</c> + LINQ.
    /// </remarks>
    private static ImmutableArray<FullPath> Loadouts(DirectoryInfo folder)
    {
        var files = ImmutableArray.CreateBuilder<FullPath>();

        foreach (var entry in folder.Children())
        {
            if (entry is FileInfo file && file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(FullPath.FromPath(file.FullName));
            }
        }

        return files.ToImmutable();
    }

    private static async Task WriteRowAsync(
        nietras.SeparatedValues.SepWriter csv,
        FullPath path,
        CancellationToken cancellationToken)
    {
        using var parsed = ParsedJsonDocument<LoadoutDocument>.Parse(
            await File.ReadAllBytesAsync(path.Value, cancellationToken));

        if (!parsed.RootElement.EvaluateSchema())
        {
            return;
        }

        var loadout = parsed.RootElement;

        await using var row = csv.NewRow(cancellationToken);

        row["Class"].Set((string)loadout.Class);

        foreach (var slot in AssetSlots.DrawOrder)
        {
            if (SlotKeys[(int)slot].Length is 0)
            {
                continue;
            }

            // The slot's member name, not FolderName's lowercase form: sheets.csv and heroes.csv
            // both head their columns in PascalCase, and one file spelling them differently is the
            // kind of thing a spreadsheet formula trips over.
            row[slot.ToString()].Set(Joined(loadout.SlotsValue, slot));
        }

        row["AssignedInRun"].Format((Guid)loadout.AssignedInRun, "D");
    }

    /// <summary>
    /// One slot's stems, joined with <c>;</c> so the comma dialect in <see cref="Csv"/> is never
    /// stressed, or blank when the class does not use that slot.
    /// </summary>
    /// <remarks>
    /// A switch over typed properties rather than a lookup by key: the generated document exposes
    /// one accessor per slot and no dynamic indexer, so a renamed schema property breaks the build
    /// here instead of silently reading blank.
    /// </remarks>
    private static string Joined(LoadoutDocument.Slots slots, AssetSlot slot)
    {
        var stems = slot switch
        {
            AssetSlot.Shadow => slots.Shadow,
            AssetSlot.BackExtra => slots.BackExtra,
            AssetSlot.BackHair => slots.BackHair,
            AssetSlot.Hair => slots.Hair,
            AssetSlot.FrontExtra => slots.FrontExtra,
            AssetSlot.Hat => slots.Hat,
            AssetSlot.Weapon => slots.Weapon,
            _ => default,
        };

        if (stems.ValueKind is not JsonValueKind.Array)
        {
            return string.Empty;
        }

        var values = new List<string>();

        foreach (var stem in stems.EnumerateArray())
        {
            values.Add((string)stem);
        }

        return string.Join(';', values);
    }
}
