using System.Threading.Channels;
using DotNext.Runtime.Caching;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// Decodes asset thumbnails off the UI thread and hands them back as they land.
/// </summary>
/// <remarks>
/// <para>
/// A virtualized grid over 995 partials realizes and unrealizes tiles faster than they can be
/// decoded, and each decode is an 828 KiB PNG read to produce a 48x48 crop. Three built-ins carry
/// that, and no bespoke machinery is involved:
/// </para>
/// <para>
/// An <b>unbounded channel</b> is the request queue. It was bounded with
/// <c>DropOldest</c> at first, on the theory that fast scrolling should shed work — but requests
/// are raised once per tile when a slot is chosen, not per realization, so the set is bounded by
/// the slot (a couple of hundred at most) rather than by scroll velocity. Dropping merely lost
/// tiles that were still on screen, oldest first, which is precisely the ones being looked at, and
/// nothing ever asked for them again.
/// </para>
/// <para>
/// <see cref="Parallel.ForEachAsync{TSource}(IAsyncEnumerable{TSource}, ParallelOptions, Func{TSource, CancellationToken, ValueTask})"/>
/// over <see cref="ChannelReader{T}.ReadAllAsync"/> is the pump. The reader is already an
/// <see cref="IAsyncEnumerable{T}"/>, so this needs no adapter, and it bounds concurrency itself —
/// which is why there is no throttle here. That is the same choice, for the same reason,
/// <c>BatchBaker</c> documents: the work is CPU-bound and synchronous, so the data-parallel
/// primitive fits and an async throttle would mean wrapping it in <c>Task.Run</c> for no gain.
/// </para>
/// <para>
/// <see cref="RandomAccessCache{TKey, TValue}"/> holds the results, bounded, evicting what has not
/// been looked at. A dictionary would grow to every tile ever scrolled past; this keeps a working
/// set. The values are <see cref="WriteableBitmap"/>, which is not disposable, so eviction needs no
/// teardown hook.
/// </para>
/// </remarks>
public sealed class ThumbnailService : IAsyncDisposable
{
    /// <summary>
    /// How many tiles stay cached. Roughly a screenful of a maximised grid several times over, so
    /// ordinary scrolling back and forth never re-decodes.
    /// </summary>
    private const int CacheCapacity = 512;

    private readonly RandomAccessCache<string, WriteableBitmap> _cache = new(CacheCapacity);
    private readonly CancellationTokenSource _stopping = new();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Channel<AssetPartial> _requests;
    private readonly Task _pump;
    private int _disposed;

    /// <summary>Starts the decode pump. Construct on the UI thread; it captures that dispatcher.</summary>
    public ThumbnailService()
    {
        _requests = Channel.CreateUnbounded<AssetPartial>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
        });

        _pump = PumpAsync();
    }

    /// <summary>Raised on the UI thread when a thumbnail is ready.</summary>
    public event EventHandler<ThumbnailReadyEventArgs>? Ready;

    /// <summary>The cache key for a partial — its slot and file name, which together are unique.</summary>
    public static string KeyFor(AssetPartial partial) => $"{partial.Slot}/{partial.FileName}";

    /// <summary>
    /// The thumbnail if it is already decoded.
    /// </summary>
    /// <param name="partial">The partial to look up.</param>
    /// <param name="thumbnail">The bitmap, when one is cached.</param>
    /// <returns><see langword="true"/> when it was cached, so the caller can skip the queue.</returns>
    /// <remarks>
    /// Synchronous, because a realizing tile asks from the UI thread and a cache hit should paint
    /// in that same layout pass rather than a frame later.
    /// </remarks>
    public bool TryGet(AssetPartial partial, out WriteableBitmap? thumbnail)
    {
        if (_cache.TryRead(KeyFor(partial), out var session))
        {
            using (session)
            {
                thumbnail = session.Value;

                return true;
            }
        }

        thumbnail = null;

        return false;
    }

    /// <summary>Queues a decode. Duplicates are harmless — the worker re-checks the cache first.</summary>
    public void Request(AssetPartial partial) => _requests.Writer.TryWrite(partial);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        _requests.Writer.TryComplete();

        await _stopping.CancelAsync().ConfigureAwait(false);

        try
        {
            // VSTHRD003 warns about awaiting work started elsewhere, which is the deadlock risk it
            // exists for. Not the case here: this object started _pump in its own constructor, and
            // the pump never posts back to a context it would then wait on.
#pragma warning disable VSTHRD003
            await _pump.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (OperationCanceledException)
        {
            // Expected: cancelling the pump is how it is stopped.
        }

        _stopping.Dispose();

        await _cache.DisposeAsync().ConfigureAwait(false);
    }

    private async Task PumpAsync()
    {
        var options = new ParallelOptions
        {
            // Decodes are CPU-bound; leave headroom so the UI thread still gets scheduled.
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
            CancellationToken = _stopping.Token,
        };

        try
        {
            await Parallel.ForEachAsync(
                _requests.Reader.ReadAllAsync(_stopping.Token),
                options,
                DecodeAsync).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async ValueTask DecodeAsync(AssetPartial partial, CancellationToken cancellationToken)
    {
        var key = KeyFor(partial);

        // It may have been decoded between the request and this worker picking it up.
        if (_cache.Contains(key))
        {
            return;
        }

        var still = Crop(partial);

        if (still is null)
        {
            return;
        }

        // ToWriteableBitmap builds a XAML object, so the conversion has to happen on the UI thread.
        // The SKBitmap crosses the hop and is disposed there.
        _dispatcher.TryEnqueue(() => Publish(key, partial, still));

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one partial and takes its still, disposing the 828 KiB assembly immediately.
    /// </summary>
    /// <remarks>
    /// Which cell the still comes from is <see cref="SpriteFilmstrip.RenderStill"/>'s decision.
    /// Hardcoding the conventional pose here left 47 partials — tails, back hair, and the bow,
    /// arrow and pickaxe — rendering as blank tiles, because they carry nothing at that cell.
    /// </remarks>
    private static SKBitmap? Crop(AssetPartial partial)
    {
        var opened = SpriteFilmstrip.Open(partial.Path);

        if (!opened.TryGet(out var filmstrip))
        {
            return null;
        }

        using (filmstrip)
        {
            var rendered = filmstrip.RenderStill(scale: 1);

            return rendered.TryGet(out var still) ? still : null;
        }
    }

    private void Publish(string key, AssetPartial partial, SKBitmap still)
    {
        WriteableBitmap bitmap;

        using (still)
        {
            bitmap = still.ToWriteableBitmap();
        }

        Store(key, bitmap);

        Ready?.Invoke(this, new ThumbnailReadyEventArgs(partial, bitmap));
    }

    /// <summary>
    /// Writes into the cache without awaiting, since this runs on the UI thread.
    /// </summary>
    /// <remarks>
    /// A failed insert is not worth handling: the thumbnail has already been handed to the tile
    /// that asked, and the only cost is decoding it again if it scrolls back.
    /// </remarks>
    private void Store(string key, WriteableBitmap bitmap) =>
        _ = StoreAsync(key, bitmap);

    private async Task StoreAsync(string key, WriteableBitmap bitmap)
    {
        try
        {
            using var session = await _cache.ChangeAsync(key, _stopping.Token).ConfigureAwait(false);

            session.SetValue(bitmap);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
