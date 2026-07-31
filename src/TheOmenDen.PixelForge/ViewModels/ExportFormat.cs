namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>
/// Which container a batch writes. Members are declared in the order the page's
/// <c>Segmented</c> lists them, so the control's index <em>is</em> the enum value and no lookup
/// table has to be kept in step with the XAML by hand.
/// </summary>
/// <remarks>
/// A peer of <see cref="ExportMode"/>, not a mode of it. The two are independent: geometry decides
/// what a sheet contains and this decides what can open it, and a run legitimately writes one
/// geometry into both containers for two different consumers.
/// </remarks>
public enum ExportFormat
{
    /// <summary>Lossless WebP only — what Corvus consumes.</summary>
    Webp,

    /// <summary>PNG only — the only container Unity and MonoGame can open.</summary>
    Png,

    /// <summary>Both containers, one file each per combination.</summary>
    Both,
}
