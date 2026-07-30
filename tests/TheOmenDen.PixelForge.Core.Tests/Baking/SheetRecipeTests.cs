using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// <see cref="SheetRecipe.RelativePath"/>, which is the one place the written file's path is
/// composed. Both manifests read it, so a change here moves <c>sheets.csv</c> and
/// <c>manifest.json</c> together or not at all.
/// </summary>
public sealed class SheetRecipeTests
{
    private static SheetRecipe Recipe(string directory) => new()
    {
        Name = "villager_01",
        Layers = [],
        Directory = directory,
    };

    /// <summary>The root is the default, and it must not produce a leading separator.</summary>
    [Fact]
    public void RelativePath_WithNoDirectory_IsTheFileNameAlone() =>
        Assert.Equal("villager_01.webp", Recipe(string.Empty).RelativePath);

    [Fact]
    public void RelativePath_WithADirectory_JoinsItAheadOfTheFileName() =>
        Assert.Equal("heroes/villager_01/villager_01.webp", Recipe("heroes/villager_01").RelativePath);

    /// <summary>
    /// Forward slashes even on Windows: this string is written verbatim into both manifests and
    /// read by a consumer that is not on Windows.
    /// </summary>
    [Fact]
    public void RelativePath_UsesForwardSlashes_WhateverThePlatformSeparatorIs() =>
        Assert.DoesNotContain('\\', Recipe("attachments/hair").RelativePath);

    /// <summary>A recipe that says nothing about placement still names a file, never a bare extension.</summary>
    [Fact]
    public void RelativePath_AlwaysCarriesTheExtension() =>
        Assert.EndsWith(
            SheetWriter.ExtensionFor(SheetFormat.Webp),
            Recipe("attachments/hat").RelativePath,
            StringComparison.Ordinal);

    /// <summary>
    /// The format a recipe names is what its path carries. Corvus reads <c>.webp</c> and neither
    /// Unity nor MonoGame can open one, so this is the single point where that difference becomes
    /// a filename — and therefore the only thing both manifests have to agree about.
    /// </summary>
    [Fact]
    public void RelativePath_WhenTheRecipeNamesPng_CarriesThePngExtension()
    {
        var recipe = new SheetRecipe
        {
            Name = "villager_01",
            Layers = [],
            Directory = "heroes/villager_01",
            Format = SheetFormat.Png,
        };

        Assert.Equal("heroes/villager_01/villager_01.png", recipe.RelativePath);
    }

    /// <summary>
    /// Silence means WebP. <see cref="SheetFormat.Webp"/> is <c>0</c> for the same reason
    /// <see cref="SheetGeometry.Curated"/> is: a recipe that says nothing must not be able to
    /// change what Corvus receives.
    /// </summary>
    [Fact]
    public void Format_WhenUnstated_IsWebp() =>
        Assert.Equal(SheetFormat.Webp, Recipe(string.Empty).Format);
}
