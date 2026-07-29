using System.Collections.Immutable;

namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>Slot metadata: where a slot's files live, and how the baker must treat them.</summary>
public static class AssetSlots
{
    /// <summary>
    /// Every slot in generator draw order. Derived from the enum rather than restated, so a new
    /// member cannot be forgotten here.
    /// </summary>
    public static ImmutableArray<AssetSlot> DrawOrder { get; } =
        [.. Enum.GetValues<AssetSlot>().AsSpan().OrderBy(static slot => (int)slot)];

    /// <summary>
    /// The directory a slot's partials live in, under a pack's <c>assets</c> folder.
    /// <para>
    /// This is the member name lowercased, which matches every folder in all three packs
    /// (<c>backextra</c>, <c>backhair</c>, <c>frontextra</c> and the rest). Verified against the
    /// packs; a mismatch would surface immediately as an empty slot in the catalogue.
    /// </para>
    /// </summary>
    /// <param name="slot">The layer whose folder is wanted.</param>
    /// <returns>The folder name, always lowercase and never <see langword="null"/>.</returns>
    public static string FolderName(AssetSlot slot) => slot.ToString().ToLowerInvariant();

    /// <summary>
    /// Whether a substitution into the chosen skin tone must be applied to this slot's layers.
    /// <para>
    /// <see langword="true"/> for exactly <see cref="AssetSlot.Bottom"/>, <see cref="AssetSlot.Top"/>
    /// and <see cref="AssetSlot.Head"/>. The evidence is a scan of every base partial for pixels in
    /// the five source-ramp hexes: 23 of 28 tops carry bare arms and hands, all 20 heads are
    /// faces, and three bottoms expose legs.
    /// </para>
    /// <para>
    /// <see cref="AssetSlot.Weapon"/> is deliberately excluded even though 13 of its 22 bases do
    /// carry ramp pixels. Hands are not on the weapon layer at all — <c>arrow1</c> is 10.7% ramp
    /// with no hand on it, while <c>sword1</c>, <c>gun1</c> and <c>wand1</c> are 0%. Those hexes
    /// are wooden shafts, bow limbs and shield trim, so recolouring them would turn a Bone-toned
    /// character's wooden bow white. This diverges from the generator, which swaps globally.
    /// </para>
    /// <para>
    /// <see cref="AssetSlot.Hair"/> and <see cref="AssetSlot.Hat"/> keep their authored colour;
    /// <c>hair1</c> (2.7%) and <c>hat4</c> (9.7%) use ramp hexes as highlights and trim.
    /// </para>
    /// </summary>
    /// <param name="slot">The layer being composited.</param>
    /// <returns>
    /// <see langword="true"/> when the layer's ramp pixels are skin and must follow the tone,
    /// <see langword="false"/> when they are authored colour and must be left alone.
    /// </returns>
    public static bool IsSkinBearing(AssetSlot slot) =>
        slot is AssetSlot.Bottom or AssetSlot.Top or AssetSlot.Head;

    /// <summary>
    /// Whether a character must have this slot filled. The generator marks every other layer
    /// <c>IsOptional</c>, so a hatless, weaponless character is legal but a headless one is not.
    /// <para>
    /// This currently names the same three slots as <see cref="IsSkinBearing"/>. That is not a
    /// coincidence worth collapsing: the required layers are the body, and the body is where the
    /// skin is. They are separate questions and a future pack could separate them.
    /// </para>
    /// </summary>
    /// <param name="slot">The layer being planned.</param>
    /// <returns>
    /// <see langword="true"/> when a selection that leaves the slot empty is invalid,
    /// <see langword="false"/> when <c>(none)</c> is a legal choice.
    /// </returns>
    public static bool IsRequired(AssetSlot slot) =>
        slot is AssetSlot.Bottom or AssetSlot.Top or AssetSlot.Head;
}
