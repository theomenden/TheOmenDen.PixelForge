using System.Collections.Immutable;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// Writing one plan into more than one container.
/// <para>
/// Corvus reads WebP and neither engine can open it, so a run that serves both has to write both.
/// Expanding here rather than in the view model keeps it testable without a window — and the
/// multiplication is the part worth testing, since getting it wrong either drops a consumer's
/// files or doubles a batch nobody asked to double.
/// </para>
/// </summary>
public sealed class LayerPlanFormatTests
{
    private static ImmutableArray<SheetRecipe> Plan() =>
    [
        new() { Name = "villager_01", Directory = "heroes/villager_01", Layers = [] },
        new() { Name = "hair1", Directory = "attachments/hair", Layers = [] },
    ];

    [Fact]
    public void InFormats_StampsEveryRecipe_WhenGivenOne()
    {
        var stamped = LayerPlan.InFormats(Plan(), [SheetFormat.Png]);

        Assert.Equal(2, stamped.Length);
        Assert.All(stamped, recipe => Assert.Equal(SheetFormat.Png, recipe.Format));
    }

    /// <summary>
    /// Two containers means each recipe twice — the same reasoning
    /// <see cref="SheetGeometry"/>'s "both" already follows, since neither is a crop of the other.
    /// </summary>
    [Fact]
    public void InFormats_EmitsEachRecipeOncePerFormat()
    {
        var stamped = LayerPlan.InFormats(Plan(), [SheetFormat.Webp, SheetFormat.Png]);

        Assert.Equal(4, stamped.Length);
        Assert.Equal(2, stamped.Count(recipe => recipe.Format is SheetFormat.Webp));
        Assert.Equal(2, stamped.Count(recipe => recipe.Format is SheetFormat.Png));
    }

    /// <summary>
    /// The claim the whole two-format design rests on: writing both cannot make two workers race
    /// <c>File.Create</c> on one path, because the extension differs. If this ever failed, the
    /// geometry marking would need a format axis too.
    /// </summary>
    [Fact]
    public void InFormats_LeavesEveryPathDistinct()
    {
        var stamped = LayerPlan.InFormats(Plan(), [SheetFormat.Webp, SheetFormat.Png]);

        var paths = stamped.Select(static recipe => recipe.RelativePath).ToArray();

        Assert.Equal(paths.Length, paths.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Nothing selected stays nothing, however many containers are asked for.</summary>
    [Fact]
    public void InFormats_OfAnEmptyPlan_IsEmpty() =>
        Assert.Empty(LayerPlan.InFormats([], [SheetFormat.Webp, SheetFormat.Png]));
}
