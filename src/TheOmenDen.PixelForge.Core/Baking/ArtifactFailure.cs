namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>One manifest that could not be written, and why.</summary>
/// <param name="File">The file name, as the writer names it — e.g. <c>manifest.json</c>.</param>
/// <param name="Failure">Why it did not land.</param>
/// <remarks>
/// A <see langword="readonly"/> <see langword="record"/> <see langword="struct"/> because it is two
/// small values reported in a batch and never mutated. Carrying the file name rather than an enum
/// member keeps <see cref="RunArtifacts"/> free of a parallel list that would need extending every
/// time a manifest is added.
/// </remarks>
public readonly record struct ArtifactFailure(string File, BakeFailure Failure);
