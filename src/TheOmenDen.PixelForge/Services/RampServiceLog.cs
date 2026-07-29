using Microsoft.Extensions.Logging;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// Source-generated log methods for <see cref="RampService"/> — no boxing, and nothing is
/// formatted when the level is off.
/// </summary>
internal static partial class RampServiceLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped imported ramp {Name}: name is a built-in")]
    public static partial void SkippedBuiltInImport(this ILogger logger, string name);
}
