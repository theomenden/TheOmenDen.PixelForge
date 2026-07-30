namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>Which container a baked sheet is encoded into.</summary>
public enum SheetFormat
{
    /// <summary>
    /// Lossless WebP, proven by round trip rather than by trusting the encoder flag — see
    /// <see cref="LosslessWebp.EncodeVerified"/>. This is what Corvus consumes.
    /// <para>
    /// Zero on purpose, for the same reason <see cref="SheetGeometry.Curated"/> is: a recipe that
    /// says nothing about its format cannot silently change what a shipped consumer receives.
    /// </para>
    /// </summary>
    Webp = 0,

    /// <summary>
    /// PNG, for the engine consumers. Neither Unity's <c>TextureImporter</c> nor MonoGame's
    /// content pipeline reads WebP, so this is not a preference — it is the only container those
    /// two can open.
    /// </summary>
    Png = 1,
}
