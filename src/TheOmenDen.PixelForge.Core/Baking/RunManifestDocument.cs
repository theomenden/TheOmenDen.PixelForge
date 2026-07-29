using Corvus.Text.Json;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// The generated type for <c>manifest.json</c>, projected from
/// <c>Schemas/pixelforge-manifest-v1.json</c> by <c>Corvus.Text.Json.SourceGenerator</c>.
/// </summary>
/// <remarks>
/// <para>
/// The schema is the source of truth, not this declaration and not a hand-written record. The
/// generator emits a <see langword="readonly"/> <see langword="struct"/> per subschema with typed
/// accessors, a <c>Builder</c>, and <c>EvaluateSchema()</c> — so a shape the schema forbids cannot
/// be constructed here, and the round-trip test proves what was written still validates.
/// </para>
/// <para>
/// This matters more than usual: Corvus consumes baked artifacts only — no package reference, no
/// submodule, no build coupling — so there is deliberately no compiler spanning the seam. The
/// schema is what replaces it, which is also why <see cref="RunManifest"/> copies the schema file
/// into the export directory beside the manifest it describes.
/// </para>
/// </remarks>
// The path resolves relative to THIS source file, not to the project directory — hence the
// leading "..". A project-relative path fails with CRV1000 "Unable to locate the root document".
[JsonSchemaTypeGenerator("../Schemas/pixelforge-manifest-v1.json")]
public readonly partial struct RunManifestDocument;
