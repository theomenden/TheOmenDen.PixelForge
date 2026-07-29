namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>Which geometry a baked sheet is written in.</summary>
public enum SheetGeometry
{
    /// <summary>
    /// The 240x1152 sheet Corvus consumes: 8 clips on 3 facings, north dropped, described by
    /// <see cref="Spritesheets.SheetIndex"/>. This is a shipped contract — see
    /// <see cref="Spritesheets.SheetLayout"/> — and must stay byte-identical.
    /// </summary>
    Curated = 0,

    /// <summary>
    /// The raw 1104x192 assembly: all 23 source columns on all 4 facing rows, written without a
    /// remap. Keeps the nock/bow draw, climb and the north facing, which the curated geometry
    /// drops. Described by <see cref="Spritesheets.ClipIndex"/>.
    /// </summary>
    Full = 1,
}
