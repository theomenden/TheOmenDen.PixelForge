using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;
using nietras.SeparatedValues;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Buffers;

namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// Loads and saves custom ramps as CSV.
/// <para>
/// The built-in ramps are never written here — <see cref="SkinRamps.All"/> is the contract and
/// stays in code. This store holds only what a user added, and the app concatenates the two.
/// </para>
/// <para>
/// The file path is injected so the app can pass LocalState and tests can pass a
/// <see cref="TemporaryDirectory"/>. Read and write are static and take a
/// <see cref="TextReader"/>/<see cref="TextWriter"/>, so the format is testable with no
/// filesystem at all.
/// </para>
/// </summary>
public sealed class RampStore(FullPath file)
{
    private const string NameColumn = "Name";

    private const string IsHumanColumn = "IsHuman";

    /// <summary>
    /// The step columns, darkest first. These names <em>are</em> the file format, so they are
    /// written out rather than generated from <see cref="SkinRamps.StepCount"/>; the round-trip
    /// tests are what hold the two in step.
    /// </summary>
    private static readonly string[] StepColumns = ["Step1", "Step2", "Step3", "Step4", "Step5"];

    public FullPath File => file;

    /// <summary>Reads every ramp in <paramref name="reader"/>, or names the first thing wrong.</summary>
    /// <param name="reader">The source, left open.</param>
    /// <param name="cancellationToken">Cancels between rows.</param>
    /// <returns>The ramps in file order, or the failure that stopped the read.</returns>
    /// <remarks>
    /// Enumerated through <see cref="SepReader.GetAsyncEnumerator"/> by hand rather than with
    /// <c>await foreach</c>, which has no syntax for passing a token: <c>WithCancellation</c> is an
    /// extension on <see cref="IAsyncEnumerable{T}"/> whose <c>T</c> is not declared
    /// <c>allows ref struct</c>, and <see cref="SepReader.Row"/> is one.
    /// </remarks>
    public static async Task<Result<ImmutableArray<SkinRamp>, RampFailure>> ReadAsync(
        TextReader reader,
        CancellationToken cancellationToken = default)
    {
        Guard.IsNotNull(reader);

        var ramps = ImmutableArray.CreateBuilder<SkinRamp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var csv = await Csv.ReaderAsync(reader, cancellationToken);

            await using var rows = csv.GetAsyncEnumerator(cancellationToken);

            while (await rows.MoveNextAsync())
            {
                var converted = ToRamp(rows.Current);

                if (!converted.TryGet(out var ramp))
                {
                    return new(converted.Error);
                }

                if (!seen.Add(ramp.Name))
                {
                    return new(RampFailure.DuplicateName);
                }

                ramps.Add(ramp);
            }
        }
        catch (InvalidDataException)
        {
            // Sep reports structural faults — a row whose column count disagrees with the header,
            // an unterminated quote — during enumeration, so the whole loop is inside the try.
            return new(RampFailure.StoreMalformed);
        }

        return ramps.DrainToImmutable();
    }

    /// <summary>Writes <paramref name="ramps"/> and reports how many landed.</summary>
    /// <param name="writer">The destination, left open.</param>
    /// <param name="ramps">The ramps to write, in order.</param>
    /// <param name="cancellationToken">Cancels between rows.</param>
    /// <returns>The count, or the first ramp fault found — in which case nothing is written.</returns>
    public static async Task<Result<int, RampFailure>> WriteAsync(
        TextWriter writer,
        IReadOnlyList<SkinRamp> ramps,
        CancellationToken cancellationToken = default)
    {
        Guard.IsNotNull(writer);
        Guard.IsNotNull(ramps);

        // Everything is checked before anything is written: a rejected ramp halfway through would
        // otherwise leave a truncated file behind under Save.
        foreach (var ramp in ramps)
        {
            if (ramp.Steps.Length != SkinRamps.StepCount)
            {
                return new(RampFailure.WrongStepCount);
            }

            if (string.IsNullOrWhiteSpace(ramp.Name))
            {
                return new(RampFailure.NameEmpty);
            }
        }

        await using (var csv = Csv.Writer(writer))
        {
            foreach (var ramp in ramps)
            {
                await using var line = csv.NewRow(cancellationToken);

                line[NameColumn].Set(ramp.Name);

                // bool is ISpanParsable but not ISpanFormattable, so Format does not accept it.
                line[IsHumanColumn].Set(ramp.IsHuman ? bool.TrueString : bool.FalseString);

                for (var step = 0; step < StepColumns.Length; step++)
                {
                    line[StepColumns[step]].Set(RampConversions.Hex(ramp.Steps[step]));
                }
            }
        }

        return ramps.Count;
    }

    /// <summary>
    /// Reads one row into a ramp, or names the reason it could not be.
    /// </summary>
    /// <param name="row">The row to read. A column the header does not carry is malformed input.</param>
    /// <returns>The ramp, or the failure that stopped it.</returns>
    /// <remarks>
    /// Takes the row rather than seven loose fields — past six parameters the arguments are a type
    /// that has not been named, and here that type already exists as
    /// <see cref="SepReader.Row"/>.
    /// <para>
    /// One deliberate narrowing against the CsvHelper original: <c>IsHuman</c> now parses through
    /// <see cref="bool"/>, which takes <c>True</c>/<c>False</c> in any casing but no longer the
    /// <c>yes</c>/<c>1</c> spellings CsvHelper also accepted. Nothing writes those, and a hand-edit
    /// that uses one now fails loudly rather than reading as <see langword="false"/>.
    /// </para>
    /// </remarks>
    private static Result<SkinRamp, RampFailure> ToRamp(SepReader.Row row)
    {
        if (!row.TryGet(NameColumn, out var name) || !row.TryGet(IsHumanColumn, out var isHuman))
        {
            return new(RampFailure.StoreMalformed);
        }

        var text = name.ToString().Trim();

        if (text.Length is 0)
        {
            return new(RampFailure.NameEmpty);
        }

        if (!isHuman.TryParse<bool>(out var human))
        {
            return new(RampFailure.StoreMalformed);
        }

        var steps = ImmutableArray.CreateBuilder<SKColor>(StepColumns.Length);

        foreach (var column in StepColumns)
        {
            if (!row.TryGet(column, out var hex)
                || !RampConversions.TryParseHex(hex.ToString(), out var color))
            {
                return new(RampFailure.StoreMalformed);
            }

            steps.Add(color);
        }

        return new SkinRamp
        {
            Name = text,
            IsHuman = human,
            Steps = steps.MoveToImmutable(),
        };
    }

    /// <summary>A missing file is an empty set, not a failure — first run is the normal case.</summary>
    /// <param name="cancellationToken">Cancels between rows.</param>
    /// <returns>The stored ramps, or the failure that stopped the read.</returns>
    public async Task<Result<ImmutableArray<SkinRamp>, RampFailure>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!System.IO.File.Exists(file.Value))
        {
            return ImmutableArray<SkinRamp>.Empty;
        }

        try
        {
            using var reader = AsyncFiles.OpenText(file);

            return await ReadAsync(reader, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(RampFailure.StoreUnreadable);
        }
    }

    /// <summary>Writes <paramref name="ramps"/> to <see cref="File"/>, creating the folder if needed.</summary>
    /// <param name="ramps">The ramps to store, in order.</param>
    /// <param name="cancellationToken">Cancels between rows.</param>
    /// <returns>The count, or the failure that stopped the write.</returns>
    public async Task<Result<int, RampFailure>> SaveAsync(
        IReadOnlyList<SkinRamp> ramps,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parent = file.Parent;

            if (!parent.IsEmpty)
            {
                Directory.CreateDirectory(parent.Value);
            }

            await using var writer = AsyncFiles.CreateText(file);

            return await WriteAsync(writer, ramps, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(RampFailure.StoreUnwritable);
        }
    }
}
