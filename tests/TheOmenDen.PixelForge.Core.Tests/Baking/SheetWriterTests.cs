using Meziantou.Framework;
using Microsoft.IO;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Buffers;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

public sealed class SheetWriterTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    private static RecyclableMemoryStream StreamOf(params byte[] bytes)
    {
        var stream = PooledStreams.New("test");

        stream.Write(bytes);
        stream.Position = 0;

        return stream;
    }

    [Fact]
    public void Write_PutsTheStreamBytesOnDisk_UnderTheRecipeName()
    {
        using var sheet = StreamOf(1, 2, 3, 4, 5);

        var result = SheetWriter.Write(_directory.FullPath, "body-01", sheet);

        Assert.True(result.IsSuccessful, $"write failed with {result.Error}");

        var written = _directory.FullPath / "body-01.webp";

        Assert.True(File.Exists(written.Value));
        Assert.Equal<byte[]>([1, 2, 3, 4, 5], File.ReadAllBytes(written.Value));
    }

    [Fact]
    public void Write_ReturnsTheNumberOfBytesWritten()
    {
        using var sheet = StreamOf(1, 2, 3, 4, 5);

        var result = SheetWriter.Write(_directory.FullPath, "body-01", sheet);

        Assert.True(result.IsSuccessful);
        Assert.Equal(5L, result.Value.Value);
    }

    /// <summary>
    /// Writing must not depend on where the caller left the position. A verified encode rewinds,
    /// but nothing in the type system says so.
    /// </summary>
    [Fact]
    public void Write_WritesTheWholeStream_RegardlessOfPosition()
    {
        using var sheet = StreamOf(1, 2, 3, 4, 5);

        sheet.Position = 3;

        var result = SheetWriter.Write(_directory.FullPath, "body-02", sheet);

        Assert.True(result.IsSuccessful);
        Assert.Equal(5L, result.Value.Value);
    }

    [Fact]
    public void Write_ReportsOutputDirectoryUnavailable_WhenTheDirectoryIsMissing()
    {
        using var sheet = StreamOf(1, 2, 3);

        var missing = _directory.FullPath / "does-not-exist";

        var result = SheetWriter.Write(missing, "body-01", sheet);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.OutputDirectoryUnavailable, result.Error);
    }

    /// <summary>
    /// A path-invalid <see cref="SheetRecipe.Name"/> is bad data, not a bug — it must come back
    /// as <see cref="BakeFailure.OutputWriteFailed"/>, the same as any other write failure, and
    /// never escape as an unhandled exception that would abort a whole batch run.
    /// </summary>
    [Fact]
    public void Write_ReportsOutputWriteFailed_WhenTheNameHasAPathInvalidCharacter()
    {
        using var sheet = StreamOf(1, 2, 3);

        // An embedded NUL is illegal in a path on every platform, unlike most of Windows'
        // reserved characters (':' is legal mid-name — it addresses an NTFS alternate data
        // stream — so it does not exercise this path).
        var result = SheetWriter.Write(_directory.FullPath, "bad\0name", sheet);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.OutputWriteFailed, result.Error);
    }
}
