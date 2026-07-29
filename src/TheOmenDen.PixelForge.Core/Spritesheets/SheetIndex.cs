using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Diagnostics;
using CsvHelper;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;

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
    public static int Write(TextWriter writer)
    {
        Guard.IsNotNull(writer);

        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture, leaveOpen: true);

        csv.WriteRecords(Rows);
        csv.Flush();

        return Rows.Length;
    }

    /// <summary>Writes <c>index.csv</c> into an export directory.</summary>
    public static Result<int, BakeFailure> WriteTo(FullPath directory)
    {
        if (!Directory.Exists(directory.Value))
        {
            return new(BakeFailure.OutputDirectoryUnavailable);
        }

        try
        {
            using var writer = new StreamWriter((directory / FileName).Value);

            return Write(writer);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(BakeFailure.OutputWriteFailed);
        }
    }
}
