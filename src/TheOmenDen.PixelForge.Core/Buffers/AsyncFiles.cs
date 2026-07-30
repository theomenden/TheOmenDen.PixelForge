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
/// <para>
/// Public rather than internal because the app project has the same need and the same trap: pack
/// settings are written from a dispatcher-thread command. A second copy of these
/// <see cref="FileStreamOptions"/> over there is exactly the drift this exists to stop, so the
/// options are stated once and both projects open files through them.
/// </para>
/// </remarks>
public static class AsyncFiles
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

    /// <summary>Opens <paramref name="path"/> for reading text, truncating nothing.</summary>
    /// <param name="path">An existing file.</param>
    /// <returns>A reader over an overlapped handle.</returns>
    public static StreamReader OpenText(FullPath path) => new(path.Value, Read);

    /// <summary>Opens <paramref name="path"/> for reading bytes, truncating nothing.</summary>
    /// <param name="path">An existing file.</param>
    /// <returns>A stream over an overlapped handle.</returns>
    public static FileStream Open(FullPath path) => new(path.Value, Read);

    /// <summary>Creates or truncates <paramref name="path"/> for text.</summary>
    /// <param name="path">The file to write. Its directory must already exist.</param>
    /// <returns>A writer over an overlapped handle.</returns>
    public static StreamWriter CreateText(FullPath path) => new(path.Value, Write);

    /// <summary>Creates or truncates <paramref name="path"/> for bytes.</summary>
    /// <param name="path">The file to write. Its directory must already exist.</param>
    /// <returns>A stream over an overlapped handle.</returns>
    public static FileStream Create(FullPath path) => new(path.Value, Write);
}
