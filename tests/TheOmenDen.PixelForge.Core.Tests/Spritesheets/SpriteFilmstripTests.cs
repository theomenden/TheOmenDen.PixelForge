using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Spritesheets;

/// <summary>
/// Cropping any cell out of a source assembly, which is what both the asset thumbnails and the
/// animation preview are built on.
/// </summary>
/// <remarks>
/// Source geometry rather than curated on purpose: curating drops north and eight of the twelve
/// generator clips, so a preview built on it could not show most of the art.
/// </remarks>
public sealed class SpriteFilmstripTests
{
    /// <summary>A source-geometry sheet whose every cell is a distinct, checkable colour.</summary>
    /// <remarks>
    /// Column and row are encoded into red and green, so a mis-cropped cell is not merely wrong but
    /// says which cell was fetched instead.
    /// </remarks>
    private static SKBitmap Striped()
    {
        var sheet = new SKBitmap(new SKImageInfo(
            SheetLayout.SourceWidth, SheetLayout.SourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        using var canvas = new SKCanvas(sheet);

        canvas.Clear(SKColors.Transparent);

        for (var row = 0; row < SheetLayout.SourceRows; row++)
        {
            for (var column = 0; column < SheetLayout.SourceColumns; column++)
            {
                using var paint = new SKPaint { Color = new SKColor((byte)(column * 10), (byte)(row * 60), 0xFF) };

                canvas.DrawRect(
                    SKRect.Create(
                        column * SheetLayout.CellSize,
                        row * SheetLayout.CellSize,
                        SheetLayout.CellSize,
                        SheetLayout.CellSize),
                    paint);
            }
        }

        return sheet;
    }

    private static SpriteFilmstrip Filmstrip(TemporaryDirectory root)
    {
        var path = root.FullPath / "striped.png";

        using (var sheet = Striped())
        using (var image = SKImage.FromBitmap(sheet))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var file = File.Create(path.Value))
        {
            data.SaveTo(file);
        }

        var opened = SpriteFilmstrip.Open(path);

        Assert.True(opened.IsSuccessful, $"open failed with {opened.Error}");

        return opened.Value;
    }

    [Fact]
    public void RenderCell_TakesTheCellAtTheGivenFacingAndColumn()
    {
        using var root = TemporaryDirectory.Create();
        using var strip = Filmstrip(root);

        var rendered = strip.RenderCell(facing: 2, column: 7, scale: 1);

        Assert.True(rendered.IsSuccessful, $"render failed with {rendered.Error}");

        using var cell = rendered.Value;

        Assert.Equal(SheetLayout.CellSize, cell.Width);
        Assert.Equal(SheetLayout.CellSize, cell.Height);

        var pixel = cell.GetPixel(SheetLayout.CellSize / 2, SheetLayout.CellSize / 2);

        // Encoded by Striped: red is the column, green the row.
        Assert.Equal(70, pixel.Red);
        Assert.Equal(120, pixel.Green);
    }

    /// <summary>Nearest-neighbour only — a blurred edge here means pixel art is being smoothed.</summary>
    [Fact]
    public void RenderCell_ScalesWithoutInterpolating()
    {
        using var root = TemporaryDirectory.Create();
        using var strip = Filmstrip(root);

        var rendered = strip.RenderCell(facing: 0, column: 3, scale: 4);

        Assert.True(rendered.IsSuccessful, $"render failed with {rendered.Error}");

        using var cell = rendered.Value;

        Assert.Equal(SheetLayout.CellSize * 4, cell.Width);
        Assert.Equal(SheetLayout.CellSize * 4, cell.Height);

        // Every pixel of a flat cell stays exactly the source colour under nearest-neighbour.
        Assert.Equal(30, cell.GetPixel(0, 0).Red);
        Assert.Equal(30, cell.GetPixel(cell.Width - 1, cell.Height - 1).Red);
    }

    /// <summary>
    /// Every frame of every generator clip is reachable, on every facing. This is the contract the
    /// animation preview relies on — including <c>climb</c> and <c>nock_and_bow</c>, which the
    /// curated geometry drops entirely.
    /// </summary>
    [Fact]
    public void RenderCell_ReachesEveryGeneratorClipFrame()
    {
        using var root = TemporaryDirectory.Create();
        using var strip = Filmstrip(root);

        foreach (var clip in GeneratorClips.All)
        {
            for (var facing = 0; facing < GeneratorClips.Facings.Length; facing++)
            {
                foreach (var column in clip.Frames)
                {
                    var rendered = strip.RenderCell(facing, column, scale: 1);

                    Assert.True(rendered.IsSuccessful, $"{clip.Name} facing {facing} column {column}: {rendered.Error}");

                    rendered.Value.Dispose();
                }
            }
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(SheetLayout.SourceRows, 0)]
    [InlineData(0, SheetLayout.SourceColumns)]
    public void RenderCell_ReportsCoordinatesOutsideTheSheet(int facing, int column)
    {
        using var root = TemporaryDirectory.Create();
        using var strip = Filmstrip(root);

        var rendered = strip.RenderCell(facing, column, scale: 1);

        Assert.False(rendered.IsSuccessful);
        Assert.Equal(BakeFailure.SourceGeometryMismatch, rendered.Error);
    }

    [Fact]
    public void Open_ReportsAMissingFile()
    {
        using var root = TemporaryDirectory.Create();

        var opened = SpriteFilmstrip.Open(root.FullPath / "absent.png");

        Assert.False(opened.IsSuccessful);
        Assert.Equal(BakeFailure.LayerNotFound, opened.Error);
    }

    /// <summary>Art from another pack version is bad input, not a crash.</summary>
    [Fact]
    public void Open_ReportsTheWrongGeometry()
    {
        using var root = TemporaryDirectory.Create();

        var path = root.FullPath / "small.png";

        using (var wrong = new SKBitmap(64, 64))
        using (var image = SKImage.FromBitmap(wrong))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var file = File.Create(path.Value))
        {
            data.SaveTo(file);
        }

        var opened = SpriteFilmstrip.Open(path);

        Assert.False(opened.IsSuccessful);
        Assert.Equal(BakeFailure.LayerGeometryMismatch, opened.Error);
    }

    /// <summary>Disposal is idempotent and use-after-dispose is caught, per the project rule.</summary>
    [Fact]
    public void Dispose_IsIdempotentAndGuardsLaterUse()
    {
        using var root = TemporaryDirectory.Create();

        var strip = Filmstrip(root);

        strip.Dispose();
        strip.Dispose();

        Assert.Throws<ObjectDisposedException>(() => strip.RenderCell(0, 0, 1));
    }
}
