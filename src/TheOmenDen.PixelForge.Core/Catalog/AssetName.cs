using System.Globalization;
using CommunityToolkit.Diagnostics;

namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>
/// The file-name grammar the Time Elements packs use, and the ordering key that falls out of it.
/// <para>
/// A partial is <c>&lt;base&gt;.png</c> or <c>&lt;base&gt;_c&lt;n&gt;.png</c>. The <c>_cN</c>
/// files are colour variants, verified by pixel diff: <c>top1_c1..c4</c> change garment pixels
/// and leave every skin pixel and the silhouette untouched. On heads they are eye colours.
/// </para>
/// <para>
/// Parsing is a span split rather than a regex, because the base name is not a simple
/// letters-then-digits shape — <c>bow1arrow1</c>, <c>shield1L</c>, <c>daggerL</c> and
/// <c>daggers</c> are all real files. Anything that is not a trailing <c>_c</c> followed only by
/// digits belongs to the base.
/// </para>
/// </summary>
public static class AssetName
{
    private const string VariantMarker = "_c";

    /// <summary>
    /// Splits a file stem (no extension) into its base name and colour variant.
    /// Variant <c>0</c> means the un-suffixed base file.
    /// </summary>
    /// <param name="stem">The file name with its extension already removed.</param>
    /// <returns>
    /// The base name and its variant number. A stem with no parseable <c>_cN</c> tail comes back
    /// unchanged with variant <c>0</c>, so <c>weird_cape</c> stays whole.
    /// </returns>
    public static (string Base, int Variant) Split(string stem)
    {
        Guard.IsNotNullOrWhiteSpace(stem);

        var marker = stem.LastIndexOf(VariantMarker, StringComparison.Ordinal);

        if (marker < 0)
        {
            return (stem, 0);
        }

        var digits = stem.AsSpan(marker + VariantMarker.Length);

        // NumberStyles.None rejects signs and whitespace, so "_c+3" stays part of the base.
        if (digits.IsEmpty
            || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var variant))
        {
            return (stem, 0);
        }

        return (stem[..marker], variant);
    }

    /// <summary>
    /// Decomposes a base name into the ordering key described by <see cref="AssetSortKey"/>:
    /// leading letters, the first run of digits, then whatever remains.
    /// </summary>
    /// <param name="base">A base name as produced by <see cref="Split"/>, without its variant tail.</param>
    /// <param name="variant">The colour variant to carry into <see cref="AssetSortKey.Variant"/>.</param>
    /// <returns>
    /// The comparable key. <see cref="AssetSortKey.Number"/> is <c>-1</c> when
    /// <paramref name="base"/> carries no digits at all, which sorts such names ahead of their
    /// numbered siblings rather than leaving the order undefined.
    /// </returns>
    public static AssetSortKey SortKey(string @base, int variant)
    {
        Guard.IsNotNullOrWhiteSpace(@base);

        var span = @base.AsSpan();
        var letters = 0;

        while (letters < span.Length && !char.IsAsciiDigit(span[letters]))
        {
            letters++;
        }

        var digits = letters;

        while (digits < span.Length && char.IsAsciiDigit(span[digits]))
        {
            digits++;
        }

        var number = digits > letters
            ? int.Parse(span[letters..digits], NumberStyles.None, CultureInfo.InvariantCulture)
            : -1;

        return new(span[..letters].ToString(), number, span[digits..].ToString(), variant);
    }
}
