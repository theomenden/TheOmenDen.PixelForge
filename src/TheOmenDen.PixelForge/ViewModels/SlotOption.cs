using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>One component in the asset browser's picker: a slot, how to label it, and how many.</summary>
/// <remarks>
/// A wrapper rather than binding <see cref="AssetSlot"/> straight into the picker, because a
/// <c>DataTemplate</c> needs properties to bind to and this SDK's XAML compiler crashes on
/// <c>x:Bind</c>'s current-object (<c>.</c>) form — the same trap <see cref="Views.PalettePage"/>
/// documents for its swatches.
/// </remarks>
/// <param name="Slot">The slot this option stands for.</param>
/// <param name="Count">How many partials the catalogue holds for it.</param>
public sealed record SlotOption(AssetSlot Slot, int Count)
{
    /// <summary>The slot, spelled the way the packs spell it.</summary>
    public string Name => AssetSlots.FolderName(Slot);

    /// <summary>How many partials sit behind this component.</summary>
    public string CountLabel => Count is 1 ? "1 partial" : $"{Count} partials";

    /// <summary>
    /// A Segoe Fluent Icons glyph, chosen to separate the categories at a glance rather than to
    /// depict a garment — the icon font has no clothing in it.
    /// </summary>
    /// <remarks>
    /// Written as escapes, not as the characters themselves. These live in Unicode's Private Use
    /// Area, so pasted literally they are invisible in an editor, survive copying badly, and make a
    /// diff unreadable. XAML has the same problem and solves it the same way — see
    /// <c>PipelinePage</c>'s <c>&amp;#xE81E;</c> icon references.
    /// </remarks>
    public string Glyph => Slot switch
    {
        AssetSlot.Shadow => "\uE8C6",
        AssetSlot.Bottom or AssetSlot.Top => "\uE81E",
        AssetSlot.Head => "\uE77B",
        AssetSlot.Hair or AssetSlot.BackHair => "\uE790",
        AssetSlot.Hat => "\uE734",
        AssetSlot.Weapon => "\uE7C4",
        _ => "\uE8B7",
    };

    /// <summary>
    /// Accent for the icon, keyed to that grouping so the picker reads as categories rather than
    /// ten equal rows.
    /// </summary>
    /// <remarks>
    /// A theme resource key rather than a colour, so this follows Light, Dark and HighContrast
    /// instead of fighting them.
    /// </remarks>
    public string AccentKey => Slot switch
    {
        AssetSlot.Shadow => "SystemFillColorNeutralBrush",
        AssetSlot.Bottom or AssetSlot.Top or AssetSlot.Head => "SystemFillColorSuccessBrush",
        AssetSlot.Hair or AssetSlot.BackHair => "SystemFillColorCautionBrush",
        AssetSlot.Hat or AssetSlot.Weapon => "SystemFillColorCriticalBrush",
        _ => "SystemFillColorAttentionBrush",
    };
}
