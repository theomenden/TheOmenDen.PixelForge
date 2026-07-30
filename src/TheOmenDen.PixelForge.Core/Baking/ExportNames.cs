using System.Collections.Immutable;
using Meziantou.Framework;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Turns what a user types into a directory name, and refuses the ones that would collide.
/// </summary>
/// <remarks>
/// <see cref="Slug"/> rather than hand-sanitising, the same choice <see cref="BatchPlan"/> already
/// makes for tone segments — it covers arbitrary text, which is what a free-form box produces.
/// </remarks>
public static class ExportNames
{
    /// <summary>
    /// How a typed name becomes a directory segment.
    /// </summary>
    /// <remarks>
    /// The same options the tone segment uses, so <c>villager</c> and <c>Villager Guard</c> slug
    /// the way a reader of either name would expect, and nothing ends in a stray separator.
    /// </remarks>
    private const char Separator = '-';

    private static readonly SlugOptions Options = new()
    {
        Separator = "-",
        CanEndWithSeparator = false,
        CasingTransformation = CasingTransformation.ToLowerCase,
    };

    /// <summary>
    /// Names the export tree owns, which a hero prefix or class must not become.
    /// </summary>
    /// <remarks>
    /// Derived from the constants rather than restated, so adding a fixed directory cannot leave a
    /// hole here. <c>curated</c> is the deliverable's folder and the other three are the layer
    /// tree's own.
    /// </remarks>
    public static ImmutableArray<string> Reserved { get; } =
    [
        "curated",
        LayerPlan.HeroesFolder,
        LayerPlan.AttachmentsFolder,
        LoadoutWriter.Folder,
    ];

    /// <summary>The slug a typed name produces, or empty when it produces none.</summary>
    /// <param name="typed">Whatever the user entered.</param>
    /// <returns>The slug, lowercase and separator-free at both ends.</returns>
    /// <remarks>
    /// <para>
    /// The leading trim is not decoration, and it is deliberately applied to the <em>result</em>
    /// rather than the input. <see cref="SlugOptions"/> offers
    /// <see cref="SlugOptions.CanEndWithSeparator"/> and — checked against 2.0.0, not only the
    /// version that happened to be cached — no counterpart for the other end. Every leading
    /// character the slug drops leaves a separator behind, and whitespace is only the most obvious
    /// source: <c>"🗡️ranger"</c> slugs to <c>-ranger</c> and <c>"..\..\escape"</c> to
    /// <c>-escape</c>. Trimming the input would have caught the first case and neither of the
    /// others.
    /// </para>
    /// <para>
    /// That the traversal attempt comes back as <c>escape</c> is worth stating: a typed name cannot
    /// climb out of the export folder, because separators and dots are not in the allowed ranges to
    /// begin with. The slug is the boundary, not a check bolted beside one.
    /// </para>
    /// <para>
    /// <see cref="SlugOptions.MaximumLength"/> is left at the library's default of 80, which is
    /// already a tighter bound on a directory segment than anything worth restating here.
    /// </para>
    /// </remarks>
    public static string Slugged(string? typed) =>
        string.IsNullOrWhiteSpace(typed)
            ? string.Empty
            : Slug.Create(typed, Options).TrimStart(Separator);

    /// <summary>
    /// Whether a typed name can be used as a hero prefix or class name.
    /// </summary>
    /// <param name="typed">Whatever the user entered.</param>
    /// <returns>
    /// <see langword="false"/> when it is blank, slugs to nothing, or slugs to a name the tree
    /// already owns.
    /// </returns>
    /// <remarks>
    /// Refusing rather than silently suffixing: a directory whose name differs from what was typed
    /// is the kind of thing noticed three runs later.
    /// </remarks>
    public static bool IsUsable(string? typed)
    {
        var slug = Slugged(typed);

        return slug.Length is not 0 && !Reserved.AsSpan().Contains(slug, StringComparer.Ordinal);
    }
}
