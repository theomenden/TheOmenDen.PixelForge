using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Buffers;

namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>
/// The manifest that makes an exported sheet self-describing.
/// <para>
/// A curated sheet is an atlas: 24 rows of 5 cells with no in-band clue that rows 3-5 are
/// <c>idle</c> south, west and east. Shipping the row map beside the art is the difference
/// between an atlas a consumer can load and one it has to be told about out of band.
/// </para>
/// <para>
/// Derived from <see cref="SheetLayout"/> rather than restated, so the manifest cannot drift
/// from the remap the baker actually performs.
/// </para>
/// </summary>
public static class SheetIndex
{
    public const string FileName = "index.csv";

    /// <summary>Source row order, north dropped — see <see cref="SheetLayout.FacingCount"/>.</summary>
    public static ImmutableArray<string> Facings { get; } = ["south", "west", "east"];

    public static ImmutableArray<SheetIndexRow> Rows { get; } = Build();

    private static ImmutableArray<SheetIndexRow> Build()
    {
        var rows = ImmutableArray.CreateBuilder<SheetIndexRow>(SheetLayout.ClipCount * SheetLayout.FacingCount);

        for (var clipIndex = 0; clipIndex < SheetLayout.Clips.Length; clipIndex++)
        {
            var clip = SheetLayout.Clips[clipIndex];

            for (var facing = 0; facing < SheetLayout.FacingCount; facing++)
            {
                rows.Add(new()
                {
                    Clip = clip.Name,
                    Facing = Facings[facing],
                    Row = SheetLayout.RowFor(clipIndex, facing),
                    FrameCount = clip.FrameCount,
                    FirstColumn = clip.SourceColumn,
                    CellSize = SheetLayout.CellSize,
                });
            }
        }

        return rows.ToImmutable();
    }

    /// <summary>Writes the manifest and returns the row count.</summary>
    /// <param name="writer">The destination, left open.</param>
    /// <param name="cancellationToken">Cancels between rows.</param>
    /// <returns>The number of rows written, which is always <see cref="Rows"/>'s length.</returns>
    public static async Task<int> WriteAsync(TextWriter writer, CancellationToken cancellationToken = default)
    {
        Guard.IsNotNull(writer);

        await using (var csv = Csv.Writer(writer))
        {
            foreach (var row in Rows)
            {
                await using var line = csv.NewRow(cancellationToken);

                line[nameof(row.Clip)].Set(row.Clip);
                line[nameof(row.Facing)].Set(row.Facing);
                line[nameof(row.Row)].Format(row.Row);
                line[nameof(row.FrameCount)].Format(row.FrameCount);
                line[nameof(row.FirstColumn)].Format(row.FirstColumn);
                line[nameof(row.CellSize)].Format(row.CellSize);
            }
        }

        return Rows.Length;
    }

    /// <summary>Writes <c>index.csv</c> into an export directory.</summary>
    /// <param name="directory">Where the run's sheets were written.</param>
    /// <param name="cancellationToken">Cancels between rows.</param>
    /// <returns>
    /// The row count, or <see cref="BakeFailure.OutputDirectoryUnavailable"/> when the folder is
    /// not there, or <see cref="BakeFailure.OutputWriteFailed"/> when it cannot be written.
    /// </returns>
    public static async Task<Result<int, BakeFailure>> WriteToAsync(
        FullPath directory,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory.Value))
        {
            return new(BakeFailure.OutputDirectoryUnavailable);
        }

        try
        {
            await using var writer = AsyncFiles.CreateText(directory / FileName);

            return await WriteAsync(writer, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(BakeFailure.OutputWriteFailed);
        }
    }
}
