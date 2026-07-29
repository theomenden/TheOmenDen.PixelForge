namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Why a bake did not produce a sheet.
/// <para>
/// Every one of these is an environmental or data condition — a missing partial, art of the
/// wrong geometry, an encoder that quietly went lossy. None is a programming error, so none
/// is an exception: they travel as <see cref="DotNext.Result{T, TError}"/> values and the
/// caller has to look at them.
/// </para>
/// <para>
/// Numbering starts at 1 so <c>default</c> is never mistaken for a real failure.
/// </para>
/// </summary>
public enum BakeFailure
{
    /// <summary>A recipe supplied no layers to composite.</summary>
    NoLayersSupplied = 1,

    /// <summary>A layer partial is not on disk at the path the recipe names.</summary>
    LayerNotFound,

    /// <summary>A layer partial exists but no decoder would read it.</summary>
    LayerUnreadable,

    /// <summary>A layer partial is not 1104x192 — the wrong art, or art from another pack version.</summary>
    LayerGeometryMismatch,

    /// <summary>
    /// A bitmap is not tightly packed RGBA8888/unpremultiplied. The baker reads pixel memory
    /// directly, and Skia's platform-preferred type on Windows is BGRA — reading that as RGBA
    /// silently swaps red and blue, so the format is checked rather than assumed.
    /// </summary>
    LayerPixelFormatMismatch,

    /// <summary>The assembly handed to the curate step is not source geometry.</summary>
    SourceGeometryMismatch,

    /// <summary>The WebP encoder returned nothing at all.</summary>
    EncoderReturnedNoData,

    /// <summary>The container carries no VP8L chunk, so the encode was lossy.</summary>
    EncoderProducedLossyOutput,

    /// <summary>The encoded sheet did not decode back to the pixels that went in.</summary>
    RoundTripMismatch,

    /// <summary>The output directory does not exist, or is not reachable.</summary>
    OutputDirectoryUnavailable,

    /// <summary>The sheet encoded, but the file could not be written.</summary>
    OutputWriteFailed,

    /// <summary>
    /// The composed run manifest did not satisfy <c>pixelforge-manifest-v1.json</c>.
    /// <para>
    /// Unlike its neighbours this one <em>is</em> a bug rather than bad input — nothing a user
    /// supplies should be able to produce an invalid document. It stays a value because the
    /// alternative is throwing out of a bake that otherwise succeeded, and because a manifest
    /// that fails its own schema must never reach disk where a consumer would read it.
    /// </para>
    /// </summary>
    ManifestSchemaViolation,
}
