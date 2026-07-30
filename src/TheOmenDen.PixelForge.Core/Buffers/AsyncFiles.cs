using Meziantou.Framework;

namespace TheOmenDen.PixelForge.Core.Buffers;

/// <summary>
/// Opens files for genuinely asynchronous I/O.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FileOptions.Asynchronous"/> is the whole reason this exists. A
/// <see cref="FileStream"/> opened without it has no overlapped handle behind it, so
/// <c>WriteAsync</c> on it is not asynchronous at all — the runtime queues the blocking call to a
/// thread-pool thread and awaits that. The <c>await</c> still compiles, the code still reads as
/// async, and the only thing that changed is which thread is blocked. That is the failure this
/// centralises away: it is invisible at the call site and every convenience constructor
/// (<c>new StreamWriter(path)</c>, <see cref="File.Create(string)"/>) picks the wrong default.
/// </para>
/// <para>
/// The manifests are small, so the win is not throughput — it is that the export command and the
/// palette editor no longer block the UI thread on the filesystem while they land.
/// </para>
/// </remarks>
internal static class AsyncFiles
{
    private static readonly FileStreamOptions Read = new()
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
    };

    private static readonly FileStreamOptions Write = new()
    {
        Mode = FileMode.Create,
        Access = FileAccess.Write,
        Options = FileOptions.Asynchronous,
    };

    /// <summary>Opens <paramref name="path"/> for reading, truncating nothing.</summary>
    internal static StreamReader OpenText(FullPath path) => new(path.Value, Read);

    /// <summary>Creates or truncates <paramref name="path"/> for text.</summary>
    internal static StreamWriter CreateText(FullPath path) => new(path.Value, Write);

    /// <summary>Creates or truncates <paramref name="path"/> for bytes.</summary>
    internal static FileStream Create(FullPath path) => new(path.Value, Write);
}
