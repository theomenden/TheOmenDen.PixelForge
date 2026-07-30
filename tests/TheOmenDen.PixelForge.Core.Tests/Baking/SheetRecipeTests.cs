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
        Assert.EndsWith(SheetWriter.Extension, Recipe("attachments/hat").RelativePath, StringComparison.Ordinal);
}
