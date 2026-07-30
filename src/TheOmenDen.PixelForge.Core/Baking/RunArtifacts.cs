using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Writes everything a run leaves beside its sheets: the index for each geometry produced, the
/// tabular run manifest, and the JSON one.
/// </summary>
/// <remarks>
/// <para>
/// Lifted out of the export view model so it is reachable without a window. It was the one part of
/// the pipeline with no coverage at all — every writer beneath it is tested, but the rule deciding
/// <em>which</em> of them run, and the run id they share, lived in a Windows-only type that the
/// test project cannot reference.
/// </para>
/// <para>
/// Failures are returned rather than thrown or logged. A manifest that does not land is worth
/// telling a user about, but it must not abort the rest — the sheets are already on disk by the
/// time this is called, and losing the index for one geometry should not cost the other.
/// </para>
/// </remarks>
public static class RunArtifacts
{
    /// <summary>
    /// Writes every artifact the run calls for.
    /// </summary>
    /// <param name="directory">Where the run's sheets were written.</param>
    /// <param name="runId">Stamped into both manifests, from <see cref="BatchManifest.NewRunId"/>.</param>
    /// <param name="recipes">The recipes the run was given, in bake order.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>
    /// One <see cref="ArtifactFailure"/> per manifest that did not land, empty when all did.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A geometry's index is written only when the run actually produced that geometry, so a
    /// curated-only export does not leave a <c>clips.csv</c> describing files that are not there.
    /// <see cref="RunManifest"/> applies the same rule to its <c>layouts</c> object.
    /// </para>
    /// <para>
    /// The run id is minted by the caller and handed to both manifests. Generating it per writer
    /// would stamp <c>sheets.csv</c> and <c>manifest.json</c> with different ids for one run —
    /// which nothing would catch, because both files would still look perfectly well-formed.
    /// </para>
    /// </remarks>
    public static async Task<ImmutableArray<ArtifactFailure>> WriteAllAsync(
        FullPath directory,
        Guid runId,
        ImmutableArray<SheetRecipe> recipes,
        CancellationToken cancellationToken = default)
    {
        Guard.IsFalse(recipes.IsDefault);

        var failures = ImmutableArray.CreateBuilder<ArtifactFailure>();

        // Written one after another rather than concurrently: four small files, and a run whose
        // manifests raced would report their failures in a different order each time.
        if (recipes.AsSpan().Any(static recipe => recipe.Geometry is SheetGeometry.Curated))
        {
            Record(failures, SheetIndex.FileName, await SheetIndex.WriteToAsync(directory, cancellationToken));
        }

        if (recipes.AsSpan().Any(static recipe => recipe.Geometry is SheetGeometry.Full))
        {
            Record(failures, ClipIndex.FileName, await ClipIndex.WriteToAsync(directory, cancellationToken));
        }

        Record(
            failures,
            BatchManifest.FileName,
            await BatchManifest.WriteToAsync(
                directory,
                runId,
                BatchManifest.RowsFor(recipes),
                cancellationToken));

        Record(
            failures,
            RunManifest.FileName,
            await RunManifest.WriteToAsync(directory, runId, recipes, cancellationToken));

        return failures.ToImmutable();
    }

    private static void Record(
        ImmutableArray<ArtifactFailure>.Builder failures,
        string file,
        DotNext.Result<int, BakeFailure> written)
    {
        if (!written.IsSuccessful)
        {
            failures.Add(new(file, written.Error));
        }
    }
}
