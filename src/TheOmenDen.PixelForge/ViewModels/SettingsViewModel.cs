using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Services;

namespace TheOmenDen.PixelForge.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IThemeService _themeService;
    private readonly SourcePackService _packs;
    private readonly PickerService _picker;

    public SettingsViewModel(IThemeService themeService, SourcePackService packs, PickerService picker)
    {
        _themeService = themeService;
        _packs = packs;
        _picker = picker;

        _packs.Changed += OnPacksChanged;
    }

    /// <summary>
    /// Index into the theme Segmented: 0 = System, 1 = Light, 2 = Dark. Segmented binds
    /// SelectedIndex rather than the enum, so no converter is needed.
    /// </summary>
    public int SelectedThemeIndex
    {
        get => _themeService.Theme switch
        {
            ElementTheme.Light => 1,
            ElementTheme.Dark => 2,
            _ => 0,
        };
        set
        {
            var theme = value switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            if (theme == _themeService.Theme)
            {
                return;
            }

            _themeService.Apply(theme);
            OnPropertyChanged();
        }
    }

    public string CorePackPath => Describe(_packs.Core);

    public string Expansion1PackPath => Describe(_packs.Expansion1);

    public string Expansion2PackPath => Describe(_packs.Expansion2);

    /// <summary>Drives the batch page's blocking InfoBar, so it lives where the paths do.</summary>
    public bool AllPacksResolved => _packs.Resolved.HasValue;

    [RelayCommand]
    private Task BrowseCorePackAsync(CancellationToken cancellationToken) =>
        BrowseAsync(ElementsPack.Core, cancellationToken);

    [RelayCommand]
    private Task BrowseExpansion1PackAsync(CancellationToken cancellationToken) =>
        BrowseAsync(ElementsPack.CharacterExpansion1, cancellationToken);

    [RelayCommand]
    private Task BrowseExpansion2PackAsync(CancellationToken cancellationToken) =>
        BrowseAsync(ElementsPack.CharacterExpansion2, cancellationToken);

    private async Task BrowseAsync(ElementsPack pack, CancellationToken cancellationToken)
    {
        var picked = await _picker.PickFolderAsync();

        if (picked.TryGet(out var path))
        {
            await _packs.SetAsync(pack, path, cancellationToken);
        }
    }

    private void OnPacksChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CorePackPath));
        OnPropertyChanged(nameof(Expansion1PackPath));
        OnPropertyChanged(nameof(Expansion2PackPath));
        OnPropertyChanged(nameof(AllPacksResolved));
    }

    private static string Describe(DotNext.Optional<Meziantou.Framework.FullPath> path) =>
        path.TryGet(out var value)
            ? Directory.Exists(value.Value) ? value.Value : $"{value.Value} (missing)"
            : "Not set";
}
