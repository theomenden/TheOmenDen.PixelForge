using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Buffers;

namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>
/// The manifest that makes a <see cref="SheetGeometry.Full"/> sheet self-describing.
/// </summary>
/// <remarks>
/// <para>
/// The full-geometry counterpart of <see cref="SheetIndex"/>. It describes the source sheet rather
/// than a remap of it, so it carries all twelve generator animations on all four facings —
/// including the ones the curated contract drops.
/// </para>
/// <para>
/// Derived from <see cref="GeneratorClips"/> rather than restated, so the manifest cannot drift
/// from the table the bake is built on. In particular the rows carry
/// <see cref="GeneratorClip.Frames"/> in playback order, which repeats and descends — <c>walk</c>
/// is columns 1, 2, 1, 0 — so a consumer must never re-sort them by
/// <see cref="ClipIndexRow.SourceColumn"/>.
/// </para>
/// </remarks>
public static class ClipIndex
{
    /// <summary>Name of the manifest written beside full-geometry sheets.</summary>
    public const string FileName = "clips.csv";

    /// <summary>Every clip, facing and frame, in declaration order.</summary>
    public static ImmutableArray<ClipIndexRow> Rows { get; } = Build();

    private static ImmutableArray<ClipIndexRow> Build()
    {
        var rows = ImmutableArray.CreateBuilder<ClipIndexRow>();

        foreach (var clip in GeneratorClips.All)
        {
            for (var facing = 0; facing < GeneratorClips.Facings.Length; facing++)
            {
                for (var frame = 0; frame < clip.Frames.Length; frame++)
                {
                    rows.Add(new()
                    {
                        Clip = clip.Name,
                        Facing = GeneratorClips.Facings[facing],
                        SourceRow = facing,
                        FrameIndex = frame,
                        SourceColumn = clip.Frames[frame],
                        CellSize = SheetLayout.CellSize,
                        FrameDurationMs = GeneratorClips.FrameDurationMilliseconds,
                        ReverseDrawOrder = clip.ReverseDrawOrder,
                    });
                }
            }
        }

        return rows.ToImmutable();
    }

    /// <summary>Writes the manifest and reports how many rows landed.</summary>
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
                line[nameof(row.SourceRow)].Format(row.SourceRow);
                line[nameof(row.FrameIndex)].Format(row.FrameIndex);
                line[nameof(row.SourceColumn)].Format(row.SourceColumn);
                line[nameof(row.CellSize)].Format(row.CellSize);
                line[nameof(row.FrameDurationMs)].Format(row.FrameDurationMs);

                // bool is ISpanParsable but not ISpanFormattable, so Format does not accept it.
                // The literals are the same "True"/"False" bool.Parse reads back.
                line[nameof(row.ReverseDrawOrder)]
                    .Set(row.ReverseDrawOrder ? bool.TrueString : bool.FalseString);
            }
        }

        return Rows.Length;
    }

    /// <summary>Writes <c>clips.csv</c> into an export directory.</summary>
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
