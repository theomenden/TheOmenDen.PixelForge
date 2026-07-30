using System.Reflection;
using System.Text.Json;
using TheOmenDen.PixelForge.Schema;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The three schemas this solution ships, and the invariants that hold across all of them.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these guards a failure that <em>compiles</em>. A schema wired up as
/// <c>AdditionalFiles</c> but not <c>EmbeddedResource</c> generates a perfectly good type and then
/// ships a document whose <c>$schema</c> points at a file that is not in the folder. A schema wired
/// up the other way round embeds fine and generates nothing. Neither is a build error, and the
/// export folder is where you would otherwise find out.
/// </para>
/// <para>
/// Reading the resource directly rather than through a shared helper is deliberate at this stage:
/// <c>RunManifest.ReadEmbeddedSchema</c> is private and single-purpose, and there is not yet a
/// second production caller to justify lifting it. <c>HeroRegistry</c> becomes that caller.
/// </para>
/// </remarks>
public sealed class GeneratedSchemaTests
{
    private const string ManifestSchema = "pixelforge-manifest-v1.json";
    private const string HeroesSchema = "pixelforge-heroes-v1.json";
    private const string LoadoutsSchema = "pixelforge-loadouts-v1.json";

    /// <summary>The assembly the schemas are embedded in, beside the types generated from them.</summary>
    private static Assembly Schemas => typeof(RunManifestDocument).Assembly;

    public static TheoryData<string> EverySchema =>
        [ManifestSchema, HeroesSchema, LoadoutsSchema];

    /// <summary>
    /// Each schema ships inside the assembly, which is what lets a writer copy it into the export
    /// folder beside the document it describes.
    /// </summary>
    [Theory]
    [MemberData(nameof(EverySchema))]
    public void EverySchema_IsEmbedded_SoItCanShipBesideItsDocument(string fileName)
    {
        var text = Read(fileName);

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    /// <summary>
    /// The <c>$id</c>'s last segment is the file name, and both carry the major version. All three
    /// are maintained by hand; this is the only thing holding them together.
    /// </summary>
    [Theory]
    [MemberData(nameof(EverySchema))]
    public void EverySchemaId_AgreesWithItsFileName(string fileName)
    {
        using var schema = JsonDocument.Parse(Read(fileName));

        var id = schema.RootElement.GetProperty("$id").GetString();

        Assert.NotNull(id);

        var segment = id[(id.LastIndexOf('/') + 1)..];

        Assert.Equal(fileName, segment);
    }

    /// <summary>
    /// Every schema declares its version as a <c>const</c>, so a drifted writer fails validation
    /// rather than shipping a plausible but wrong version.
    /// </summary>
    [Theory]
    [MemberData(nameof(EverySchema))]
    public void EverySchema_DeclaresItsVersionAsAConst(string fileName)
    {
        using var schema = JsonDocument.Parse(Read(fileName));

        var version = schema.RootElement
            .GetProperty("properties")
            .GetProperty("schemaVersion")
            .GetProperty("const")
            .GetString();

        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    /// <summary>
    /// The hero registry's generated type exists and carries the schema's version constant.
    /// </summary>
    /// <remarks>
    /// This is the only proof available that the generator actually ran for this schema: generated
    /// sources are not emitted to disk here, so a missing <c>AdditionalFiles</c> entry would not
    /// show up as a file, only as a type that will not resolve.
    /// </remarks>
    [Fact]
    public void HeroRegistryDocument_IsGenerated_AndCarriesItsVersion()
    {
        var version = (string)HeroRegistryDocument.SchemaVersionEntity.ConstInstance;

        Assert.Equal("1.0.0", version);
        Assert.Contains($"\"const\": \"{version}\"", Read(HeroesSchema), StringComparison.Ordinal);
    }

    /// <summary>The loadout's generated type exists and carries the schema's version constant.</summary>
    [Fact]
    public void LoadoutDocument_IsGenerated_AndCarriesItsVersion()
    {
        var version = (string)LoadoutDocument.SchemaVersionEntity.ConstInstance;

        Assert.Equal("1.0.0", version);
        Assert.Contains($"\"const\": \"{version}\"", Read(LoadoutsSchema), StringComparison.Ordinal);
    }

    /// <summary>
    /// A loadout describes equipment, so it carries the seven optional slots and none of the three
    /// that make a hero — the body is identity, not a kit.
    /// </summary>
    [Fact]
    public void LoadoutSchema_CarriesTheOptionalSlotsOnly()
    {
        using var schema = JsonDocument.Parse(Read(LoadoutsSchema));

        var slots = schema.RootElement
            .GetProperty("$defs")
            .GetProperty("slots")
            .GetProperty("properties");

        foreach (var optional in (string[])["shadow", "backExtra", "backHair", "hair", "frontExtra", "hat", "weapon"])
        {
            Assert.True(slots.TryGetProperty(optional, out _), optional);
        }

        foreach (var body in (string[])["bottom", "top", "head"])
        {
            Assert.False(slots.TryGetProperty(body, out _), body);
        }
    }

    private static string Read(string fileName)
    {
        var name = Schemas.GetManifestResourceNames()
            .First(candidate => candidate.EndsWith(fileName, StringComparison.Ordinal));

        using var stream = Schemas.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
