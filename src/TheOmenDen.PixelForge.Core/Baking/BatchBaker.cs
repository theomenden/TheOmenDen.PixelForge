using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Runs many recipes and writes each result to disk.
/// <para>
/// <c>Parallel.ForAsync</c> is what bounds this run. The work is synchronous and CPU-bound —
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
/// are already running.
/// </para>
/// <para>
/// Results are index-addressed: each worker writes only its own slot, so nothing about the
/// outcomes needs synchronising and the summary is a plain fold once the run has stopped. The
/// only shared state left is the progress position counter.
/// </para>
/// <para>
/// That counter stays <see cref="Interlocked"/> because DotNext has nothing better for it.
/// <c>DotNext.Threading.Atomic</c> supplies <c>AccumulateAndGet</c>, <c>UpdateAndGet</c>,
/// <c>GetAndUpdate</c>, <c>GetOrSet</c>, <c>TrySet</c> and volatile <c>Read</c>/<c>Write</c> —
/// the update-through-a-function cases the BCL handles poorly — but no <c>IncrementAndGet</c>.
/// Writing <c>++</c> as <c>UpdateAndGet(ref x, static v =&gt; v + 1)</c> would be a compare-and-swap
/// loop through a delegate, strictly worse than the intrinsic.
/// </para>
/// <para>
/// A failed recipe is reported and the run continues. One missing partial must not cost the
/// rest of the batch.
/// </para>
/// </summary>
public static class BatchBaker
{
    public static async Task<Result<BatchSummary, BakeFailure>> RunAsync(
        ImmutableArray<SheetRecipe> recipes,
        FullPath outputDirectory,
        IProgress<BakeProgress>? progress,
        int maxParallelism,
        CancellationToken cancellationToken)
    {
        Guard.IsGreaterThan(maxParallelism, 0);

        if (recipes.IsDefaultOrEmpty)
        {
            // An empty run is an empty fold, so it needs no separate zero literal to drift.
            return Summarize([], cancelled: false);
        }

        if (Prepare(outputDirectory, recipes).TryGet(out var unusable))
        {
            return new(unusable);
        }

        var total = recipes.Length;
        var cancelled = false;

        // One slot per recipe, written only by the worker that owns that index, so the run needs
        // no synchronisation over its results at all — the summary is a fold once everything has
        // stopped. Null means "never ran", which is how a cancelled run stays countable.
        var outcomes = new Result<ByteSize, BakeFailure>?[total];

        // The one genuinely shared counter — see the type's remarks for why it stays Interlocked.
        var completed = 0;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallelism,
            CancellationToken = cancellationToken,
        };

        try
        {
            await Parallel.ForAsync(0, total, options, (index, token) =>
            {
                token.ThrowIfCancellationRequested();

                var recipe = recipes[index];
                var outcome = BakeOne(recipe, outputDirectory);

                outcomes[index] = outcome;

                progress?.Report(new()
                {
                    Name = recipe.Name,
                    Written = outcome.TryGet(out var size) ? size : Optional<ByteSize>.None,
                    Failure = outcome.Error,
                    Completed = Interlocked.Increment(ref completed),
                    Total = total,
                });

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        return Summarize(outcomes, cancelled);
    }

    /// <summary>
    /// Creates every directory the run will write into, before any of it is baked.
    /// </summary>
    /// <returns>
    /// <see cref="BakeFailure.OutputDirectoryCreateFailed"/> for the first destination that cannot
    /// be created, or nothing when they all can.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Single-threaded and up front, rather than on demand inside each worker.
    /// <c>Directory.CreateDirectory</c> is idempotent and safe to call concurrently, so the racy
    /// version would also work — but a root that cannot be written into is a property of the run,
    /// not of a sheet, and failing once before any decode beats failing per recipe after all of
    /// them. It also means <see cref="SheetWriter"/> keeps its existing contract: it reports a
    /// missing directory, it does not create one.
    /// </para>
    /// <para>
    /// Deduplicated on the recipe's relative directory with <see cref="StringComparer.Ordinal"/>.
    /// A case-insensitive comparer would be defensible on Windows, but every directory here is
    /// generated lowercase, and the cost of being wrong is one redundant idempotent call.
    /// </para>
    /// </remarks>
    private static Optional<BakeFailure> Prepare(FullPath root, ImmutableArray<SheetRecipe> recipes)
    {
        // The root is the folder the user picked, not one to invent. If it has been moved or
        // deleted since, recreating it would quietly scatter the run into a stale location — so a
        // missing root stays a failure, and only the subdirectories beneath it are created.
        if (!Directory.Exists(root.Value))
        {
            return BakeFailure.OutputDirectoryUnavailable;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var recipe in recipes)
        {
            if (recipe.Directory.Length is 0 || !seen.Add(recipe.Directory))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(Destination(root, recipe).Value);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                return BakeFailure.OutputDirectoryCreateFailed;
            }
        }

        return Optional<BakeFailure>.None;
    }

    /// <summary>
    /// Folds the per-recipe outcomes into the run summary. Entries left <see langword="null"/> are
    /// recipes a cancelled run never reached, and are counted as neither succeeded nor failed.
    /// </summary>
    private static BatchSummary Summarize(Result<ByteSize, BakeFailure>?[] outcomes, bool cancelled)
    {
        var succeeded = 0;
        var failed = 0;
        var written = 0L;

        foreach (var outcome in outcomes)
        {
            if (outcome is not { } settled)
            {
                continue;
            }

            if (settled.TryGet(out var size))
            {
                succeeded++;
                written += size.Value;
            }
            else
            {
                failed++;
            }
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
    private static Result<ByteSize, BakeFailure> BakeOne(SheetRecipe recipe, FullPath root)
    {
        var baked = RecipeBaker.Bake(recipe);

        if (!baked.TryGet(out var sheet))
        {
            return new(baked.Error);
        }

        using (sheet)
        {
            return SheetWriter.Write(Destination(root, recipe), recipe.Name, sheet, recipe.Format);
        }
    }

    /// <summary>
    /// Where one recipe's sheet lands: the export root, or a subdirectory of it.
    /// </summary>
    /// <remarks>
    /// <c>FullPath</c>'s <c>/</c> is what normalises the recipe's forward slashes onto the
    /// platform's separator, so the same relative string can be written verbatim into the manifests
    /// and still resolve on disk here.
    /// </remarks>
    private static FullPath Destination(FullPath root, SheetRecipe recipe) =>
        recipe.Directory.Length is 0 ? root : root / recipe.Directory;
}
