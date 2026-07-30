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

    private const string PngExtension = ".png";

    /// <summary>
    /// The file extension one format is written with, dot included.
    /// </summary>
    /// <param name="format">Which container the sheet is encoded into.</param>
    /// <returns>The extension, e.g. <c>.png</c>.</returns>
    /// <remarks>
    /// One mapping, consulted by <see cref="SheetRecipe.RelativePath"/> and by
    /// <see cref="Write"/>. Before this existed the extension was a single constant, which was
    /// correct only while there was a single consumer.
    /// </remarks>
    public static string ExtensionFor(SheetFormat format) => format switch
    {
        SheetFormat.Webp => Extension,
        SheetFormat.Png => PngExtension,
        _ => ThrowHelper.ThrowArgumentOutOfRangeException<string>(nameof(format)),
    };

    /// <summary>Every extension a sheet can be written with, derived from the formats themselves.</summary>
    /// <remarks>
    /// Projected from <see cref="Enum.GetValues{TEnum}"/> rather than listed, so a fourth format is
    /// covered by <see cref="IsSheetFile"/> the moment it exists. Computed once — this is consulted
    /// per file by <see cref="OrphanScan"/>'s directory walk.
    /// </remarks>
    private static string[] Extensions { get; } = [.. Enum.GetValues<SheetFormat>().Select(ExtensionFor)];

    /// <summary>
    /// Whether a file name is one a bake run could have written.
    /// </summary>
    /// <param name="name">A file name, extension included.</param>
    /// <returns><see langword="true"/> when it ends in any format's extension.</returns>
    /// <remarks>
    /// The counterpart to <see cref="ExtensionFor"/>, and the reason both live here: writing a sheet
    /// and recognising one are the same question asked from two ends. <see cref="OrphanScan"/>
    /// previously asked it with the WebP literal, which silently stopped being the whole answer the
    /// moment a second format existed.
    /// </remarks>
    public static bool IsSheetFile(string name)
    {
        Guard.IsNotNull(name);

        foreach (var extension in Extensions)
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes <paramref name="sheet"/> to <c>&lt;directory&gt;/&lt;name&gt;</c> under
    /// <paramref name="format"/>'s extension, and reports how much landed. The stream's position is
    /// irrelevant: the whole thing is written.
    /// </summary>
    /// <remarks>
    /// <paramref name="format"/> is required rather than defaulted. This is the point where encoded
    /// bytes meet a filename, and a silent default is exactly how PNG data ends up inside a
    /// <c>.webp</c> — a file neither Corvus nor either engine could open, and one nothing
    /// downstream would diagnose.
    /// </remarks>
    public static Result<ByteSize, BakeFailure> Write(
        FullPath directory,
        string name,
        RecyclableMemoryStream sheet,
        SheetFormat format)
    {
        Guard.IsNotNull(sheet);
        Guard.IsNotNullOrWhiteSpace(name);

        if (!Directory.Exists(directory.Value))
        {
            return new(BakeFailure.OutputDirectoryUnavailable);
        }

        try
        {
            // Combination is inside the try too: a Name with a path-invalid character (a bad
            // recipe, not a bug) throws from the combine, not just from File.Create.
            var target = directory / (name + ExtensionFor(format));

            using var file = File.Create(target.Value);

            // WriteTo ignores Position and writes the full length, which is what we want and is
            // also why this never rewinds the caller's stream.
            sheet.WriteTo(file);

            return ByteSize.FromBytes(sheet.Length);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return new(BakeFailure.OutputWriteFailed);
        }
    }
}
