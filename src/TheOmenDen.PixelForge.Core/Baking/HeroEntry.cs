using System.Globalization;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// One row of the hero registry: a body, the directory name it was given, and the run that gave it.
/// </summary>
/// <remarks>
/// <see cref="Prefix"/> and <see cref="Number"/> are carried apart rather than only joined into
/// <see cref="Name"/>, so resolving the next free number never parses a label back into parts — a
/// parse a prefix containing a digit or an underscore would make ambiguous.
/// </remarks>
/// <param name="Prefix">The slugged archetype, such as <c>villager</c>.</param>
/// <param name="Number">The number within that prefix, from 1.</param>
/// <param name="Key">The body this entry names.</param>
/// <param name="AssignedInRun">The run that first minted this number.</param>
public sealed record HeroEntry(string Prefix, int Number, HeroKey Key, Guid AssignedInRun)
{
    /// <summary>
    /// The hero's directory name, such as <c>villager_01</c>.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, so the label format has one definition. Two digits for the
    /// common case and widening naturally past 99 — the hundredth villager is <c>villager_100</c>,
    /// which sorts wrong lexically at that boundary but right in the registry, where the sort key
    /// is the integer.
    /// </remarks>
    public string Name => string.Create(CultureInfo.InvariantCulture, $"{Prefix}_{Number:00}");
}
