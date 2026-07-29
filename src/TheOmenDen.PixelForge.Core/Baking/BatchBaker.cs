using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Runs many recipes and writes each result to disk.
/// <para>
/// <c>Parallel.ForEachAsync</c> is what bounds this run. The work is synchronous and CPU-bound —
/// <see cref="RecipeBaker.Bake"/> decodes, composites and encodes on the calling thread — so the
/// BCL's data-parallel primitive is the right shape: it partitions, schedules onto the thread
/// pool, bounds by <c>MaxDegreeOfParallelism</c>, and threads cancellation through
/// <see cref="ParallelOptions"/>, without wrapping each bake in a <c>Task.Run</c>.
/// </para>
/// <para>
/// DotNext.Threading was checked first and does offer bounded alternatives: <c>TaskQueue&lt;T&gt;</c>
/// bounds via <c>EnqueueAsync</c>, and <c>AsyncSharedLock</c> is this project's sanctioned throttle
/// in place of <c>SemaphoreSlim(n, n)</c>. Both are the better tool when the throttled work is
/// genuinely async; feeding either one here would mean wrapping synchronous bakes in
/// <c>Task.Run</c> for no gain. <c>TaskCompletionPipe&lt;T&gt;</c> is the one that would be wrong
/// outright — it orders completions but never throttles starts, since the tasks handed to it
/// are already running. A full run is 79 sheets, the 63 flattened ones each decoding four
/// 828 KiB partials.
/// </para>
/// <para>
/// A failed recipe is reported and the run continues. One missing partial must not cost the
/// other 78 sheets.
/// </para>
/// </summary>
public static class BatchBaker
{
    public static async Task<BatchSummary> RunAsync(
        ImmutableArray<SheetRecipe> recipes,
        FullPath outputDirectory,
        IProgress<BakeProgress>? progress,
        int maxParallelism,
        CancellationToken cancellationToken)
    {
        Guard.IsGreaterThan(maxParallelism, 0);

        if (recipes.IsDefaultOrEmpty)
        {
            return new()
            {
                Succeeded = 0,
                Failed = 0,
                TotalWritten = ByteSize.FromBytes(0),
                Cancelled = false,
            };
        }

        var total = recipes.Length;
        var completed = 0;
        var succeeded = 0;
        var failed = 0;
        var written = 0L;
        var cancelled = false;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallelism,
            CancellationToken = cancellationToken,
        };

        try
        {
            await Parallel.ForEachAsync(recipes, options, (recipe, token) =>
            {
                token.ThrowIfCancellationRequested();

                var outcome = BakeOne(recipe, outputDirectory);

                // Interlocked rather than a lock: four independent counters, touched once per
                // recipe. A lock here would serialise the reporting of work already done in
                // parallel.
                var position = Interlocked.Increment(ref completed);
                var size = Optional<ByteSize>.None;

                if (outcome.TryGet(out var actual))
                {
                    Interlocked.Increment(ref succeeded);
                    Interlocked.Add(ref written, actual.Value);
                    size = actual;
                }
                else
                {
                    Interlocked.Increment(ref failed);
                }

                progress?.Report(new()
                {
                    Name = recipe.Name,
                    Written = size,
                    Failure = outcome.Error,
                    Completed = position,
                    Total = total,
                });

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        return new()
        {
            Succeeded = succeeded,
            Failed = failed,
            TotalWritten = ByteSize.FromBytes(written),
            Cancelled = cancelled,
        };
    }

    /// <summary>
    /// Bake and write one recipe. The pooled stream is disposed here so its buffer returns to
    /// the pool before the next recipe on this worker asks for one.
    /// </summary>
    private static Result<ByteSize, BakeFailure> BakeOne(SheetRecipe recipe, FullPath outputDirectory)
    {
        var baked = RecipeBaker.Bake(recipe);

        if (!baked.TryGet(out var sheet))
        {
            return new(baked.Error);
        }

        using (sheet)
        {
            return SheetWriter.Write(outputDirectory, recipe.Name, sheet);
        }
    }
}
