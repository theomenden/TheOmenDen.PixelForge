using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.Core.Tests.Catalog;

/// <summary>
/// The layer order a paper-doll consumer stacks by, and the invariants a proposed "fix" for the
/// <c>arms_up</c> clip would have broken.
/// </summary>
/// <remarks>
/// <para>
/// Spec 079 reported that <c>arms_up</c> draws <c>top</c> above hair and proposed reordering.
/// Three checks against the Core pack disagreed: the generator's own <c>Settings.json</c> gives
/// "Arms Up" <c>ReverseDrawOrder: false</c>, compositing in this order renders correctly across
/// all four facings and every core hairstyle tried, and the alternative renders far worse.
/// </para>
/// <para>
/// The pixel comparison lives in the spec's revision log, because it needs the licensed art and
/// this project ships none. What is guarded here is the ordering rule itself — which is what the
/// proposal would have changed, and what a consumer now stacks by unconditionally.
/// </para>
/// </remarks>
public sealed class DrawOrderTests
{
    private static int Position(AssetSlot slot) => AssetSlots.DrawOrder.IndexOf(slot);

    /// <summary>
    /// Hair draws after the body, not before it.
    /// </summary>
    /// <remarks>
    /// The transitive trap that sank the proposal: putting hair below <c>top</c> necessarily puts
    /// it below <c>head</c> too, because <c>top</c> is below <c>head</c>. The face then draws over
    /// the hair and erases it, which is a far worse defect than the one being fixed.
    /// </remarks>
    [Fact]
    public void Hair_DrawsAfterTheWholeBody()
    {
        Assert.True(Position(AssetSlot.Hair) > Position(AssetSlot.Top));
        Assert.True(Position(AssetSlot.Hair) > Position(AssetSlot.Head));
        Assert.True(Position(AssetSlot.Hair) > Position(AssetSlot.Bottom));
    }

    /// <summary>The body composites bottom, then top, then head.</summary>
    [Fact]
    public void TheBodyCompositesInAnatomicalOrder()
    {
        Assert.True(Position(AssetSlot.Bottom) < Position(AssetSlot.Top));
        Assert.True(Position(AssetSlot.Top) < Position(AssetSlot.Head));
    }

    /// <summary>
    /// The art splits "behind" and "in front" into their own slots rather than reordering per
    /// frame, which is how the generator sidesteps direction-dependent depth for extras.
    /// </summary>
    [Fact]
    public void BackLayersDrawBehindTheBodyAndFrontLayersInFrontOfIt()
    {
        Assert.True(Position(AssetSlot.BackExtra) < Position(AssetSlot.Bottom));
        Assert.True(Position(AssetSlot.BackHair) < Position(AssetSlot.Bottom));
        Assert.True(Position(AssetSlot.FrontExtra) > Position(AssetSlot.Head));
    }

    /// <summary>Shadow is under everything and the weapon is over everything.</summary>
    [Fact]
    public void ShadowIsFirstAndWeaponIsLast()
    {
        Assert.Equal(0, Position(AssetSlot.Shadow));
        Assert.Equal(AssetSlots.DrawOrder.Length - 1, Position(AssetSlot.Weapon));
    }

    /// <summary>
    /// The order is the enum's own, so a consumer that stacks by the slot's value and one that
    /// stacks by this list can never disagree.
    /// </summary>
    [Fact]
    public void DrawOrderIsTheEnumsOwnOrder()
    {
        for (var i = 0; i < AssetSlots.DrawOrder.Length; i++)
        {
            Assert.Equal(i, (int)AssetSlots.DrawOrder[i]);
        }
    }
}
