using System.Collections.Immutable;

namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// A five-entry colour substitution, held as two parallel arrays of packed pixels.
/// <para>
/// This replaces the <see cref="System.Collections.Frozen.FrozenDictionary{TKey, TValue}"/> the
/// recolour used to consult once per pixel. A hash lookup is the wrong shape for a table of five:
/// five straight comparisons beat it even scalar, and they vectorise, which a dictionary cannot.
/// </para>
/// <para>
/// Both arrays hold whole 32-bit pixels with alpha forced opaque, not bare RGB. The source art has
/// strictly binary alpha — verified across all 995 partials — so an opaque pixel is always
/// <c>0xFF______</c> and a transparent one can never equal an entry here. Comparing the full pixel
/// therefore excludes transparent pixels for free, with no mask, no separate opacity test and no
/// alpha re-combination. See <see cref="Baking.SheetBaker"/> for the loop that relies on it.
/// </para>
/// </summary>
public readonly record struct RampSubstitution
{
    /// <summary>Packed pixels to look for, in ramp-step order.</summary>
    public required ImmutableArray<uint> From { get; init; }

    /// <summary>Packed pixels to write, index-aligned with <see cref="From"/>.</summary>
    public required ImmutableArray<uint> To { get; init; }

    /// <summary>How many steps the substitution covers.</summary>
    public int Length => From.Length;

    /// <summary>
    /// Whether every step maps to itself, making the substitution a no-op the baker can skip
    /// entirely. <see langword="true"/> when a sheet is baked in the tone its art is authored in.
    /// </summary>
    public bool IsIdentity
    {
        get
        {
            for (var step = 0; step < From.Length; step++)
            {
                if (From[step] != To[step])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
