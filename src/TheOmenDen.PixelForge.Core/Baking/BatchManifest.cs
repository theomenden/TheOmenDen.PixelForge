using System.Globalization;
using CommunityToolkit.Diagnostics;
using CsvHelper;
using DotNext;
using Meziantou.Framework;

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
/// Written with the same shape as <see cref="Spritesheets.ClipIndex"/> — <see cref="CsvWriter"/>
/// over <see cref="CultureInfo.InvariantCulture"/>, a <see cref="Result{T, TError}"/> return and
/// the same narrow exception filter — with the run id prepended as the first column so rows from
/// separate runs stay attributable when the files are concatenated.
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

    /// <summary>Writes <c>sheets.csv</c> into an export directory.</summary>
    /// <param name="directory">Where the run's sheets were written.</param>
    /// <param name="runId">The run identifier stamped onto every row, from <see cref="NewRunId"/>.</param>
    /// <param name="rows">One row per sheet, in the order they were baked.</param>
    /// <returns>
    /// The row count, or <see cref="BakeFailure.OutputDirectoryUnavailable"/> when the folder is
    /// not there, or <see cref="BakeFailure.OutputWriteFailed"/> when it cannot be written.
    /// </returns>
    public static Result<int, BakeFailure> WriteTo(
        FullPath directory,
        Guid runId,
        IReadOnlyList<BatchManifestRow> rows)
    {
        Guard.IsNotNull(rows);

        if (!Directory.Exists(directory.Value))
        {
            return new(BakeFailure.OutputDirectoryUnavailable);
        }

        try
        {
            using var writer = new StreamWriter((directory / FileName).Value);

            return Write(writer, runId, rows);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(BakeFailure.OutputWriteFailed);
        }
    }

    private static int Write(TextWriter writer, Guid runId, IReadOnlyList<BatchManifestRow> rows)
    {
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture, leaveOpen: true);

        // WriteField before WriteHeader/WriteRecord appends to the record being built, which is
        // how the run id lands in column one without duplicating every slot property into a
        // wrapper record that would then need keeping in step with BatchManifestRow.
        csv.WriteField(RunIdColumn);
        csv.WriteHeader<BatchManifestRow>();
        csv.NextRecord();

        var identifier = runId.ToString("D", CultureInfo.InvariantCulture);

        foreach (var row in rows)
        {
            csv.WriteField(identifier);
            csv.WriteRecord(row);
            csv.NextRecord();
        }

        csv.Flush();

        return rows.Count;
    }
}
