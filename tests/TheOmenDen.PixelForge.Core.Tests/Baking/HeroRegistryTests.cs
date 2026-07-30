using System.Collections.Immutable;
using System.Text.Json;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The registry that makes a hero directory name mean the same body next run as it did this one.
/// </summary>
public sealed class HeroRegistryTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    private FullPath Root => _directory.FullPath;

    private static HeroKey Key(string bottom) => new(bottom, "top11", "head1");

    private static readonly Guid FirstRun = Guid.Parse("019a4f00-0000-7000-8000-000000000001");
    private static readonly Guid SecondRun = Guid.Parse("019b7c00-0000-7000-8000-000000000002");

    private async Task<ImmutableArray<HeroEntry>> RoundTripAsync(ImmutableArray<HeroEntry> heroes)
    {
        var written = await HeroRegistry.WriteToAsync(Root, heroes, TestContext.Current.CancellationToken);

        Assert.True(written.IsSuccessful);

        var read = await HeroRegistry.ReadAsync(Root, TestContext.Current.CancellationToken);

        Assert.True(read.IsSuccessful);

        return read.Value;
    }

    /// <summary>No registry is a first run, not a fault.</summary>
    [Fact]
    public async Task ReadAsync_WithNoRegistry_IsEmptyRatherThanAFailure()
    {
        var read = await HeroRegistry.ReadAsync(Root, TestContext.Current.CancellationToken);

        Assert.True(read.IsSuccessful);
        Assert.Empty(read.Value);
    }

    [Fact]
    public void Assign_NumbersFromOne_WhenNothingIsKnown()
    {
        var heroes = HeroRegistry.Assign([], [Key("bottom1"), Key("bottom3")], "villager", FirstRun);

        Assert.Equal(["villager_01", "villager_02"], [.. heroes.Select(static h => h.Name)]);
    }

    /// <summary>
    /// The whole reason the registry is read back: a body keeps its number when a later run adds
    /// others around it.
    /// </summary>
    [Fact]
    public void Assign_KeepsAnExistingNumber_WhenALaterRunAddsMore()
    {
        var first = HeroRegistry.Assign([], [Key("bottom1"), Key("bottom3")], "villager", FirstRun);

        // bottom2 sorts between them, which is exactly what would renumber under plan order.
        var second = HeroRegistry.Assign(
            first, [Key("bottom1"), Key("bottom2"), Key("bottom3")], "villager", SecondRun);

        Assert.Equal("villager_01", second.Single(h => h.Key == Key("bottom1")).Name);
        Assert.Equal("villager_02", second.Single(h => h.Key == Key("bottom3")).Name);
        Assert.Equal("villager_03", second.Single(h => h.Key == Key("bottom2")).Name);
    }

    /// <summary>A body already named keeps that name whatever prefix is typed later.</summary>
    [Fact]
    public void Assign_DoesNotRenameAKnownBody_WhenThePrefixChanges()
    {
        var first = HeroRegistry.Assign([], [Key("bottom1")], "villager", FirstRun);
        var second = HeroRegistry.Assign(first, [Key("bottom1")], "noble", SecondRun);

        Assert.Equal("villager_01", Assert.Single(second).Name);
    }

    /// <summary>Each prefix has its own high-water mark, so a class's heroes sort together.</summary>
    [Fact]
    public void Assign_NumbersEachPrefixIndependently()
    {
        var villagers = HeroRegistry.Assign([], [Key("bottom1"), Key("bottom3")], "villager", FirstRun);
        var both = HeroRegistry.Assign(villagers, [Key("bottom4")], "noble", SecondRun);

        Assert.Equal("noble_01", both.Single(h => h.Key == Key("bottom4")).Name);
    }

    /// <summary>A hero absent from this run keeps its entry, so its number is never reused.</summary>
    [Fact]
    public void Assign_KeepsHeroesThisRunDidNotProduce()
    {
        var first = HeroRegistry.Assign([], [Key("bottom1"), Key("bottom3")], "villager", FirstRun);
        var second = HeroRegistry.Assign(first, [Key("bottom1")], "villager", SecondRun);

        Assert.Equal(2, second.Length);
        Assert.Contains(second, h => h.Key == Key("bottom3"));
    }

    /// <summary>Numbers widen past 99 rather than wrapping or being refused.</summary>
    [Fact]
    public void Assign_WidensThelabelPastNinetyNine()
    {
        var existing = ImmutableArray.CreateRange(
            Enumerable.Range(1, 99).Select(n => new HeroEntry("villager", n, Key($"bottom{n}"), FirstRun)));

        var grown = HeroRegistry.Assign(existing, [Key("newbody")], "villager", SecondRun);

        Assert.Equal("villager_100", grown.Single(h => h.Key == Key("newbody")).Name);
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsEveryField()
    {
        var heroes = HeroRegistry.Assign([], [Key("bottom1")], "villager", FirstRun);

        var entry = Assert.Single(await RoundTripAsync(heroes));

        Assert.Equal("villager", entry.Prefix);
        Assert.Equal(1, entry.Number);
        Assert.Equal(Key("bottom1"), entry.Key);
        Assert.Equal(FirstRun, entry.AssignedInRun);
    }

    /// <summary>Numbering survives the file, not just the in-memory call.</summary>
    [Fact]
    public async Task WriteThenRead_PreservesNumbersAcrossRuns()
    {
        await RoundTripAsync(HeroRegistry.Assign([], [Key("bottom1"), Key("bottom3")], "villager", FirstRun));

        var read = await HeroRegistry.ReadAsync(Root, TestContext.Current.CancellationToken);
        var second = HeroRegistry.Assign(read.Value, [Key("bottom2")], "villager", SecondRun);

        Assert.Equal("villager_03", second.Single(h => h.Key == Key("bottom2")).Name);
    }

    /// <summary>The schema travels with the document, or a consumer cannot validate it.</summary>
    [Fact]
    public async Task WriteToAsync_CopiesTheSchemaInBeside()
    {
        await RoundTripAsync(HeroRegistry.Assign([], [Key("bottom1")], "villager", FirstRun));

        Assert.True(File.Exists((Root / HeroRegistry.SchemaFileName).Value));
    }

    /// <summary>The spreadsheet view is written too, and agrees with the registry.</summary>
    [Fact]
    public async Task WriteToAsync_WritesTheCsvView()
    {
        await RoundTripAsync(HeroRegistry.Assign([], [Key("bottom1")], "villager", FirstRun));

        var csv = await File.ReadAllTextAsync(
            (Root / HeroRegistry.CsvFileName).Value, TestContext.Current.CancellationToken);

        Assert.Contains("Hero,Prefix,Number,Bottom,Top,Head,AssignedInRun", csv, StringComparison.Ordinal);
        Assert.Contains("villager_01,villager,1,bottom1,top11,head1", csv, StringComparison.Ordinal);
    }

    /// <summary>
    /// A registry that cannot be trusted stops the run, rather than renumbering over an existing
    /// tree — which is the silent corruption the read-back exists to prevent.
    /// </summary>
    [Theory]
    [InlineData("{ not json at all")]
    [InlineData("""{"$schema":"pixelforge-heroes-v1.json","schemaVersion":"1.0.0"}""")]
    public async Task ReadAsync_RefusesARegistryItCannotTrust(string content)
    {
        await File.WriteAllTextAsync(
            (Root / HeroRegistry.FileName).Value, content, TestContext.Current.CancellationToken);

        var read = await HeroRegistry.ReadAsync(Root, TestContext.Current.CancellationToken);

        Assert.False(read.IsSuccessful);
        Assert.Equal(PlanFailure.HeroRegistryUnreadable, read.Error);
    }

    /// <summary>
    /// A number arriving as a string is exactly what a column count cannot catch and a schema can.
    /// </summary>
    [Fact]
    public async Task ReadAsync_RefusesARegistryWhoseNumberIsNotAnInteger()
    {
        const string Registry = """
            {
              "$schema": "pixelforge-heroes-v1.json",
              "schemaVersion": "1.0.0",
              "heroes": [
                {
                  "name": "villager_01",
                  "prefix": "villager",
                  "number": "one",
                  "body": { "bottom": "bottom1", "top": "top11", "head": "head1" },
                  "assignedInRun": "019a4f00-0000-7000-8000-000000000001"
                }
              ]
            }
            """;

        await File.WriteAllTextAsync(
            (Root / HeroRegistry.FileName).Value, Registry, TestContext.Current.CancellationToken);

        var read = await HeroRegistry.ReadAsync(Root, TestContext.Current.CancellationToken);

        Assert.False(read.IsSuccessful);
        Assert.Equal(PlanFailure.HeroRegistryUnreadable, read.Error);
    }

    /// <summary>The document declares the version the schema does, not a literal of its own.</summary>
    [Fact]
    public async Task WriteToAsync_StampsTheSchemaVersionFromTheSchema()
    {
        await RoundTripAsync(HeroRegistry.Assign([], [Key("bottom1")], "villager", FirstRun));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(
            (Root / HeroRegistry.FileName).Value, TestContext.Current.CancellationToken));

        Assert.Equal("1.0.0", document.RootElement.GetProperty("schemaVersion").GetString());
    }

    /// <summary>Labels are what the planner consumes, keyed by body.</summary>
    [Fact]
    public void Labels_MapEveryBodyToItsDirectoryName()
    {
        var heroes = HeroRegistry.Assign([], [Key("bottom1"), Key("bottom3")], "villager", FirstRun);

        var labels = HeroRegistry.Labels(heroes);

        Assert.Equal("villager_01", labels[Key("bottom1")]);
        Assert.Equal("villager_02", labels[Key("bottom3")]);
    }
}
