namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>
/// Why a catalogue scan produced nothing usable.
/// <para>
/// Both members describe someone's disk rather than a bug — the packs live outside every repo
/// and the user points the app at them — so they travel as
/// <see cref="DotNext.Result{T, TError}"/> values instead of exceptions.
/// </para>
/// <para>
/// Numbering starts at 1 so <see langword="default"/> is never mistaken for a real failure.
/// </para>
/// </summary>
public enum CatalogFailure
{
    /// <summary>One of the three configured pack directories is not on disk.</summary>
    PackDirectoryMissing = 1,

    /// <summary>
    /// Every directory exists but holds no <c>.png</c> in any known slot folder — usually a path
    /// pointing at a pack's root rather than at its <c>assets</c> subdirectory.
    /// <para>
    /// A slot folder missing from one pack is <em>not</em> this failure. Expansion 2 ships no
    /// <c>frontextra</c> and only the core pack ships a <c>shadow</c>, so an absent slot directory
    /// is the normal case and is skipped silently. Only an empty result across all three packs is
    /// evidence that the paths themselves are wrong.
    /// </para>
    /// </summary>
    NoPartialsFound,
}
