using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Buffers;
using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// The manifest for a whole batch run: every sheet written, and the slot composition and tone
/// behind it.
/// </summary>
/// <remarks>
/// <para>
/// A cross-product run emits well over a hundred files whose names, however carefully composed,
/// stop being a usable index long before that. This is what maps a baked sheet back to the
/// partials that produced it.
/// </para>
/// <para>
/// Written with the same shape as <see cref="Spritesheets.ClipIndex"/> — the shared
/// <see cref="Csv"/> dialect, a <see cref="Result{T, TError}"/> return and the same narrow
/// exception filter — with the run id set first on every row, so rows from separate runs stay
/// attributable when the files are concatenated.
/// </para>
/// </remarks>
public static class BatchManifest
{
    /// <summary>Name of the manifest written beside a run's sheets.</summary>
    public const string FileName = "sheets.csv";

    private const string RunIdColumn = "RunId";

    /// <summary>
    /// A fresh identifier for one batch run.
    /// </summary>
    /// <remarks>
    /// <see cref="Guid.CreateVersion7()"/>, never <see cref="Guid.NewGuid"/>. A v7 UUID leads with
    /// a millisecond timestamp, so run ids sort chronologically in the manifest and in the log;
    /// v4 is uniformly random and scatters.
    /// </remarks>
    /// <returns>A UUIDv7 whose text form orders by creation time.</returns>
    public static Guid NewRunId() => Guid.CreateVersion7();

    /// <summary>
    /// Describes a batch of baked recipes as manifest rows, one per sheet, in bake order.
    /// </summary>
    /// <param name="recipes">The recipes a run was given, as planned.</param>
    /// <returns>One <see cref="BatchManifestRow"/> per recipe.</returns>
    public static ImmutableArray<BatchManifestRow> RowsFor(ImmutableArray<SheetRecipe> recipes) =>
        [.. recipes.AsSpan().Select(RowFor)];

    /// <summary>
    /// One row: the sheet, and the partial that filled each slot.
    /// </summary>
    /// <param name="recipe">The recipe the sheet was baked from.</param>
    /// <returns>The row, with a blank cell for every slot the recipe left empty.</returns>
    /// <remarks>
    /// <para>
    /// The slot comes from each layer's parent folder rather than from the recipe, which carries
    /// only paths and skin flags by the time it reaches the baker. That folder name <em>is</em> the
    /// slot — see <see cref="Catalog.AssetSlots.FolderName"/> — so the mapping is exact rather than
    /// inferred, and a path from somewhere else simply contributes no column.
    /// </para>
    /// <para>
    /// Staged through an array indexed by slot rather than through ten conditionals, because
    /// <see cref="AssetSlot"/>'s value <em>is</em> its position and a new member would otherwise
    /// need a new branch as well as a new column.
    /// </para>
    /// </remarks>
    public static BatchManifestRow RowFor(SheetRecipe recipe)
    {
        var stems = StemsBySlot(recipe);

        return new()
        {
            Name = recipe.Name,
            File = recipe.Name + SheetWriter.Extension,
            Geometry = recipe.Geometry.ToString(),
            Tone = recipe.Tone.TryGet(out var ramp) ? ramp.Name : string.Empty,
            Shadow = stems[(int)AssetSlot.Shadow],
            BackExtra = stems[(int)AssetSlot.BackExtra],
            BackHair = stems[(int)AssetSlot.BackHair],
            Bottom = stems[(int)AssetSlot.Bottom],
            Top = stems[(int)AssetSlot.Top],
            Head = stems[(int)AssetSlot.Head],
            Hair = stems[(int)AssetSlot.Hair],
            FrontExtra = stems[(int)AssetSlot.FrontExtra],
            Hat = stems[(int)AssetSlot.Hat],
            Weapon = stems[(int)AssetSlot.Weapon],
        };
    }

    /// <summary>
    /// The partial filling each slot, indexed by <see cref="AssetSlot"/>, blank where the recipe
    /// leaves a slot empty.
    /// </summary>
    /// <param name="recipe">The recipe to read layers from.</param>
    /// <returns>An array of <see cref="AssetSlots.DrawOrder"/>'s length, never <see langword="null"/>.</returns>
    /// <remarks>
    /// Shared with <see cref="RunManifest"/> so <c>sheets.csv</c> and <c>manifest.json</c> cannot
    /// disagree about which partial filled which slot — one mapping, two readers.
    /// </remarks>
    internal static string[] StemsBySlot(SheetRecipe recipe)
    {
        Guard.IsNotNull(recipe);

        var stems = new string[AssetSlots.DrawOrder.Length];

        Array.Fill(stems, string.Empty);

        foreach (var layer in recipe.Layers)
        {
            if (Enum.TryParse<AssetSlot>(layer.Path.Parent.Name, ignoreCase: true, out var slot))
            {
                stems[(int)slot] = layer.Path.NameWithoutExtension;
            }
        }

        return stems;
    }

    /// <summary>Writes <c>sheets.csv</c> into an export directory.</summary>
    /// <param name="directory">Where the run's sheets were written.</param>
    /// <param name="runId">The run identifier stamped onto every row, from <see cref="NewRunId"/>.</param>
    /// <param name="rows">One row per sheet, in the order they were baked.</param>
    /// <param name="cancellationToken">Cancels between rows.</param>
    /// <returns>
    /// The row count, or <see cref="BakeFailure.OutputDirectoryUnavailable"/> when the folder is
    /// not there, or <see cref="BakeFailure.OutputWriteFailed"/> when it cannot be written.
    /// </returns>
    public static async Task<Result<int, BakeFailure>> WriteToAsync(
        FullPath directory,
        Guid runId,
        IReadOnlyList<BatchManifestRow> rows,
        CancellationToken cancellationToken = default)
    {
        Guard.IsNotNull(rows);

        if (!Directory.Exists(directory.Value))
        {
            return new(BakeFailure.OutputDirectoryUnavailable);
        }

        try
        {
            await using var writer = AsyncFiles.CreateText(directory / FileName);

            return await WriteAsync(writer, runId, rows, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(BakeFailure.OutputWriteFailed);
        }
    }

    private static async Task<int> WriteAsync(
        TextWriter writer,
        Guid runId,
        IReadOnlyList<BatchManifestRow> rows,
        CancellationToken cancellationToken)
    {
        await using (var csv = Csv.Writer(writer))
        {
            foreach (var row in rows)
            {
                await using var line = csv.NewRow(cancellationToken);

                // Columns are written in the order they are first set and the header is derived
                // from that order, so setting the run id first is all it takes to lead with it.
                line[RunIdColumn].Format(runId, "D");
                line[nameof(row.Name)].Set(row.Name);
                line[nameof(row.File)].Set(row.File);
                line[nameof(row.Geometry)].Set(row.Geometry);
                line[nameof(row.Tone)].Set(row.Tone);
                line[nameof(row.Shadow)].Set(row.Shadow);
                line[nameof(row.BackExtra)].Set(row.BackExtra);
                line[nameof(row.BackHair)].Set(row.BackHair);
                line[nameof(row.Bottom)].Set(row.Bottom);
                line[nameof(row.Top)].Set(row.Top);
                line[nameof(row.Head)].Set(row.Head);
                line[nameof(row.Hair)].Set(row.Hair);
                line[nameof(row.FrontExtra)].Set(row.FrontExtra);
                line[nameof(row.Hat)].Set(row.Hat);
                line[nameof(row.Weapon)].Set(row.Weapon);
            }
        }

        return rows.Count;
    }
}
