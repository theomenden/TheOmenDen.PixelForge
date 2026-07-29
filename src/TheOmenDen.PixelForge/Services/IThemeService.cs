using Microsoft.UI.Xaml;

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
