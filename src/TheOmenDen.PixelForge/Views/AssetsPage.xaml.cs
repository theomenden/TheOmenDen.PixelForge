using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Spritesheets;
using TheOmenDen.PixelForge.Services;
using TheOmenDen.PixelForge.ViewModels;

namespace TheOmenDen.PixelForge.Views;

/// <summary>
/// Browses the scanned catalogue as a grid of stills, with the selection animating beside it.
/// </summary>
/// <remarks>
/// Markup, <c>x:Bind</c> helpers and tile plumbing only. Decoding belongs to
/// <see cref="ThumbnailService"/> and playback to <see cref="AnimationPreviewHost"/> — the same
/// division <see cref="PipelinePage"/> and <see cref="PalettePage"/> keep.
/// </remarks>
public sealed partial class AssetsPage : Page
{
    private readonly AnimationPreviewHost _animation;

    public AssetsPage()
    {
        InitializeComponent();

        Gallery = new(ViewModel, App.Services.GetRequiredService<ThumbnailService>());
        _animation = new(ViewModel, AnimationImage, AnimationHint);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The page's view model, resolved from the host.</summary>
    public AssetsViewModel ViewModel { get; } = App.Services.GetRequiredService<AssetsViewModel>();

    /// <summary>The bindable tiles behind the grid, and what fills their stills.</summary>
    public ThumbnailGallery Gallery { get; }

    /// <summary>Play for a clip that animates, pause for a single-frame pose.</summary>
    /// <remarks>
    /// <see cref="GeneratorClip.IsRenderedByDefault"/> is the generator's own distinction between
    /// an animation and a pose — <c>stand</c>, <c>crouch</c>, <c>wind_up</c> and <c>nock</c> are
    /// poses that exist to be composed into other clips.
    /// </remarks>
    public static string ClipGlyph(bool isRenderedByDefault) =>
        isRenderedByDefault ? "\uE768" : "\uE769";

    /// <summary>How many frames a clip plays, counting repeats.</summary>
    /// <remarks>
    /// The length of <see cref="GeneratorClip.Frames"/>, not its distinct columns: <c>walk</c> is
    /// 1, 2, 1, 0 — four frames drawn from three columns.
    /// </remarks>
    public static string FrameLabel(ImmutableArray<int> frames) =>
        frames.Length is 1 ? "1 frame" : $"{frames.Length} frames";

    /// <summary>The selected clip's length, cadence and draw order, in one line.</summary>
    public static string ClipSummary(GeneratorClip clip)
    {
        // The division is done once in Core, so this is a multiply per call rather than both.
        var seconds = clip.Frames.Length * GeneratorClips.FrameDurationSeconds;

        var summary = string.Create(
            CultureInfo.CurrentCulture,
            $"{FrameLabel(clip.Frames)} · {GeneratorClips.FrameDurationMilliseconds} ms each · {seconds:0.0}s loop");

        // Climb is the one clip the generator composites back to front. It is worth surfacing:
        // a preview that ignored it would show the layers stacked the wrong way round.
        return clip.ReverseDrawOrder ? $"{summary} · reversed layer order" : summary;
    }

    /// <summary>Collapses an element rather than leaving an empty gap.</summary>
    /// <remarks>
    /// An <c>x:Bind</c> function, which is how this project does visibility — no IValueConverter,
    /// and never <c>Converter={x:Null}</c>, which crashes at run time.
    /// </remarks>
    public static Visibility ShowIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Pluralised catalogue size, or an invitation when nothing has been scanned.</summary>
    public static string CatalogueLabel(int count) => count switch
    {
        0 => "No art found yet. Set your three pack folders in Settings.",
        1 => "1 piece of art found across your three packs.",
        _ => $"{count} pieces of art found across your three packs.",
    };

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Gallery.Start();
        _animation.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Gallery.Stop();
        _animation.Stop();
    }

    /// <summary>
    /// Pushes the chosen tile's partial into the view model, which drives the preview.
    /// </summary>
    /// <remarks>
    /// The grid's items are <see cref="AssetTile"/> because they carry a bitmap, while the view
    /// model deals in <see cref="AssetPartial"/> so it stays free of UI types. This is the one
    /// place the two meet.
    /// </remarks>
    private void OnTileSelected(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SelectedPartial = (AssetGrid.SelectedItem as AssetTile)?.Partial;

    /// <summary>Loads the chosen component's partials.</summary>
    /// <remarks>
    /// Calls <see cref="AssetsViewModel.Select"/> rather than writing a property, so the load
    /// happens because it was asked for and not because a value happened to differ.
    /// </remarks>
    private void OnComponentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: SlotOption option })
        {
            ViewModel.Select(option.Slot);
        }
    }

    private void OnGoToSettings(object sender, RoutedEventArgs e) =>
        MainWindow.Shell?.NavigateToSettings();
}
