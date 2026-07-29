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
        var tally = new RunTally();
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

                BakeAndReport(recipe, outputDirectory, tally, progress, total);

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        return new()
        {
            Succeeded = tally.Succeeded,
            Failed = tally.Failed,
            TotalWritten = ByteSize.FromBytes(tally.Written),
            Cancelled = cancelled,
        };
    }

    /// <summary>
    /// The counters one run accumulates, incremented from several worker threads at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Interlocked"/> rather than a lock: these are independent counters, each touched
    /// once per recipe. A lock would serialise the reporting of work that was deliberately done in
    /// parallel.
    /// </para>
    /// <para>
    /// A type rather than four captured locals so the increments have one named home and the
    /// concurrency contract is stated once, where the fields live, instead of in a comment beside
    /// a lambda.
    /// </para>
    /// </remarks>
    private sealed class RunTally
    {
        private int _completed;
        private int _succeeded;
        private int _failed;
        private long _written;

        /// <summary>How many recipes finished, successfully or not.</summary>
        public int Completed => _completed;

        /// <summary>How many recipes produced a verified sheet.</summary>
        public int Succeeded => _succeeded;

        /// <summary>How many recipes failed.</summary>
        public int Failed => _failed;

        /// <summary>Total bytes written across the run.</summary>
        public long Written => _written;

        /// <summary>Records a written sheet and returns this recipe's completion position.</summary>
        public int RecordSuccess(ByteSize size)
        {
            Interlocked.Increment(ref _succeeded);
            Interlocked.Add(ref _written, size.Value);

            return Interlocked.Increment(ref _completed);
        }

        /// <summary>Records a failed recipe and returns its completion position.</summary>
        public int RecordFailure()
        {
            Interlocked.Increment(ref _failed);

            return Interlocked.Increment(ref _completed);
        }
    }

    /// <summary>
    /// Bakes one recipe, tallies the outcome, and reports it. Runs on a thread-pool worker, so it
    /// touches nothing but <paramref name="tally"/> and the caller's progress sink.
    /// </summary>
    private static void BakeAndReport(
        SheetRecipe recipe,
        FullPath outputDirectory,
        RunTally tally,
        IProgress<BakeProgress>? progress,
        int total)
    {
        var outcome = BakeOne(recipe, outputDirectory);
        var size = Optional<ByteSize>.None;
        var position = 0;

        if (outcome.TryGet(out var actual))
        {
            size = actual;
            position = tally.RecordSuccess(actual);
        }
        else
        {
            position = tally.RecordFailure();
        }

        progress?.Report(new()
        {
            Name = recipe.Name,
            Written = size,
            Failure = outcome.Error,
            Completed = position,
            Total = total,
        });
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
