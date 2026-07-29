using CommunityToolkit.Mvvm.ComponentModel;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>One selectable sheet, and how its bake went.</summary>
public sealed partial class SheetSelectionItem(SheetRecipe recipe) : ObservableObject
{
    public SheetRecipe Recipe { get; } = recipe;

    public string Name => Recipe.Name;

    public string AutomationId => $"Sheet_{Recipe.Name}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;
}
