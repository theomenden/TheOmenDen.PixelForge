using Corvus.Text.Json;

namespace TheOmenDen.PixelForge.Schema;

/// <summary>
/// The generated type for <c>loadouts/&lt;class&gt;.json</c>, projected from
/// <c>Schemas/pixelforge-loadouts-v1.json</c> by <c>Corvus.Text.Json.SourceGenerator</c>.
/// </summary>
/// <remarks>
/// <para>
/// A loadout is a shipped contract another system reads, which is what puts it in the same category
/// as <see cref="RunManifestDocument"/> rather than alongside the write-only CSV views. The
/// <c>classes.csv</c> beside it carries the same facts for a spreadsheet and is deliberately
/// <em>not</em> schema-backed, exactly as <c>sheets.csv</c> is not.
/// </para>
/// <para>
/// <b>Do not add hand-written code to this project.</b> See <see cref="RunManifestDocument"/> for
/// why the generator's doc-comment diagnostics can be suppressed here and nowhere else.
/// </para>
/// </remarks>
// The path resolves relative to THIS source file, not to the project directory. One that does not
// resolve fails with CRV1000 "Unable to locate the root document".
[JsonSchemaTypeGenerator("Schemas/pixelforge-loadouts-v1.json")]
public readonly partial struct LoadoutDocument;
