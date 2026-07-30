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
    [InlineData("Chevalier Éclair", "chevalier-eclair")]
    public void Slugged_LowercasesAndSeparates(string typed, string expected) =>
        Assert.Equal(expected, ExportNames.Slugged(typed));

    /// <summary>
    /// Every character the slug drops from the front leaves a separator behind, and whitespace is
    /// only the most obvious source. Trimming the input would catch the first of these and none of
    /// the rest — which is why the trim is applied to the result.
    /// </summary>
    [Theory]
    [InlineData("  Ranger", "ranger")]
    [InlineData("🗡️ranger", "ranger")]
    [InlineData("...ranger", "ranger")]
    [InlineData("(ranger)", "ranger")]
    public void Slugged_NeverStartsWithASeparator(string typed, string expected) =>
        Assert.Equal(expected, ExportNames.Slugged(typed));

    /// <summary>
    /// A typed name cannot climb out of the export folder: separators and dots are not in the
    /// allowed ranges, so the slug is the boundary rather than something checked beside one.
    /// </summary>
    [Theory]
    [InlineData(@"..\..\escape")]
    [InlineData("../../escape")]
    [InlineData("/etc/passwd")]
    public void Slugged_CannotEscapeTheOutputFolder(string typed)
    {
        var slug = ExportNames.Slugged(typed);

        Assert.DoesNotContain('/', slug);
        Assert.DoesNotContain('\\', slug);
        Assert.DoesNotContain('.', slug);
        Assert.False(Path.IsPathRooted(slug));
    }

    /// <summary>
    /// Text with nothing in the allowed ranges slugs to nothing, and an unusable prefix is refused
    /// rather than silently becoming a directory named for whatever survived.
    /// </summary>
    [Theory]
    [InlineData("村人")]
    [InlineData("🗡️")]
    [InlineData("...")]
    public void Slugged_IsEmpty_WhenNothingSurvives(string typed)
    {
        Assert.Empty(ExportNames.Slugged(typed));
        Assert.False(ExportNames.IsUsable(typed));
    }

    /// <summary>
    /// The library caps a segment at 80 by default, which is a tighter bound on a directory name
    /// than anything worth restating — so a pathological prefix cannot push a path toward MAX_PATH.
    /// </summary>
    [Fact]
    public void Slugged_IsBoundedInLength() =>
        Assert.True(ExportNames.Slugged(new string('a', 500)).Length <= 80);

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
