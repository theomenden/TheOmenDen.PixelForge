using System.Collections.Immutable;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// What a run writes beside its sheets, on top of the manifests every run leaves.
/// </summary>
/// <remarks>
/// <para>
/// A record rather than four more parameters on <c>RunArtifacts.WriteAllAsync</c>. The house limit
/// is six and this would have reached it — and at that point, as <c>SheetRecipe</c> and
/// <c>SlotSelection</c> already demonstrate, the arguments are a type nobody has named yet.
/// </para>
/// <para>
/// Every member past <see cref="RunId"/> is optional, so the curated deliverable — which has no
/// heroes and no class — passes only the id and gets exactly the manifests it had before.
/// </para>
/// </remarks>
public sealed record LayerRun
{
    /// <summary>Stamped into every manifest this run writes, from <see cref="BatchManifest.NewRunId"/>.</summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// Every hero the export folder knows, after <see cref="HeroRegistry.Assign"/> — not only the
    /// ones this run produced. Empty writes no registry at all.
    /// </summary>
    public ImmutableArray<HeroEntry> Heroes { get; init; } = [];

    /// <summary>The slugged class this run names, or empty for a run that names none.</summary>
    public string ClassName { get; init; } = string.Empty;

    /// <summary>The class's equipment pool, from <see cref="LoadoutWriter.PoolOf"/>.</summary>
    public ImmutableArray<string>[] Pool { get; init; } = [];
}
