using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using Meziantou.Framework;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Finds sheets an export folder still holds that the run just finished did not write.
/// </summary>
/// <remarks>
/// <para>
/// The original bug: a second run overwrote every manifest while leaving the first run's sheets on
/// disk, so those files became unindexed — present, and described by nothing. Re-running the same
/// selection is already idempotent, because it writes the same file names, so orphans only appear
/// when a selection <em>shrinks</em>.
/// </para>
/// <para>
/// Reports rather than deletes. The files are the user's, and a stale sheet named in a notice is a
/// far better outcome than a wanted one silently removed.
/// </para>
/// </remarks>
public static class OrphanScan
{
    /// <summary>
    /// The sheets on disk beneath the run's own directories that this run did not produce.
    /// </summary>
    /// <param name="root">The export root.</param>
    /// <param name="recipes">The recipes the run wrote, as planned.</param>
    /// <returns>
    /// Root-relative, forward-slash paths, ordered so a notice reads the same way twice. Empty
    /// when nothing is stale.
    /// </returns>
    /// <remarks>
    /// Walks only <see cref="LayerPlan.HeroesFolder"/> and <see cref="LayerPlan.AttachmentsFolder"/>
    /// — the two this run owns. <c>curated/</c> is deliberately excluded: it is written by a
    /// different command against its own manifests, so including it would report the whole
    /// deliverable as orphaned on every batch export.
    /// </remarks>
    public static ImmutableArray<string> Find(FullPath root, ImmutableArray<SheetRecipe> recipes)
    {
        Guard.IsFalse(recipes.IsDefault);

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var recipe in recipes)
        {
            written.Add(recipe.RelativePath);
        }

        var found = ImmutableArray.CreateBuilder<string>();

        foreach (var folder in (string[])[LayerPlan.HeroesFolder, LayerPlan.AttachmentsFolder])
        {
            Collect(root, folder, written, found);
        }

        found.Sort(StringComparer.Ordinal);

        return found.ToImmutable();
    }

    /// <summary>Adds every sheet under one folder that the run did not write.</summary>
    private static void Collect(
        FullPath root,
        string folder,
        HashSet<string> written,
        ImmutableArray<string>.Builder found)
    {
        var directory = new DirectoryInfo((root / folder).Value);

        if (!directory.Exists)
        {
            return;
        }

        // Descendants, not Children: heroes/<hero>/ and attachments/<slot>/ are both a level down.
        // ZLinq.FileSystem's value-enumerable walk, this project's replacement for
        // Directory.EnumerateFiles + LINQ.
        foreach (var entry in directory.Descendants())
        {
            if (entry is not FileInfo file
                || !file.Name.EndsWith(SheetWriter.Extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Relative(root, FullPath.FromPath(file.FullName));

            if (!written.Contains(relative))
            {
                found.Add(relative);
            }
        }
    }

    /// <summary>
    /// One file's path relative to the export root, in the same forward-slash form
    /// <see cref="SheetRecipe.RelativePath"/> writes into the manifests.
    /// </summary>
    /// <remarks>
    /// Both halves matter. <c>MakePathRelativeTo</c> is what turns an absolute path back into the
    /// manifest's vocabulary, and the separator swap is what lets the comparison succeed at all —
    /// on Windows the walk yields backslashes and the manifests never do.
    /// </remarks>
    private static string Relative(FullPath root, FullPath file) =>
        file.MakePathRelativeTo(root).Replace('\\', '/');
}
