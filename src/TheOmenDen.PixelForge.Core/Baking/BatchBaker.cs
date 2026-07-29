using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Runs many recipes and writes each result to disk.
/// <para>
/// <c>Parallel.ForEachAsync</c> is what bounds this. DotNext's <c>TaskCompletionPipe&lt;T&gt;</c>
/// was the obvious candidate — it streams results in completion order and carries a correlation
/// token — but it does not bound concurrency: every task added starts immediately. A full
/// flattened run is 63 sheets, each decoding four 828 KiB partials, so unbounded start is the
/// memory failure mode. <c>Parallel.ForEachAsync</c> bounds <em>and</em> reports on completion,
/// so no throttle primitive is needed and none of the banned synchronisation types appear.
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
            return Empty(cancelled: false);
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
                    Failure = outcome.IsSuccessful ? default : outcome.Error,
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

    private static BatchSummary Empty(bool cancelled) => new()
    {
        Succeeded = 0,
        Failed = 0,
        TotalWritten = ByteSize.FromBytes(0),
        Cancelled = cancelled,
    };

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
