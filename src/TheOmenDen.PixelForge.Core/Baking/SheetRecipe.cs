using System.Collections.Immutable;
using DotNext;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// One output sheet: the layers that make it, in back-to-front draw order, and the tone its
/// skin-bearing layers are substituted into.
/// </summary>
/// <remarks>
/// <para>
/// There is no <c>Overlays</c> collection any more. It existed to draw hair <em>after</em> a
/// substitution that ran over the flattened assembly, so hair's authored colour survived. Once the
/// substitution moved onto individual layers that problem cannot arise, and the old shape could
/// never have expressed back-hair anyway — it draws below the body, so "after the recolour" and
/// "behind the body" were mutually exclusive.
/// </para>
/// </remarks>
public sealed record SheetRecipe
{
    /// <summary>Output stem, e.g. <c>body-01</c>. The <c>.webp</c> is added when written.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Where this sheet goes, relative to the export root and separated with <c>/</c> —
    /// <c>heroes/villager_01</c>, <c>attachments/hair</c>, <c>curated</c>. Empty means the root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A layer export writes every recipe to a different place, so the destination is a property of
    /// the recipe rather than an argument threaded beside it. That is also what keeps
    /// <see cref="BatchBaker.RunAsync"/> at five parameters against a hard cap of six.
    /// </para>
    /// <para>
    /// Forward slashes, not <see cref="Path.DirectorySeparatorChar"/>: this string is written into
    /// the manifests as-is and read by a consumer that is not on Windows. Combination with the
    /// export root goes through <c>FullPath</c>, which normalises it for the filesystem.
    /// </para>
    /// </remarks>
    public string Directory { get; init; } = string.Empty;

    /// <summary>
    /// The file this recipe writes, relative to the export root — <see cref="Directory"/>,
    /// <see cref="Name"/> and <see cref="SheetWriter.Extension"/> joined.
    /// </summary>
    /// <remarks>
    /// The single source for the <c>File</c> column in <c>sheets.csv</c> and the <c>file</c>
    /// property in <c>manifest.json</c>. Those are composed by two different writers, and before
    /// this existed each built the string itself — the same drift <c>StemsBySlot</c> is shared to
    /// prevent, one layer up. Two writers, one mapping, or the manifests disagree about where a
    /// sheet is and nothing catches it.
    /// </remarks>
    public string RelativePath => Directory.Length is 0
        ? Name + SheetWriter.ExtensionFor(Format)
        : $"{Directory}/{Name}{SheetWriter.ExtensionFor(Format)}";

    /// <summary>
    /// Which container to encode into. Defaults to <see cref="SheetFormat.Webp"/>, so a recipe
    /// that says nothing cannot change what Corvus receives.
    /// </summary>
    /// <remarks>
    /// A peer of <see cref="Geometry"/> rather than a mode of it: the two are independent, and a
    /// run legitimately writes the same geometry into both containers for two different consumers.
    /// No collision follows, because the extension differs — which is also why
    /// <see cref="LayerPlan"/>'s geometry marking needs no third axis.
    /// </remarks>
    public SheetFormat Format { get; init; } = SheetFormat.Webp;

    /// <summary>
    /// Which pack these layers are authored in, naming the palette a recolour reads from.
    /// </summary>
    /// <remarks>
    /// <see cref="Tone"/> has only ever named the palette a recolour goes <em>to</em>; the one it
    /// came from was assumed to be Time Elements' authored ramp. That assumption is invisible and
    /// fails quietly — the two packs share no colour, so a Time Fantasy sheet built against the
    /// wrong source table matches nothing and comes out in its authored palette with no error
    /// anywhere. Stating the pack is what makes the pair explicit.
    /// </remarks>
    public SourcePack Pack { get; init; } = SourcePack.TimeElements;

    /// <summary>
    /// Layers back to front, following the generator's <c>CharacterLayers</c> order — which is
    /// also <see cref="Catalog.AssetSlot"/>'s member order, so a planner can sort by slot.
    /// </summary>
    public required ImmutableArray<AssetLayer> Layers { get; init; }

    /// <summary>
    /// The skin tone to substitute into, applied only to layers whose
    /// <see cref="AssetLayer.IsSkin"/> is set.
    /// <para>
    /// <see cref="Optional{T}"/> rather than a nullable reference so "keep the authored tone" is a
    /// value the type system carries, not a <see langword="null"/> every caller must remember to
    /// check. A hair-only sheet has no tone at all.
    /// </para>
    /// </summary>
    public Optional<SkinRamp> Tone { get; init; } = Optional<SkinRamp>.None;

    /// <summary>
    /// Which geometry to write. Defaults to <see cref="SheetGeometry.Curated"/>, so a recipe that
    /// says nothing cannot silently change the Corvus contract.
    /// </summary>
    public SheetGeometry Geometry { get; init; } = SheetGeometry.Curated;
}
