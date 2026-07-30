using System.Collections.Immutable;
using System.Text.Json;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// A class's equipment pool, written once at the root rather than once under every hero.
/// </summary>
public sealed class LoadoutWriterTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    private FullPath Root => _directory.FullPath;

    private static readonly Guid Run = Guid.Parse("019a4f00-0000-7000-8000-000000000001");

    private static AssetPartial Partial(AssetSlot slot, string name) => new()
    {
        Slot = slot,
        Pack = ElementsPack.Core,
        Base = name,
        Variant = 0,
        Path = FullPath.FromPath($"C:/packs/core/assets/{AssetSlots.FolderName(slot)}/{name}.png"),
    };

    private static SlotSelection Optional(AssetSlot slot, params string[] names) => new()
    {
        Slot = slot,
        Choices = [Optional<AssetPartial>.None, .. names.Select(name => (Optional<AssetPartial>)Partial(slot, name))],
    };

    private static SlotSelection Required(AssetSlot slot, string name) => new()
    {
        Slot = slot,
        Choices = [Partial(slot, name)],
    };

    private async Task<string> WriteAsync(string className, ImmutableArray<SlotSelection> selections)
    {
        var written = await LoadoutWriter.WriteToAsync(
            Root, className, LoadoutWriter.PoolOf(selections), Run, TestContext.Current.CancellationToken);

        Assert.True(written.IsSuccessful);

        return await File.ReadAllTextAsync(
            (Root / LoadoutWriter.Folder / (className + ".json")).Value,
            TestContext.Current.CancellationToken);
    }

    /// <summary>The body is identity, not equipment, so it never appears in a loadout.</summary>
    [Fact]
    public void PoolOf_ExcludesTheRequiredTrio()
    {
        var pool = LoadoutWriter.PoolOf(
        [
            Required(AssetSlot.Bottom, "bottom1"),
            Required(AssetSlot.Top, "top11"),
            Required(AssetSlot.Head, "head1"),
            Optional(AssetSlot.Hat, "hat3"),
        ]);

        Assert.Empty(pool[(int)AssetSlot.Bottom]);
        Assert.Empty(pool[(int)AssetSlot.Top]);
        Assert.Empty(pool[(int)AssetSlot.Head]);
        Assert.Equal(["hat3"], pool[(int)AssetSlot.Hat]);
    }

    /// <summary>The absence of a hat is not equipment.</summary>
    [Fact]
    public void PoolOf_IgnoresTheNoneChoice()
    {
        var pool = LoadoutWriter.PoolOf([Optional(AssetSlot.Weapon, "sword1", "bow1")]);

        Assert.Equal(["sword1", "bow1"], pool[(int)AssetSlot.Weapon]);
    }

    [Fact]
    public void IsEmpty_IsTrue_WhenNothingOptionalIsTicked() =>
        Assert.True(LoadoutWriter.IsEmpty(LoadoutWriter.PoolOf([Required(AssetSlot.Bottom, "bottom1")])));

    [Fact]
    public void IsEmpty_IsFalse_WhenAnySlotOffersAStem() =>
        Assert.False(LoadoutWriter.IsEmpty(LoadoutWriter.PoolOf([Optional(AssetSlot.Hair, "hair1")])));

    /// <summary>Several stems in one slot is a pool to pick from, not a kit worn at once.</summary>
    [Fact]
    public async Task WriteToAsync_RecordsEveryStemASlotOffers()
    {
        var json = await WriteAsync("ranger",
        [
            Optional(AssetSlot.Hair, "hair1", "hair7"),
            Optional(AssetSlot.Hat, "hat3"),
            Optional(AssetSlot.Weapon, "sword1", "bow1"),
        ]);

        using var document = JsonDocument.Parse(json);

        var slots = document.RootElement.GetProperty("slots");

        Assert.Equal(["hair1", "hair7"], [.. slots.GetProperty("hair").EnumerateArray().Select(s => s.GetString()!)]);
        Assert.Equal(["sword1", "bow1"], [.. slots.GetProperty("weapon").EnumerateArray().Select(s => s.GetString()!)]);
        Assert.Equal("ranger", document.RootElement.GetProperty("class").GetString());
    }

    /// <summary>A slot the class does not use is absent, never an empty array.</summary>
    [Fact]
    public async Task WriteToAsync_OmitsSlotsTheClassDoesNotUse()
    {
        var json = await WriteAsync("caster", [Optional(AssetSlot.Weapon, "wand1")]);

        using var document = JsonDocument.Parse(json);

        var slots = document.RootElement.GetProperty("slots");

        Assert.True(slots.TryGetProperty("weapon", out _));
        Assert.False(slots.TryGetProperty("hair", out _));
        Assert.False(slots.TryGetProperty("hat", out _));
    }

    /// <summary>Loadouts sit one directory down, so their schema reference climbs a level.</summary>
    [Fact]
    public async Task WriteToAsync_PointsAtTheSchemaBesideTheRoot()
    {
        var json = await WriteAsync("ranger", [Optional(AssetSlot.Hat, "hat3")]);

        using var document = JsonDocument.Parse(json);

        Assert.Equal("../" + LoadoutWriter.SchemaFileName, document.RootElement.GetProperty("$schema").GetString());
        Assert.True(File.Exists((Root / LoadoutWriter.SchemaFileName).Value));
    }

    /// <summary>
    /// One row per class, across every loadout the folder holds — not only the one this run wrote.
    /// </summary>
    [Fact]
    public async Task WriteToAsync_RebuildsTheCsvOverEveryClass()
    {
        await WriteAsync("ranger", [Optional(AssetSlot.Hair, "hair1", "hair7"), Optional(AssetSlot.Weapon, "bow1")]);
        await WriteAsync("caster", [Optional(AssetSlot.Weapon, "wand1")]);

        var csv = await File.ReadAllTextAsync(
            (Root / LoadoutWriter.CsvFileName).Value, TestContext.Current.CancellationToken);

        Assert.Contains("ranger", csv, StringComparison.Ordinal);
        Assert.Contains("caster", csv, StringComparison.Ordinal);

        // Several stems join with a semicolon, so the comma dialect is never stressed.
        Assert.Contains("hair1;hair7", csv, StringComparison.Ordinal);
    }

    /// <summary>Rewriting a class replaces it rather than adding a second row.</summary>
    [Fact]
    public async Task WriteToAsync_ReplacesAClassRatherThanDuplicatingIt()
    {
        await WriteAsync("ranger", [Optional(AssetSlot.Weapon, "sword1")]);
        await WriteAsync("ranger", [Optional(AssetSlot.Weapon, "bow1")]);

        var csv = await File.ReadAllTextAsync(
            (Root / LoadoutWriter.CsvFileName).Value, TestContext.Current.CancellationToken);

        var rows = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Header plus exactly one ranger row.
        Assert.Equal(2, rows.Length);
        Assert.Contains("bow1", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("sword1", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteToAsync_FailsWhenTheRootIsMissing()
    {
        var written = await LoadoutWriter.WriteToAsync(
            Root / "no-such-dir",
            "ranger",
            LoadoutWriter.PoolOf([Optional(AssetSlot.Hat, "hat3")]),
            Run,
            TestContext.Current.CancellationToken);

        Assert.False(written.IsSuccessful);
        Assert.Equal(BakeFailure.OutputDirectoryUnavailable, written.Error);
    }
}
