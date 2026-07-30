using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// What a user may type into the hero-prefix and class boxes, and what the tree refuses.
/// </summary>
public sealed class ExportNamesTests
{
    [Theory]
    [InlineData("villager", "villager")]
    [InlineData("Villager Guard", "villager-guard")]
    [InlineData("  Ranger  ", "ranger")]
    [InlineData("Tone 4 (Green)", "tone-4-green")]
    public void Slugged_LowercasesAndSeparates(string typed, string expected) =>
        Assert.Equal(expected, ExportNames.Slugged(typed));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Slugged_IsEmpty_ForNothingTyped(string? typed) =>
        Assert.Empty(ExportNames.Slugged(typed));

    /// <summary>A prefix that slugs to nothing cannot name a directory.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsUsable_IsFalse_ForNothingTyped(string? typed) =>
        Assert.False(ExportNames.IsUsable(typed));

    /// <summary>
    /// The tree owns these names. Refusing beats silently suffixing: a directory whose name differs
    /// from what was typed is noticed three runs later.
    /// </summary>
    [Theory]
    [InlineData("curated")]
    [InlineData("heroes")]
    [InlineData("attachments")]
    [InlineData("loadouts")]
    [InlineData("Curated")]
    [InlineData("  HEROES ")]
    public void IsUsable_IsFalse_ForANameTheTreeOwns(string typed) =>
        Assert.False(ExportNames.IsUsable(typed));

    [Theory]
    [InlineData("villager")]
    [InlineData("noble")]
    [InlineData("Villager Guard")]
    [InlineData("ranger")]
    public void IsUsable_IsTrue_ForAnOrdinaryName(string typed) =>
        Assert.True(ExportNames.IsUsable(typed));

    /// <summary>
    /// The reserved list is derived from the folder constants, so adding a fixed directory cannot
    /// leave a hole in it.
    /// </summary>
    [Fact]
    public void Reserved_CoversEveryFolderTheTreeCreates()
    {
        Assert.Contains(LayerPlan.HeroesFolder, ExportNames.Reserved, StringComparer.Ordinal);
        Assert.Contains(LayerPlan.AttachmentsFolder, ExportNames.Reserved, StringComparer.Ordinal);
        Assert.Contains(LoadoutWriter.Folder, ExportNames.Reserved, StringComparer.Ordinal);
    }
}
