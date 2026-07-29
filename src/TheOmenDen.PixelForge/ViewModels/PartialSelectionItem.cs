using CommunityToolkit.Mvvm.ComponentModel;
using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>One tickable partial in a slot's list, and how its bake went.</summary>
/// <remarks>
/// Replaces <c>SheetSelectionItem</c>, which listed whole recipes. A row is now one source file,
/// because the recipe is what the cross product produces rather than what the user picks.
/// </remarks>
/// <param name="partial">The file this row stands for.</param>
public sealed partial class PartialSelectionItem(AssetPartial partial) : ObservableObject
{
    /// <summary>The file this row stands for.</summary>
    public AssetPartial Partial { get; } = partial;

    /// <summary>
    /// What the row reads as, and the segment a baked stem carries — see
    /// <see cref="AssetPartial.Stem"/>. One property rather than a display name beside a matching
    /// key, so progress marking cannot drift from the label it marks.
    /// </summary>
    public string Name => Partial.Stem;

    /// <summary>
    /// Unique across the whole page, not just this slot: ten lists share the namespace, and
    /// <c>top1</c> and <c>hat1</c> would otherwise both be <c>Sel_1</c>.
    /// </summary>
    public string AutomationId => $"Sel{Partial.Slot}_{Partial.Stem}";

    /// <summary>
    /// Whether this partial contributes to the plan. Unticked by default: the library is roughly
    /// 995 files, so a list that started fully ticked would plan an astronomical first run.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The last thing a run said about a sheet this partial is in, or empty.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;
}
