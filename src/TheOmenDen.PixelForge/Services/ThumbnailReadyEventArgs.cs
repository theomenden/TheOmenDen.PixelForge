using Microsoft.UI.Xaml.Media.Imaging;
using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.Services;

/// <summary>A decoded thumbnail, and which partial it is for.</summary>
/// <remarks>
/// <para>
/// Derives from <see cref="EventArgs"/> so it can ride on <see cref="EventHandler{TEventArgs}"/>,
/// which is why it is a plain class rather than a record — a record may only inherit from
/// <see langword="object"/> or another record (CS8864). Same reasoning as
/// <see cref="ViewModels.StatusNoticeEventArgs"/>.
/// </para>
/// <para>
/// Carries the partial rather than a tile index. By the time a decode finishes the grid may have
/// scrolled and recycled that container onto a different asset, so a listener has to check the
/// tile still wants this one before assigning it.
/// </para>
/// </remarks>
/// <param name="partial">The partial that was decoded.</param>
/// <param name="thumbnail">Its still, ready to assign to an <c>Image</c>.</param>
public sealed class ThumbnailReadyEventArgs(AssetPartial partial, WriteableBitmap thumbnail) : EventArgs
{
    /// <summary>The partial that was decoded.</summary>
    public AssetPartial Partial { get; } = partial;

    /// <summary>Its still, ready to assign to an <c>Image</c>.</summary>
    public WriteableBitmap Thumbnail { get; } = thumbnail;
}
