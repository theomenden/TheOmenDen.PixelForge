using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace TheOmenDen.PixelForge.Services;

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
        if (!AppPaths.IsPackaged)
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
        if (!AppPaths.IsPackaged)
        {
            return;
        }

        ApplicationData.Current.LocalSettings.Values[SettingKey] = theme.ToString();
    }
}
