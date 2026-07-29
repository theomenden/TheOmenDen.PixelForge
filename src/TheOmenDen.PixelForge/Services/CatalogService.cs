using DotNext;
using Microsoft.Extensions.Logging;
using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// Holds the scanned asset catalogue and keeps it in step with the configured pack paths.
/// </summary>
/// <remarks>
/// <para>
/// Rescans whenever <see cref="SourcePackService.Changed"/> fires. The scan reads directory
/// entries only — no image is decoded — so re-running it on a path change costs nothing worth
/// deferring, and a stale catalogue would silently plan bakes against files that moved.
/// </para>
/// <para>
/// No interface: there is one implementation and nothing mocks it.
/// </para>
/// </remarks>
public sealed class CatalogService
{
    private readonly SourcePackService _packs;
    private readonly ILogger<CatalogService> _logger;

    /// <summary>Subscribes to the pack service so the catalogue can never go stale silently.</summary>
    /// <param name="packs">Where the three pack roots are configured.</param>
    /// <param name="logger">Where a failed scan is recorded.</param>
    public CatalogService(SourcePackService packs, ILogger<CatalogService> logger)
    {
        _packs = packs;
        _logger = logger;

        // Never unsubscribed: both are singletons that live as long as the process, so there is
        // nothing here to leak and no teardown ordering to get wrong.
        _packs.Changed += OnPacksChanged;
    }

    /// <summary>The catalogue, or <see cref="Optional{T}.None"/> until the packs resolve.</summary>
    public Optional<AssetCatalog> Current { get; private set; } = Optional<AssetCatalog>.None;

    /// <summary>Raised after a rescan, so pages can rebuild their lists.</summary>
    /// <remarks>
    /// Raised on every rescan, including one that produced nothing — a page that only heard about
    /// successes would keep showing a catalogue the packs no longer back.
    /// </remarks>
    public event EventHandler? Changed;

    /// <summary>
    /// Walks the configured packs and replaces <see cref="Current"/> with what they hold.
    /// </summary>
    /// <remarks>
    /// A failed scan is not an error the user has to dismiss: unconfigured or moved packs are the
    /// ordinary state on a first run. It is logged at warning and leaves <see cref="Current"/> as
    /// <see cref="Optional{T}.None"/>, which is what puts the page's "packs missing" bar up.
    /// </remarks>
    public void Rescan()
    {
        Current = Scan();

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Optional<AssetCatalog> Scan()
    {
        if (!_packs.Resolved.TryGet(out var packs))
        {
            return Optional<AssetCatalog>.None;
        }

        var scanned = AssetCatalog.Scan(packs);

        if (scanned.TryGet(out var catalog))
        {
            return catalog;
        }

        _logger.LogWarning("Asset catalogue scan failed: {Failure}", scanned.Error);

        return Optional<AssetCatalog>.None;
    }

    private void OnPacksChanged(object? sender, EventArgs e) => Rescan();
}
