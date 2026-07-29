using CommunityToolkit.Mvvm.ComponentModel;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>One tickable skin ramp on the tone axis.</summary>
/// <remarks>
/// The tone axis only multiplies combinations that actually carry skin, so ticking every tone
/// costs nothing on a hair-only selection — see <c>BatchPlan</c>'s remarks.
/// </remarks>
/// <param name="ramp">The ramp this swatch stands for.</param>
public sealed partial class ToneSelectionItem(SkinRamp ramp) : ObservableObject
{
    /// <summary>The ramp this swatch stands for.</summary>
    public SkinRamp Ramp { get; } = ramp;

    /// <summary>What the swatch reads as, and what the stem's tone segment is slugged from.</summary>
    public string Name => Ramp.Name;

    /// <summary>Whether this tone is on the axis.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
