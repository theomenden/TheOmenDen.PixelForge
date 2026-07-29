using System.Collections.Immutable;
using System.Globalization;
using ColorHelper;
using DotNext;
using SkiaSharp;

namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// Conversion between a ramp and its CSV row.
/// <para>
/// Hand-written rather than generated. Mapperly is the project default for object mapping, but
/// this is a shape change — a five-element <see cref="ImmutableArray{T}"/> to five named hex
/// columns — plus formatting, so a Mapperly mapping would need a user-implemented method whose
/// body is this entire conversion. The generator would add indirection and emit nothing.
/// </para>
/// </summary>
// CA1708 false positive: the compiler's synthesized names for two `extension(...)` blocks in
// one class collide by case only. Reproduced in isolation with two trivial extension blocks
// over unrelated types — same failure, nothing to do with SkinRamp/RampRow. No real naming
// collision exists in the public API.
#pragma warning disable CA1708
public static class RampConversions
{
    extension(SkinRamp ramp)
    {
        public RampRow ToRow() => new()
        {
            Name = ramp.Name,
            IsHuman = ramp.IsHuman,
            Step1 = Hex(ramp.Steps[0]),
            Step2 = Hex(ramp.Steps[1]),
            Step3 = Hex(ramp.Steps[2]),
            Step4 = Hex(ramp.Steps[3]),
            Step5 = Hex(ramp.Steps[4]),
        };
    }

    extension(RampRow row)
    {
        public Result<SkinRamp, RampFailure> ToRamp()
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                return new(RampFailure.NameEmpty);
            }

            var steps = ImmutableArray.CreateBuilder<SKColor>(SkinRamps.StepCount);

            foreach (var hex in row.Steps)
            {
                if (!TryParseHex(hex, out var color))
                {
                    return new(RampFailure.StoreMalformed);
                }

                steps.Add(color);
            }

            return new SkinRamp
            {
                Name = row.Name.Trim(),
                IsHuman = row.IsHuman,
                Steps = steps.ToImmutable(),
            };
        }
    }

    public static string Hex(SKColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}");

    /// <summary>
    /// Parsing goes through <see cref="ColorConverter.HexToRgb"/>, as
    /// <see cref="SkinRamps"/> already does — but that throws on garbage, and a hand-edited file
    /// is expected input, so the shape is checked first.
    /// </summary>
    public static bool TryParseHex(string? hex, out SKColor color)
    {
        color = default;

        if (hex is null)
        {
            return false;
        }

        var trimmed = hex.AsSpan().Trim();

        if (trimmed.Length is not 0 && trimmed[0] is '#')
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length is not 6)
        {
            return false;
        }

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        var rgb = ColorConverter.HexToRgb(new HEX(trimmed.ToString()));

        color = new SKColor(rgb.R, rgb.G, rgb.B);

        return true;
    }
}
#pragma warning restore CA1708
