using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.Core.Tests.Catalog;

/// <summary>
/// Pins the file-name grammar the packs actually use. The awkward cases are real files:
/// <c>bow1arrow1</c>, <c>shield1L</c>, <c>daggerL</c>, <c>daggers</c>, <c>crown1</c>.
/// </summary>
public sealed class AssetNameTests
{
    [Theory]
    [InlineData("hair15", "hair15", 0)]
    [InlineData("hair15_c3", "hair15", 3)]
    [InlineData("top0", "top0", 0)]
    [InlineData("bow1arrow1", "bow1arrow1", 0)]
    [InlineData("shield1L", "shield1L", 0)]
    [InlineData("daggerL", "daggerL", 0)]
    [InlineData("daggers", "daggers", 0)]
    [InlineData("crown1", "crown1", 0)]
    [InlineData("sword1_c2", "sword1", 2)]
    public void Split_SeparatesBaseFromColourVariant(string stem, string expectedBase, int expectedVariant)
    {
        var (actualBase, actualVariant) = AssetName.Split(stem);

        Assert.Equal(expectedBase, actualBase);
        Assert.Equal(expectedVariant, actualVariant);
    }

    /// <summary>A trailing <c>_c</c> that is not followed by digits is part of the name.</summary>
    [Theory]
    [InlineData("weird_cape")]
    [InlineData("thing_c")]
    public void Split_TreatsANonNumericSuffixAsPartOfTheBase(string stem)
    {
        var (actualBase, actualVariant) = AssetName.Split(stem);

        Assert.Equal(stem, actualBase);
        Assert.Equal(0, actualVariant);
    }

    [Fact]
    public void SortKey_OrdersNumericallyNotLexically()
    {
        var two = AssetName.SortKey("hair2", 0);
        var ten = AssetName.SortKey("hair10", 0);

        Assert.True(two.CompareTo(ten) < 0, "hair2 must sort before hair10");
    }

    [Fact]
    public void SortKey_SplitsPrefixNumberAndSuffix()
    {
        var key = AssetName.SortKey("shield1L", 0);

        Assert.Equal("shield", key.Prefix);
        Assert.Equal(1, key.Number);
        Assert.Equal("L", key.Suffix);
    }

    /// <summary>A name with no digits at all still has to order deterministically.</summary>
    [Fact]
    public void SortKey_UsesMinusOne_WhenTheNameCarriesNoNumber()
    {
        var key = AssetName.SortKey("daggers", 0);

        Assert.Equal("daggers", key.Prefix);
        Assert.Equal(-1, key.Number);
        Assert.Equal(string.Empty, key.Suffix);
    }

    [Fact]
    public void SortKey_OrdersVariantsAfterTheirBase()
    {
        var bare = AssetName.SortKey("hair1", 0);
        var variant = AssetName.SortKey("hair1", 3);

        Assert.True(bare.CompareTo(variant) < 0);
    }

    /// <summary>Folder names are the slot's own lowercase name in every pack.</summary>
    [Theory]
    [InlineData(AssetSlot.BackExtra, "backextra")]
    [InlineData(AssetSlot.BackHair, "backhair")]
    [InlineData(AssetSlot.FrontExtra, "frontextra")]
    [InlineData(AssetSlot.Weapon, "weapon")]
    public void FolderName_IsTheLowercasedSlotName(AssetSlot slot, string expected)
        => Assert.Equal(expected, AssetSlots.FolderName(slot));

    /// <summary>
    /// The evidence for this set is in the spec: 23 of 28 tops carry bare arms and hands,
    /// while weapons carry ramp hexes as wood and shield trim, not skin.
    /// </summary>
    [Fact]
    public void IsSkinBearing_IsTrueForExactlyBottomTopAndHead()
    {
        AssetSlot[] expected = [AssetSlot.Bottom, AssetSlot.Top, AssetSlot.Head];

        foreach (var slot in AssetSlots.DrawOrder)
        {
            Assert.Equal(expected.Contains(slot), AssetSlots.IsSkinBearing(slot));
        }
    }

    [Fact]
    public void DrawOrder_IsTheGeneratorsCharacterLayersOrder()
    {
        AssetSlot[] expected =
        [
            AssetSlot.Shadow, AssetSlot.BackExtra, AssetSlot.BackHair, AssetSlot.Bottom,
            AssetSlot.Top, AssetSlot.Head, AssetSlot.Hair, AssetSlot.FrontExtra,
            AssetSlot.Hat, AssetSlot.Weapon,
        ];

        Assert.Equal(expected, AssetSlots.DrawOrder);
    }
}
