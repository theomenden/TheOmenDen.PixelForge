using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Diagnostics;
using CsvHelper;
using DotNext;
using Meziantou.Framework;

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
    public FullPath File => file;

    public static Result<ImmutableArray<SkinRamp>, RampFailure> Read(TextReader reader)
    {
        Guard.IsNotNull(reader);

        List<RampRow> rows;

        try
        {
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture, leaveOpen: true);

            rows = [.. csv.GetRecords<RampRow>()];
        }
        catch (CsvHelperException)
        {
            return new(RampFailure.StoreMalformed);
        }

        var ramps = ImmutableArray.CreateBuilder<SkinRamp>(rows.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var converted = row.ToRamp();

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

        return ramps.ToImmutable();
    }

    public static Result<int, RampFailure> Write(TextWriter writer, IReadOnlyList<SkinRamp> ramps)
    {
        Guard.IsNotNull(writer);
        Guard.IsNotNull(ramps);

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

        var rows = new List<RampRow>(ramps.Count);

        foreach (var ramp in ramps)
        {
            rows.Add(ramp.ToRow());
        }

        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture, leaveOpen: true);

        csv.WriteRecords(rows);
        csv.Flush();

        return ramps.Count;
    }

    /// <summary>A missing file is an empty set, not a failure — first run is the normal case.</summary>
    public Result<ImmutableArray<SkinRamp>, RampFailure> Load()
    {
        if (!System.IO.File.Exists(file.Value))
        {
            return ImmutableArray<SkinRamp>.Empty;
        }

        try
        {
            using var reader = new StreamReader(file.Value);

            return Read(reader);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(RampFailure.StoreUnreadable);
        }
    }

    public Result<int, RampFailure> Save(IReadOnlyList<SkinRamp> ramps)
    {
        try
        {
            var parent = file.Parent;

            if (!parent.IsEmpty)
            {
                Directory.CreateDirectory(parent.Value);
            }

            using var writer = new StreamWriter(file.Value);

            return Write(writer, ramps);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(RampFailure.StoreUnwritable);
        }
    }
}
