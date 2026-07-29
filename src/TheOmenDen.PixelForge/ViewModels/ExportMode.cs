namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>
/// Which geometry a batch writes. Members are declared in the order the page's
/// <c>Segmented</c> lists them, so the control's index <em>is</em> the enum value and no lookup
/// table has to be kept in step with the XAML by hand.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the previous Layered/Flattened/Both meaning. Layering is no longer a mode: it
/// falls out of what is selected, because the recolour now runs per layer. Tick head, top and
/// bottom for a body sheet; tick hair alone for a hair sheet — which is exactly the two-texture
/// contract Corvus consumes.
/// </para>
/// </remarks>
public enum ExportMode
{
    /// <summary>The 240x1152 contract sheet only.</summary>
    Curated,

    /// <summary>The raw 1104x192 source geometry only.</summary>
    Full,

    /// <summary>Both geometries, one file each per combination.</summary>
    Both,
}
