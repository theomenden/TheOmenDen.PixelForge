using Corvus.Text.Json;

namespace TheOmenDen.PixelForge.Schema;

/// <summary>
/// The generated type for <c>heroes.json</c>, projected from
/// <c>Schemas/pixelforge-heroes-v1.json</c> by <c>Corvus.Text.Json.SourceGenerator</c>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="RunManifestDocument"/>, this document is <em>read back</em> as well as written:
/// hero numbering is stable across runs precisely because the previous registry is parsed before the
/// next one is composed. That is what earns it a schema rather than another CSV — a column count
/// cannot catch a <c>number</c> that arrived as a string or a <c>body</c> missing its <c>head</c>,
/// and renumbering over an existing tree is the corruption the read-back exists to prevent.
/// </para>
/// <para>
/// <b>Do not add hand-written code to this project.</b> See <see cref="RunManifestDocument"/> for
/// why the generator's doc-comment diagnostics can be suppressed here and nowhere else.
/// </para>
/// </remarks>
// The path resolves relative to THIS source file, not to the project directory. One that does not
// resolve fails with CRV1000 "Unable to locate the root document".
[JsonSchemaTypeGenerator("Schemas/pixelforge-heroes-v1.json")]
public readonly partial struct HeroRegistryDocument;
