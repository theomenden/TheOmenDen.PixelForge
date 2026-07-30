using System.Collections.Immutable;
using System.Text.Json;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The <c>hero</c> property added at schema 1.1.0: present on a hero's base sheet, absent on a
/// standalone attachment layer.
/// </summary>
public sealed class RunManifestHeroTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    private static SheetRecipe Recipe(string name, string directory) => new()
    {
        Name = name,
        Directory = directory,
        Layers = [],
    };

    private async Task<JsonElement> SheetsAsync(ImmutableArray<SheetRecipe> recipes)
    {
        var written = await RunManifest.WriteToAsync(
            _directory.FullPath, BatchManifest.NewRunId(), recipes, TestContext.Current.CancellationToken);

        Assert.True(written.IsSuccessful);

        var json = await File.ReadAllTextAsync(
            (_directory.FullPath / RunManifest.FileName).Value, TestContext.Current.CancellationToken);

        return JsonDocument.Parse(json).RootElement.GetProperty("sheets").Clone();
    }

    [Fact]
    public async Task Write_NamesTheHero_OnABaseSheet()
    {
        var sheets = await SheetsAsync([Recipe("villager_01", "heroes/villager_01")]);

        Assert.Equal("villager_01", sheets[0].GetProperty("hero").GetString());
    }

    /// <summary>An attachment belongs to no hero and is shared by every one of them.</summary>
    [Fact]
    public async Task Write_OmitsTheHero_OnAnAttachment()
    {
        var sheets = await SheetsAsync([Recipe("hair1", "attachments/hair")]);

        Assert.False(sheets[0].TryGetProperty("hero", out _));
    }

    /// <summary>The deliverable sits at its own root, so it names no hero either.</summary>
    [Fact]
    public async Task Write_OmitsTheHero_ForASheetAtTheRoot()
    {
        var sheets = await SheetsAsync([Recipe("body-01", string.Empty)]);

        Assert.False(sheets[0].TryGetProperty("hero", out _));
    }

    /// <summary>
    /// The two manifests must agree about where a sheet is — they compose the path through one
    /// property now, and this is what proves it.
    /// </summary>
    [Fact]
    public async Task Write_AgreesWithTheCsvAboutTheFilePath()
    {
        var recipe = Recipe("villager_01", "heroes/villager_01");

        var sheets = await SheetsAsync([recipe]);

        Assert.Equal(
            BatchManifest.RowFor(recipe).File,
            sheets[0].GetProperty("file").GetString());
    }
}
