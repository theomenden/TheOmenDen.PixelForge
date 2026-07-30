using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.Views;

/// <summary>One tile in the asset grid: a partial, and its still once something has decoded it.</summary>
/// <remarks>
/// <para>
/// In <c>Views</c> rather than <c>ViewModels</c> because it names <see cref="WriteableBitmap"/>,
/// and a view model that names a <c>Microsoft.UI</c> type stops being unit-testable — the same
/// division <see cref="CompositePreview"/> and <see cref="PalettePreviewHost"/> keep.
/// </para>
/// <para>
/// The thumbnail is a bound property rather than something pushed into a container's
/// <c>Image</c>. Reaching into the container was the original approach and it was wrong twice
/// over: the template is not always realized when <c>ContainerContentChanging</c> fires, so the
/// lookup returned nothing and no retry ever came; and once a tile is realized that event does not
/// fire again, so a tile missed once stayed blank for the life of the page. A bound property has
/// neither problem — whichever container happens to be showing this item picks the value up, and
/// recycling is XAML's business rather than ours.
/// </para>
/// </remarks>
/// <param name="partial">The file this tile stands for.</param>
public sealed partial class AssetTile(AssetPartial partial) : ObservableObject
{
    /// <summary>The file this tile stands for.</summary>
    public AssetPartial Partial { get; } = partial;

    /// <summary>What the tile reads as — see <see cref="AssetPartial.Stem"/>.</summary>
    public string Name => Partial.Stem;

    /// <summary>The file on disk, for the tooltip.</summary>
    public string FileName => Partial.FileName;

    /// <summary>Unique across the page, so a UI test can address one tile.</summary>
    public string AutomationId => $"Tile{Partial.Slot}_{Partial.Stem}";

    /// <summary>The still, once decoded. Null until then, which renders as an empty frame.</summary>
    [ObservableProperty]
    public partial WriteableBitmap? Thumbnail { get; set; }
}
