using CsvHelper.Configuration.Attributes;

namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// One ramp as a CSV row. Steps are <c>#RRGGBB</c>, darkest first — the same literals
/// <see cref="SkinRamps"/> is authored in, so a saved file diffs directly against the source.
/// </summary>
public sealed record RampRow
{
    public string Name { get; init; } = string.Empty;

    public bool IsHuman { get; init; }

    public string Step1 { get; init; } = string.Empty;

    public string Step2 { get; init; } = string.Empty;

    public string Step3 { get; init; } = string.Empty;

    public string Step4 { get; init; } = string.Empty;

    public string Step5 { get; init; } = string.Empty;

    /// <summary>
    /// Convenience for <see cref="RampConversions"/>. Ignored by CsvHelper — a gettable property
    /// otherwise becomes a phantom "Steps" column on write and a bind target on read.
    /// </summary>
    [Ignore]
    public string[] Steps => [Step1, Step2, Step3, Step4, Step5];
}
