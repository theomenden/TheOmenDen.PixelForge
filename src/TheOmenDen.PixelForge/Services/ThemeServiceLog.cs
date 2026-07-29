using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// Source-generated log methods — no boxing, and nothing is formatted when the level is off.
/// </summary>
internal static partial class ThemeServiceLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Theme restored: {Theme}")]
    public static partial void ThemeRestored(this ILogger logger, ElementTheme theme);

    [LoggerMessage(Level = LogLevel.Information, Message = "Theme changed: {Theme}")]
    public static partial void ThemeChanged(this ILogger logger, ElementTheme theme);
}
