using System.Globalization;
using ColorHelper;
using SkiaSharp;

namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// The <c>#RRGGBB</c> text form of a colour, in both directions.
/// </summary>
/// <remarks>
/// Shared by <see cref="RampStore"/>, which reads and writes it as CSV, and by the ramp editor,
/// which shows it in a text box — one spelling of a colour, so a hex a user types is the same hex
/// the file gets.
/// </remarks>
public static class RampConversions
{
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
