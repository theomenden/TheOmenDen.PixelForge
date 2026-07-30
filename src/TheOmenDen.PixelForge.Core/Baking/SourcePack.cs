namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>Which art pack a sheet's layers are authored in.</summary>
/// <remarks>
/// This names the palette a recolour reads <em>from</em>. <see cref="SheetRecipe.Tone"/> only ever
/// named what it goes to, and the source was assumed to be Time Elements' authored ramp — an
/// assumption that held exactly as long as there was one pack.
/// </remarks>
public enum SourcePack
{
    /// <summary>
    /// Time Elements, authored in <see cref="Palettes.SkinRamps.Source"/>.
    /// <para>
    /// Zero on purpose, like <see cref="SheetGeometry.Curated"/> and
    /// <see cref="SheetFormat.Webp"/>: silence must keep meaning what it has always meant.
    /// </para>
    /// </summary>
    TimeElements = 0,

    /// <summary>
    /// Time Fantasy, authored in <see cref="Palettes.TimeFantasyRamps"/>' four shades plus its
    /// <c>#354048</c> outline.
    /// </summary>
    TimeFantasy = 1,
}
