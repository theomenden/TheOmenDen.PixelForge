using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;
using Microsoft.IO;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Puts a baked sheet on disk.
/// <para>
/// <see cref="RecyclableMemoryStream.WriteTo(Stream)"/> is the zero-copy path and the only one
/// available: the manager sets <c>ThrowExceptionOnToArray</c>, so the obvious
/// <c>File.WriteAllBytes(stream.ToArray())</c> throws by design rather than quietly copying a
/// pooled buffer back onto the managed heap.
/// </para>
/// <para>
/// A missing directory or a locked file is someone's disk, not a bug, so both travel as
/// <see cref="BakeFailure"/> values.
/// </para>
/// </summary>
public static class SheetWriter
{
    public const string Extension = ".webp";

    /// <summary>
    /// Writes <paramref name="sheet"/> to <c>&lt;directory&gt;/&lt;name&gt;.webp</c> and reports
    /// how much landed. The stream's position is irrelevant: the whole thing is written.
    /// </summary>
    public static Result<ByteSize, BakeFailure> Write(
        FullPath directory,
        string name,
        RecyclableMemoryStream sheet)
    {
        Guard.IsNotNull(sheet);
        Guard.IsNotNullOrWhiteSpace(name);

        if (!Directory.Exists(directory.Value))
        {
            return new(BakeFailure.OutputDirectoryUnavailable);
        }

        var target = directory / (name + Extension);

        try
        {
            using var file = File.Create(target.Value);

            // WriteTo ignores Position and writes the full length, which is what we want and is
            // also why this never rewinds the caller's stream.
            sheet.WriteTo(file);

            return ByteSize.FromBytes(sheet.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(BakeFailure.OutputWriteFailed);
        }
    }
}
