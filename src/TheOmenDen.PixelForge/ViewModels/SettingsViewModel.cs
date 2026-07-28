using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using TheOmenDen.PixelForge.Services;

namespace TheOmenDen.PixelForge.ViewModels;

public sealed partial class SettingsViewModel(IThemeService themeService) : ObservableObject
{
    /// <summary>
    /// Index into the theme RadioButtons: 0 = System, 1 = Light, 2 = Dark. RadioButtons binds
    /// SelectedIndex rather than the enum, so no converter is needed.
    /// </summary>
    public int SelectedThemeIndex
    {
        get => themeService.Theme switch
        {
            ElementTheme.Light => 1,
            ElementTheme.Dark => 2,
            _ => 0,
        };
        set
        {
            ElementTheme theme = value switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            if (theme == themeService.Theme)
            {
                return;
            }

            themeService.Apply(theme);
            OnPropertyChanged();
        }
    }
}
