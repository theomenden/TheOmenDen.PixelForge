using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// Applies and persists the app theme. <see cref="ElementTheme.Default"/> means "follow the
/// system", which is the shipping default.
/// </summary>
public interface IThemeService
{
    ElementTheme Theme { get; }

    /// <summary>Applies <paramref name="theme"/> to the window and persists the choice.</summary>
    void Apply(ElementTheme theme);

    /// <summary>Re-applies the persisted theme. Called once the window content exists.</summary>
    void Restore(FrameworkElement root);
}

public sealed class ThemeService(ILogger<ThemeService> logger) : IThemeService
{
    private const string SettingKey = "AppTheme";

    private FrameworkElement? _root;

    public ElementTheme Theme { get; private set; } = ElementTheme.Default;

    public void Restore(FrameworkElement root)
    {
        _root = root;
        Theme = Load();
        _root.RequestedTheme = Theme;

        logger.ThemeRestored(Theme);
    }

    public void Apply(ElementTheme theme)
    {
        Theme = theme;

        // Setting RequestedTheme on the content root re-evaluates every {ThemeResource} in
        // the tree. {StaticResource} does not update — which is why the app brushes are
        // StaticResource *redirects inside* theme dictionaries rather than plain resources.
        _root?.RequestedTheme = theme;

        Save(theme);
        logger.ThemeChanged(theme);
    }

    private static ElementTheme Load()
    {
        if (!App.IsPackaged)
        {
            return ElementTheme.Default;
        }

        return ApplicationData.Current.LocalSettings.Values[SettingKey] switch
        {
            string stored when Enum.TryParse(stored, out ElementTheme parsed) => parsed,
            _ => ElementTheme.Default,
        };
    }

    private static void Save(ElementTheme theme)
    {
        // Unpackaged launches have no LocalSettings store; the choice applies for the
        // session but is not persisted.
        if (!App.IsPackaged)
        {
            return;
        }

        ApplicationData.Current.LocalSettings.Values[SettingKey] = theme.ToString();
    }
}

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
