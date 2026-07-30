namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>One direction in the preview's facing picker.</summary>
/// <remarks>
/// A wrapper rather than binding the bare facing name, for the same reason
/// <see cref="SlotOption"/> is one: a <c>DataTemplate</c> needs properties, and this SDK's XAML
/// compiler crashes on <c>x:Bind</c>'s current-object (<c>.</c>) form.
/// </remarks>
/// <param name="Row">The source row this facing occupies, which is also its index.</param>
/// <param name="Name">The facing, as the generator names it.</param>
public sealed record FacingOption(int Row, string Name)
{
    /// <summary>
    /// A chevron pointing the way the sprite faces.
    /// </summary>
    /// <remarks>
    /// Escapes rather than the characters themselves — these are Private Use Area codepoints, and
    /// pasted literally they leave source that cannot be read in an editor or followed in a diff.
    /// </remarks>
    public string Glyph => Name switch
    {
        "south" => "\uE70D",
        "west" => "\uE76B",
        "east" => "\uE76C",
        "north" => "\uE70E",
        _ => "\uE9CE",
    };

    /// <summary>
    /// Whether the curated contract keeps this facing.
    /// </summary>
    /// <remarks>
    /// North is dropped from every shipped sheet — the wander model fixes <c>y</c> per avatar, so
    /// nothing ever walks away from the camera. Worth saying in the picker, because previewing a
    /// facing that will not be exported is otherwise a silent surprise.
    /// </remarks>
    public bool IsExported => !string.Equals(Name, "north", StringComparison.Ordinal);

    /// <summary>The inverse, for a template that only shows a marker on the facings that do not ship.</summary>
    public bool IsNotExported => !IsExported;

    /// <summary>A short note for the facings that do not ship, and nothing for the ones that do.</summary>
    public string Note => IsExported ? string.Empty : "not exported";
}
