using Meziantou.Framework;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// One partial in a recipe's draw order, and whether the skin substitution applies to it.
/// </summary>
/// <param name="Path">Absolute path to the partial.</param>
/// <param name="IsSkin">
/// <see langword="true"/> when this layer carries skin and must take the recipe's tone.
/// Seeded from <see cref="Catalog.AssetSlots.IsSkinBearing"/>, but carried per layer rather than
/// looked up per slot — that is the escape hatch for excluding a single partial later without
/// touching the baker or reclassifying its whole slot.
/// </param>
/// <remarks>
/// <para>
/// Named <c>IsSkin</c> rather than something like <c>Recolor</c> on purpose: the layer states
/// whether it <em>carries skin</em>, while <see cref="SheetRecipe.Tone"/> states which tone to
/// apply. A layer is substituted when both are set. Naming both ends the same thing would read as
/// one switch expressed in two places.
/// </para>
/// </remarks>
public readonly record struct AssetLayer(FullPath Path, bool IsSkin);
