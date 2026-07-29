namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>
/// The ten character layers the Elements generator composites, and the only slots a partial can
/// belong to.
/// <para>
/// Each member's value <em>is</em> its draw order, taken verbatim from the generator's
/// <c>Settings.json</c> <c>CharacterLayers</c> block. Compositing is therefore an ordinary sort
/// by slot rather than a second table that can drift out of step with this enum.
/// </para>
/// <para>
/// The lowercase member name is also the folder name in all three packs, so no slot-to-folder
/// map is needed either — see <see cref="AssetSlots.FolderName"/>.
/// </para>
/// </summary>
public enum AssetSlot
{
    /// <summary>Drawn first, beneath everything.</summary>
    Shadow = 0,

    /// <summary>Backpacks and tails, behind the body.</summary>
    BackExtra = 1,

    /// <summary>Long hair falling behind the body.</summary>
    BackHair = 2,

    /// <summary>Legs and lower garment. Carries bare skin on <c>bottom0</c>.</summary>
    Bottom = 3,

    /// <summary>Torso, arms and hands. Carries bare skin on 23 of its 28 bases.</summary>
    Top = 4,

    /// <summary>The face. Always skin.</summary>
    Head = 5,

    /// <summary>Hair in front of the head.</summary>
    Hair = 6,

    /// <summary>Held items and effects drawn in front of the body.</summary>
    FrontExtra = 7,

    /// <summary>Headwear, drawn over hair.</summary>
    Hat = 8,

    /// <summary>Weapons and shields, drawn last.</summary>
    Weapon = 9,
}
