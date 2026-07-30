using TheOmenDen.PixelForge.Schema;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Reads a schema out of the assembly it is embedded in.
/// </summary>
/// <remarks>
/// <para>
/// The Schema assembly, never this one: each schema is embedded beside the type generated from it,
/// so the two cannot drift to different files. Shipping the schema into the export folder is what
/// replaces a compiler across the seam — Corvus consumes baked artifacts with no build coupling, so
/// every document's <c>$schema</c> points at a copy sitting next to it.
/// </para>
/// <para>
/// Lifted out of <c>RunManifest</c> when <see cref="HeroRegistry"/> became a second caller. It stays
/// <see langword="internal"/>: reading raw schema text is a detail of writing a document, not
/// something a consumer of this library needs.
/// </para>
/// </remarks>
internal static class EmbeddedSchemas
{
    /// <summary>The text of one embedded schema.</summary>
    /// <param name="fileName">Its file name, such as <c>pixelforge-heroes-v1.json</c>.</param>
    /// <returns>The schema source, verbatim.</returns>
    /// <remarks>
    /// A missing resource throws rather than returning a failure, and deliberately: the schemas are
    /// compiled into this solution, so their absence is a broken build — an
    /// <c>EmbeddedResource</c> entry dropped from the csproj — not a condition any user can reach.
    /// </remarks>
    internal static string Read(string fileName)
    {
        var assembly = typeof(RunManifestDocument).Assembly;

        // GetManifestResourceNames returns an array, which the ZLinq drop-in generator covers, so
        // this First binds to ZLinq without an AsValueEnumerable.
        var name = assembly.GetManifestResourceNames()
            .First(candidate => candidate.EndsWith(fileName, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
