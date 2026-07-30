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
    /// <returns>The slug, lowercase and separator-safe at both ends.</returns>
    /// <remarks>
    /// The trim is not decoration. <see cref="SlugOptions"/> offers
    /// <see cref="SlugOptions.CanEndWithSeparator"/> and no counterpart for the other end — checked,
    /// and it is genuinely absent — so <c>"  Ranger  "</c> slugs to <c>-ranger</c> and would name a
    /// directory with a leading dash. Trailing space is the library's problem and it solves it;
    /// leading space is ours.
    /// </remarks>
    public static string Slugged(string? typed) =>
        string.IsNullOrWhiteSpace(typed) ? string.Empty : Slug.Create(typed.Trim(), Options);

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
