using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Services;
using TheOmenDen.PixelForge.ViewModels;

namespace TheOmenDen.PixelForge.Views;

/// <summary>
/// Keeps the asset grid's tiles in step with the selected slot, and fills their stills as decodes
/// land.
/// </summary>
/// <remarks>
/// <para>
/// The fourth of this project's view-side hosts, after <see cref="CompositePreview"/>,
/// <see cref="PalettePreviewHost"/> and <see cref="AnimationPreviewHost"/>, and it exists for the
/// same reason: the view model deals in <see cref="AssetPartial"/> values, this turns them into
/// something bindable that carries a bitmap.
/// </para>
/// <para>
/// Every tile of the selected slot is requested once, when the slot changes — not as tiles scroll
/// into view. That is deliberate. Realization-driven requests looked cheaper but could not be made
/// correct: the event does not fire again for an already-realized tile, so anything missed the
/// first time stayed missing. A slot is at most a couple of hundred partials, the decodes are
/// bounded by the pump behind <see cref="ThumbnailService"/>, and each result is cached, so
/// re-selecting a slot repaints from memory rather than re-reading disk.
/// </para>
/// <para>
/// Public, unlike the other hosts, because the grid binds to <see cref="Tiles"/> and
/// <c>x:Bind</c> resolves that against the page's declared type.
/// </para>
/// </remarks>
/// <param name="viewModel">The page's view model; its selected slot drives the tile set.</param>
/// <param name="thumbnails">Where stills are decoded and cached.</param>
public sealed class ThumbnailGallery(AssetsViewModel viewModel, ThumbnailService thumbnails)
{
    /// <summary>Tiles for the selected slot, in catalogue order. The grid binds to this.</summary>
    public ObservableCollection<AssetTile> Tiles { get; } = [];

    /// <summary>Subscribes and builds the first slot's tiles.</summary>
    public void Start()
    {
        viewModel.Partials.CollectionChanged -= OnPartialsChanged;
        viewModel.Partials.CollectionChanged += OnPartialsChanged;

        viewModel.PropertyChanged -= OnViewModelChanged;
        viewModel.PropertyChanged += OnViewModelChanged;

        thumbnails.Ready -= OnThumbnailReady;
        thumbnails.Ready += OnThumbnailReady;

        Rebuild();
    }

    /// <summary>Unsubscribes. The tiles are kept, so returning to the page repaints instantly.</summary>
    public void Stop()
    {
        viewModel.Partials.CollectionChanged -= OnPartialsChanged;
        viewModel.PropertyChanged -= OnViewModelChanged;
        thumbnails.Ready -= OnThumbnailReady;
    }

    /// <summary>The tile standing for <paramref name="partial"/>, if this slot holds one.</summary>
    public AssetTile? Find(AssetPartial partial)
    {
        foreach (var tile in Tiles)
        {
            if (tile.Partial.Equals(partial))
            {
                return tile;
            }
        }

        return null;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AssetsViewModel.SelectedSlot))
        {
            Rebuild();
        }
    }

    private void OnPartialsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    /// <summary>
    /// Rebuilds the tile set for the current slot, painting what is cached and queueing the rest.
    /// </summary>
    private void Rebuild()
    {
        Tiles.Clear();

        foreach (var partial in viewModel.Partials)
        {
            var tile = new AssetTile(partial);

            // A cache hit paints in this pass; everything else arrives via Ready.
            if (thumbnails.TryGet(partial, out var cached))
            {
                tile.Thumbnail = cached;
            }
            else
            {
                thumbnails.Request(partial);
            }

            Tiles.Add(tile);
        }
    }

    /// <summary>
    /// Hands a finished still to its tile, if that tile is still on screen.
    /// </summary>
    /// <remarks>
    /// The slot may have changed while the decode was in flight, which is why this looks the tile
    /// up rather than holding a reference to it.
    /// </remarks>
    private void OnThumbnailReady(object? sender, ThumbnailReadyEventArgs e)
    {
        if (Find(e.Partial) is { } tile)
        {
            tile.Thumbnail = e.Thumbnail;
        }
    }
}
