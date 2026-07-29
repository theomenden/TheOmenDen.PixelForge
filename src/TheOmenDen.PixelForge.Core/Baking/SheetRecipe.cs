using System.Collections.Immutable;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// One output sheet: the layer partials that make it, in back-to-front draw order, and the
/// skin ramp to recolour the assembly into.
/// </summary>
public sealed record SheetRecipe
{
    /// <summary>Output stem, e.g. <c>body-01</c>. The <c>.webp</c> is added when written.</summary>
    public required string Name { get; init; }

    /// <summary>Layer partials, back to front, following the generator's CharacterLayers order.</summary>
    public required ImmutableArray<FullPath> Layers { get; init; }

    /// <summary>
    /// Absent for hair, which keeps its authored colour. <see cref="Optional{T}"/> rather than
    /// a nullable reference so "no recolour" is a value the type system carries, not a null
    /// every caller has to remember to check.
    /// </summary>
    public Optional<SkinRamp> Recolor { get; init; } = Optional<SkinRamp>.None;

    /// <summary>
    /// Layers drawn <em>after</em> the recolour, so their authored colours survive it. Empty
    /// for layered output.
    /// <para>
    /// This is what makes flattening safe. <see cref="RoostSheets"/> records that some hair
    /// partials legitimately use skin-ramp hexes as hair and trim; compositing those before
    /// the substitution would recolour them along with the face.
    /// </para>
    /// </summary>
    public ImmutableArray<FullPath> Overlays { get; init; } = [];
}
