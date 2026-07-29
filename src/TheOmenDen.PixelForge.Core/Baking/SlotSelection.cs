using System.Collections.Immutable;
using DotNext;
using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// What the user ticked for one slot.
/// </summary>
/// <remarks>
/// <para>
/// A choice is <see cref="Optional{T}"/> so that "wear no hat" is a first-class alternative
/// alongside the hats themselves. Including <see cref="Optional{T}.None"/> in an optional slot is
/// how one run produces both a hatted and a hatless character; including it in a required slot
/// whose siblings are definitely filled is <see cref="PlanFailure.RequiredSlotEmpty"/>.
/// </para>
/// <para>
/// Colour variants are ordinary choices here. The picker's per-slot "include colour variants"
/// toggle is what expands a ticked base into its <c>_cN</c> siblings before building this.
/// </para>
/// <para>
/// A selection with no choices at all is not an error, it is simply nothing:
/// <see cref="BatchPlan"/> drops it rather than multiplying the whole cross product by zero.
/// </para>
/// </remarks>
public sealed record SlotSelection
{
    /// <summary>Which slot these choices fill.</summary>
    public required AssetSlot Slot { get; init; }

    /// <summary>The chosen partials, plus <see cref="Optional{T}.None"/> to mean "leave empty".</summary>
    public required ImmutableArray<Optional<AssetPartial>> Choices { get; init; }
}
