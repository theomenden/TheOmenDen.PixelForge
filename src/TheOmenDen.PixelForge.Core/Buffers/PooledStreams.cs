using Microsoft.IO;

namespace TheOmenDen.PixelForge.Core.Buffers;

/// <summary>
/// The process-wide <see cref="RecyclableMemoryStreamManager"/>.
/// <para>
/// One manager, created once. That is the whole point of the library and also the way it is
/// most often got wrong: a manager constructed per call pools nothing, it just adds a layer of
/// bookkeeping over the same allocations. The pipeline converts many files in a batch, so the
/// buffers this recycles are exactly the ones that would otherwise churn the LOH.
/// </para>
/// </summary>
public static class PooledStreams
{
    /// <summary>
    /// 128 KiB. A source partial is 1104x192x4 = 828 KiB, so a decoded sheet is a handful of
    /// blocks rather than one oversized allocation.
    /// </summary>
    private const int BlockSize = 128 * 1024;

    private const int LargeBufferMultiple = 1024 * 1024;
    private const int MaximumBufferSize = 16 * 1024 * 1024;

    public static RecyclableMemoryStreamManager Manager { get; } = new(new RecyclableMemoryStreamManager.Options
    {
        BlockSize = BlockSize,
        LargeBufferMultiple = LargeBufferMultiple,
        MaximumBufferSize = MaximumBufferSize,
        MaximumSmallPoolFreeBytes = BlockSize * 128L,
        MaximumLargePoolFreeBytes = MaximumBufferSize * 4L,
        UseExponentialLargeBuffer = true,

        // ToArray copies the pooled buffer straight back onto the managed heap, which is the
        // allocation this library exists to avoid. Making it throw means a caller cannot
        // quietly undo the pooling — they have to reach for WriteTo, GetBuffer, or
        // GetReadOnlySequence, all of which are zero-copy.
        ThrowExceptionOnToArray = true,

        // Buffers are always fully overwritten before being read back, and zeroing 828 KiB per
        // sheet is measurable. No pooled buffer ever crosses a trust boundary here.
        ZeroOutBuffer = false,
    });

    /// <summary>A pooled stream, tagged so leaks are attributable in the manager's diagnostics.</summary>
    public static RecyclableMemoryStream New(string tag) => Manager.GetStream(tag);

    /// <summary>
    /// A pooled stream pre-sized to <paramref name="requiredSize"/>, avoiding the block-by-block
    /// growth when the final size is already known.
    /// </summary>
    public static RecyclableMemoryStream New(string tag, long requiredSize) =>
        Manager.GetStream(tag, requiredSize);
}
