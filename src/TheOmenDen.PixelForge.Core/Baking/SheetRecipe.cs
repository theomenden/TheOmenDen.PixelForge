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
}
