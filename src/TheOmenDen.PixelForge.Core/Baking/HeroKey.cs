namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// What makes one hero distinct from another: the three required partials, by stem.
/// </summary>
/// <remarks>
/// <para>
/// Tone is deliberately absent. A hero is a body, and the same body in seven skins is one hero with
/// seven sheets — tone is a filename suffix, not identity. Putting it here would multiply hero
/// directories sevenfold and make <c>heroes.json</c> claim seven characters where there is one.
/// </para>
/// <para>
/// Keyed on <see cref="Catalog.AssetSlots.IsRequired"/>, not
/// <see cref="Catalog.AssetSlots.IsSkinBearing"/>. They name the same three slots today, and
/// <c>AssetSlots</c> says outright that this is not a coincidence worth collapsing: they are
/// separate questions and a future pack could separate them. The hero is the body, so it is the
/// required trio that decides.
/// </para>
/// <para>
/// A <see langword="readonly"/> <see langword="record"/> <see langword="struct"/> so it can be a
/// dictionary key without allocating, with the structural equality that lookup needs supplied by
/// the compiler rather than hand-written.
/// </para>
/// </remarks>
/// <param name="Bottom">Stem of the bottom partial.</param>
/// <param name="Top">Stem of the top partial.</param>
/// <param name="Head">Stem of the head partial.</param>
public readonly record struct HeroKey(string Bottom, string Top, string Head);
