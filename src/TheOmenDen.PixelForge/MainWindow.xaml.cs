using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOmenDen.PixelForge.Services;
using TheOmenDen.PixelForge.Views;
using Windows.Graphics;

namespace TheOmenDen.PixelForge;

/// <summary>
/// Application shell: title bar, adaptive navigation, and the content frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    // Derived from the design rubric: canvas/media editors start at 1280 wide. 280px tool
    // panel + 320px sprite surface + an Expanded nav pane, rounded up to a multiple of 20.
    private const int DefaultWidthDips = 1360;
    private const int DefaultHeightDips = 900;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        ResizeToDefault();

        // Theme applies to the content root — Window itself has no RequestedTheme. Doing it
        // here means the persisted theme is live before the first paint.
        App.Services.GetRequiredService<IThemeService>().Restore(RootGrid);

        // NavigationView's settings entry already ships AutomationId "SettingsItem"; it is
        // created with the template, so it is null here and cannot be renamed from XAML.
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>
    /// AppWindow.Resize takes physical pixels, so the DIP size is scaled by monitor DPI.
    /// XamlRoot.RasterizationScale is null this early, hence the Win32 call.
    /// </summary>
    private void ResizeToDefault()
    {
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;

        AppWindow.Resize(new SizeInt32((int)(DefaultWidthDips * scale), (int)(DefaultHeightDips * scale)));
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // SelectionChanged, not ItemInvoked — ItemInvoked misses programmatic selection.
        if (args.IsSettingsSelected)
        {
            RootFrame.Navigate(typeof(SettingsPage));
            return;
        }

        var page = (args.SelectedItem as NavigationViewItem)?.Tag switch
        {
            "Assets" => typeof(AssetsPage),
            "Pipeline" => typeof(PipelinePage),
            _ => typeof(CanvasPage),
        };

        if (RootFrame.CurrentSourcePageType != page)
        {
            RootFrame.Navigate(page);
        }
    }

    private void OnTitleBarBackRequested(TitleBar sender, object args)
    {
        if (RootFrame.CanGoBack)
        {
            RootFrame.GoBack();
        }
    }

    private void OnTitleBarPaneToggleRequested(TitleBar sender, object args)
        => NavView.IsPaneOpen = !NavView.IsPaneOpen;
}
