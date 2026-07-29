namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Why a selection cannot be turned into recipes. Every member describes a user's choice rather
/// than a bug, so these travel as <see cref="DotNext.Result{T, TError}"/> values.
/// <para>Numbering starts at 1 so <see langword="default"/> is never a real failure.</para>
/// </summary>
public enum PlanFailure
{
    /// <summary>No slot has anything ticked.</summary>
    NothingSelected = 1,

    /// <summary>
    /// The body is half-committed: at least one slot the generator marks non-optional — bottom,
    /// top or head — is definitely filled while another is left empty or offers <c>(none)</c>.
    /// A character without a head is not a sheet.
    /// <para>
    /// Committing to <em>no</em> part of the body is legal and is not this failure: a run that
    /// ticks only hair produces the standalone overlay sheets the layered contract is built on.
    /// The rule is all-or-nothing, not always-on.
    /// </para>
    /// </summary>
    RequiredSlotEmpty,
}
