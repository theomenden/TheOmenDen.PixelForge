namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>
/// How one partial orders against another within its slot.
/// <para>
/// Pack file names are numbered, so plain string ordering is wrong everywhere — it puts
/// <c>hair10</c> before <c>hair2</c>. Rather than reach for a natural-sort comparer (none of the
/// referenced Meziantou packages ships one, the BCL has no portable one, and
/// <c>StrCmpLogicalW</c> is Win32 which <c>Core</c> may not use), the catalogue already
/// decomposes each name while parsing it. Ordering by the parts is correct by construction and
/// costs nothing extra.
/// </para>
/// <para>
/// The <see cref="Suffix"/> is what keeps <c>shield1L</c> and <c>shield1R</c> adjacent and
/// deterministic, and <see cref="Number"/> is -1 for names carrying no digits at all
/// (<c>daggers</c>), sorting them ahead of numbered siblings sharing a prefix.
/// </para>
/// </summary>
/// <param name="Prefix">The leading run of non-digit characters, e.g. <c>shield</c>.</param>
/// <param name="Number">
/// The first run of digits as an integer, or <c>-1</c> when the name carries none.
/// </param>
/// <param name="Suffix">Whatever follows the number, e.g. <c>L</c>. Empty when nothing does.</param>
/// <param name="Variant">
/// The <c>_cN</c> colour variant, <c>0</c> for the base file, so a base always precedes its own
/// variants.
/// </param>
public readonly record struct AssetSortKey(string Prefix, int Number, string Suffix, int Variant)
    : IComparable<AssetSortKey>
{
    /// <summary>Orders <paramref name="left"/> before <paramref name="right"/>.</summary>
    /// <param name="left">The key on the left of the operator.</param>
    /// <param name="right">The key on the right of the operator.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> sorts first.</returns>
    /// <remarks>
    /// The four relational operators exist because CA1036 is escalated to a build error and
    /// requires them of anything implementing <see cref="IComparable{T}"/>. Each simply forwards
    /// to <see cref="CompareTo"/>, which stays the single definition of the order.
    /// </remarks>
    public static bool operator <(AssetSortKey left, AssetSortKey right) => left.CompareTo(right) < 0;

    /// <summary>Orders <paramref name="left"/> before <paramref name="right"/> or equal to it.</summary>
    /// <param name="left">The key on the left of the operator.</param>
    /// <param name="right">The key on the right of the operator.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> does not sort after.</returns>
    public static bool operator <=(AssetSortKey left, AssetSortKey right) => left.CompareTo(right) <= 0;

    /// <summary>Orders <paramref name="left"/> after <paramref name="right"/>.</summary>
    /// <param name="left">The key on the left of the operator.</param>
    /// <param name="right">The key on the right of the operator.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> sorts last.</returns>
    public static bool operator >(AssetSortKey left, AssetSortKey right) => left.CompareTo(right) > 0;

    /// <summary>Orders <paramref name="left"/> after <paramref name="right"/> or equal to it.</summary>
    /// <param name="left">The key on the left of the operator.</param>
    /// <param name="right">The key on the right of the operator.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> does not sort first.</returns>
    public static bool operator >=(AssetSortKey left, AssetSortKey right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public int CompareTo(AssetSortKey other)
    {
        var byPrefix = string.CompareOrdinal(Prefix, other.Prefix);

        if (byPrefix is not 0)
        {
            return byPrefix;
        }

        if (Number != other.Number)
        {
            return Number.CompareTo(other.Number);
        }

        var bySuffix = string.CompareOrdinal(Suffix, other.Suffix);

        if (bySuffix is not 0)
        {
            return bySuffix;
        }

        return Variant.CompareTo(other.Variant);
    }
}
