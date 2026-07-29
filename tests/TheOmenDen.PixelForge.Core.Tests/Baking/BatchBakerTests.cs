using System.Collections.Immutable;
using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

public sealed class BatchBakerTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    /// <summary>Collects reports synchronously. Progress&lt;T&gt; posts asynchronously, which
    /// would make the count assertions racy.</summary>
    private sealed class CollectingProgress : IProgress<BakeProgress>
    {
        private readonly Lock _gate = new();

        public List<BakeProgress> Reports { get; } = [];

        public void Report(BakeProgress value)
        {
            lock (_gate)
            {
                Reports.Add(value);
            }
        }
    }

    /// <summary>Cancels <paramref name="source"/> the moment the <paramref name="cancelAfter"/>-th
    /// report arrives, so a test can assert on state mid-run rather than only at the boundary
    /// where every counter is trivially zero.</summary>
    private sealed class CancelingProgress(CancellationTokenSource source, int cancelAfter) : IProgress<BakeProgress>
    {
        private readonly Lock _gate = new();
        private int _count;

        public void Report(BakeProgress value)
        {
            lock (_gate)
            {
                _count++;

                if (_count == cancelAfter)
                {
                    source.Cancel();
                }
            }
        }
    }

    private FullPath WritePartial(string name)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            SheetLayout.SourceWidth, SheetLayout.SourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var pixels = bitmap.Pixels;
        Array.Fill(pixels, new SKColor(0x20, 0x40, 0x60, 0xFF));
        bitmap.Pixels = pixels;

        var path = _directory.FullPath / name;

        using var stream = File.Create(path.Value);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        data.SaveTo(stream);

        return path;
    }

    private FullPath OutputDirectory()
    {
        var output = _directory.FullPath / "out";

        Directory.CreateDirectory(output.Value);

        return output;
    }

    private ImmutableArray<SheetRecipe> GoodRecipes(int count)
    {
        var layer = WritePartial("layer.png");
        var recipes = ImmutableArray.CreateBuilder<SheetRecipe>(count);

        for (var i = 0; i < count; i++)
        {
            recipes.Add(new() { Name = $"sheet-{i:00}", Layers = [layer] });
        }

        return recipes.ToImmutable();
    }

    [Fact]
    public async Task RunAsync_WritesOneFilePerRecipe()
    {
        var output = OutputDirectory();

        var summary = await BatchBaker.RunAsync(
            GoodRecipes(3), output, null, 2, TestContext.Current.CancellationToken);

        Assert.Equal(3, summary.Succeeded);
        Assert.Equal(0, summary.Failed);
        Assert.False(summary.Cancelled);
        Assert.Equal(3, Directory.GetFiles(output.Value, "*.webp").Length);
    }

    [Fact]
    public async Task RunAsync_ReportsProgressOncePerRecipe()
    {
        var output = OutputDirectory();
        var progress = new CollectingProgress();

        var summary = await BatchBaker.RunAsync(
            GoodRecipes(4), output, progress, 2, TestContext.Current.CancellationToken);

        Assert.Equal(4, summary.Succeeded);
        Assert.Equal(4, progress.Reports.Count);
        Assert.All(progress.Reports, r => Assert.Equal(4, r.Total));
        Assert.All(progress.Reports, r => Assert.True(r.IsSuccess));

        // Completed is a running position, so the set must be exactly 1..4 regardless of order.
        // ZLinq's drop-in returns a ref-struct ValueEnumerable here, which does not implicitly
        // convert to IEnumerable<int> — materialise before handing it to Assert.Equal.
        Assert.Equal([1, 2, 3, 4], progress.Reports.Select(static r => r.Completed).Order().ToArray());
    }

    /// <summary>One missing partial must not abort the sheets that are fine.</summary>
    [Fact]
    public async Task RunAsync_ContinuesPastAFailedRecipe()
    {
        var output = OutputDirectory();

        var recipes = GoodRecipes(2).Add(new SheetRecipe
        {
            Name = "broken",
            Layers = [_directory.FullPath / "absent.png"],
        });

        var summary = await BatchBaker.RunAsync(
            recipes, output, null, 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, summary.Succeeded);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(2, Directory.GetFiles(output.Value, "*.webp").Length);
    }

    [Fact]
    public async Task RunAsync_ReportsTheFailureReason_ForABrokenRecipe()
    {
        var output = OutputDirectory();
        var progress = new CollectingProgress();

        var recipes = ImmutableArray.Create(new SheetRecipe
        {
            Name = "broken",
            Layers = [_directory.FullPath / "absent.png"],
        });

        await BatchBaker.RunAsync(recipes, output, progress, 1, TestContext.Current.CancellationToken);

        var report = Assert.Single(progress.Reports);

        Assert.Equal("broken", report.Name);
        Assert.Equal(BakeFailure.LayerNotFound, report.Failure);
        Assert.False(report.IsSuccess);
        Assert.False(report.Written.HasValue);
    }

    [Fact]
    public async Task RunAsync_ReportsCancelled_WhenTheTokenIsAlreadyCancelled()
    {
        var output = OutputDirectory();

        using var cts = new CancellationTokenSource();

        await cts.CancelAsync();

        var summary = await BatchBaker.RunAsync(GoodRecipes(4), output, null, 2, cts.Token);

        Assert.True(summary.Cancelled);
    }

    /// <summary>
    /// Cancelling before the run starts leaves every counter at zero by construction, which
    /// cannot distinguish a correct cancellation from one that discards already-written sheets.
    /// This cancels mid-run instead and checks the partial results survive.
    /// </summary>
    [Fact]
    public async Task RunAsync_KeepsAlreadyWrittenSheets_WhenCancelledMidRun()
    {
        var output = OutputDirectory();

        using var cts = new CancellationTokenSource();

        const int total = 6;
        const int cancelAfter = 2;

        var progress = new CancelingProgress(cts, cancelAfter);

        // DOP 1 makes this deterministic: nothing else can be in flight when the token is
        // observed, so exactly cancelAfter recipes finish before the run stops.
        var summary = await BatchBaker.RunAsync(GoodRecipes(total), output, progress, 1, cts.Token);

        Assert.True(summary.Cancelled);
        Assert.True(summary.Succeeded >= cancelAfter,
            $"expected at least {cancelAfter} succeeded, got {summary.Succeeded}");
        Assert.True(Directory.GetFiles(output.Value, "*.webp").Length >= cancelAfter);
    }

    [Fact]
    public async Task RunAsync_FailsEveryRecipe_WhenTheOutputDirectoryIsMissing()
    {
        var missing = _directory.FullPath / "no-such-dir";

        var summary = await BatchBaker.RunAsync(
            GoodRecipes(3), missing, null, 2, TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.Succeeded);
        Assert.Equal(3, summary.Failed);
    }

    [Fact]
    public async Task RunAsync_SumsTheBytesWritten()
    {
        var output = OutputDirectory();

        var summary = await BatchBaker.RunAsync(
            GoodRecipes(3), output, null, 2, TestContext.Current.CancellationToken);

        var onDisk = Directory.GetFiles(output.Value, "*.webp").Sum(static f => new FileInfo(f).Length);

        Assert.Equal(onDisk, summary.TotalWritten.Value);
    }

    [Fact]
    public async Task RunAsync_ReturnsAnEmptySummary_WhenGivenNoRecipes()
    {
        var summary = await BatchBaker.RunAsync(
            [], OutputDirectory(), null, 2, TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.Succeeded);
        Assert.Equal(0, summary.Failed);
        Assert.False(summary.Cancelled);
    }

    /// <summary>
    /// Proves <c>MaxDegreeOfParallelism</c> is actually wired into <c>ParallelOptions</c> rather
    /// than merely accepted and ignored — the one property here with otherwise-zero coverage.
    /// <para>
    /// Recipes carry <em>descending</em> cost: the first submitted decodes the most layers, the
    /// last submitted decodes only one. With DOP pinned to 1, only one recipe is ever in flight,
    /// so completion order can only ever match submission order, however unequal the cost. With
    /// the bound removed, real concurrency lets the cheap, late recipe finish well ahead of the
    /// expensive, early one — flipping that order, which this test would then catch.
    /// </para>
    /// </summary>
    [Fact]
    public async Task RunAsync_ProcessesStrictlyInSubmissionOrder_WhenParallelismIsOne()
    {
        var output = OutputDirectory();
        var progress = new CollectingProgress();
        var layer = WritePartial("layer.png");

        const int count = 6;
        var recipes = ImmutableArray.CreateBuilder<SheetRecipe>(count);

        for (var i = 0; i < count; i++)
        {
            var layers = ImmutableArray.CreateBuilder<FullPath>(count - i);

            for (var j = 0; j < count - i; j++)
            {
                layers.Add(layer);
            }

            recipes.Add(new() { Name = $"sheet-{i:00}", Layers = layers.ToImmutable() });
        }

        var summary = await BatchBaker.RunAsync(
            recipes.ToImmutable(), output, progress, 1, TestContext.Current.CancellationToken);

        Assert.Equal(count, summary.Succeeded);
        Assert.Equal([1, 2, 3, 4, 5, 6], progress.Reports.Select(static r => r.Completed).ToArray());
    }
}
