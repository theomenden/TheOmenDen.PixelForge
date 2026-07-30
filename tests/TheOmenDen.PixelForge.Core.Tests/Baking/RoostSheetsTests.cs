using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The spec-079 table seeds every layer's <see cref="AssetLayer.IsSkin"/> flag from its slot, so
/// these pin the seeding rather than the art. The body/hair cross product used to live here as
/// <c>Flattened</c>; it moves to the batch planner, which builds it from a per-slot selection.
/// </summary>
public sealed class RoostSheetsTests : IDisposable
{
    private readonly TemporaryDirectory _root = TemporaryDirectory.Create();

    /// <summary>
    /// Three directories that are never read. Everything except <c>Selection</c> composes paths
    /// without opening them, so a fixture on disk would only slow these assertions down.
    /// </summary>
    private static SourcePacks Packs { get; } = new()
    {
        CoreAssets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "core")),
        Expansion1Assets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "x1")),
        Expansion2Assets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "x2")),
    };

    public void Dispose() => _root.Dispose();

    /// <summary>
    /// The spec-079 filenames are literals in Corvus's cosmetic registry, so they are a contract,
    /// not a naming convention. Generating them from the picker's stem rule would rename the
    /// shipped deliverable without a compiler anywhere to notice.
    /// </summary>
    [Fact]
    public void All_KeepsTheContractFilenames()
    {
        var names = RoostSheets.All(Packs).AsSpan().Select(static recipe => recipe.Name).ToArray();

        // 7 bodies + 9 hair + 28 equipment. The sixteen the two-slot contract shipped keep both
        // their names and their indices; the ten-slot growth appends.
        Assert.Equal(44, names.Length);
        Assert.Contains("body-01", names, StringComparer.Ordinal);
        Assert.Contains("body-07", names, StringComparer.Ordinal);
        Assert.Contains("hair-01", names, StringComparer.Ordinal);
        Assert.Contains("hair-09", names, StringComparer.Ordinal);
        Assert.Contains("hat-01", names, StringComparer.Ordinal);
        Assert.Contains("weapon-08", names, StringComparer.Ordinal);
    }

    [Fact]
    public void All_IsBodiesThenHairThenEquipment()
    {
        var all = RoostSheets.All(Packs);

        Assert.Equal(
            RoostSheets.Bodies(Packs).Length + RoostSheets.Hair(Packs).Length + RoostSheets.Equipment(Packs).Length,
            all.Length);

        Assert.Equal("body-01", all[0].Name);
        Assert.Equal("hair-01", all[7].Name);
        Assert.StartsWith("weapon-", all[^1].Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Bodies_CarryOneRecipePerToneInOrder()
    {
        var bodies = RoostSheets.Bodies(Packs);

        Assert.Equal(SkinRamps.All.Length, bodies.Length);

        for (var i = 0; i < bodies.Length; i++)
        {
            Assert.Equal($"body-{i + 1:00}", bodies[i].Name);
            Assert.True(bodies[i].Tone.TryGet(out var tone));
            Assert.Equal(SkinRamps.All[i].Name, tone.Name);
        }
    }

    /// <summary>Bottom, top and head are all skin-bearing, so a body's every layer takes the tone.</summary>
    [Fact]
    public void Bodies_MarkEveryLayerAsSkinBearing()
    {
        foreach (var recipe in RoostSheets.Bodies(Packs))
        {
            Assert.All(recipe.Layers, layer => Assert.True(layer.IsSkin));
        }
    }

    /// <summary>
    /// Hair keeps its authored colour — some styles use skin-ramp hexes as highlights — so no
    /// hair layer may be marked skin and no hair recipe may carry a tone.
    /// </summary>
    [Fact]
    public void Hair_CarriesNoToneAndNoSkinLayers()
    {
        Assert.NotEmpty(RoostSheets.Hair(Packs));

        foreach (var recipe in RoostSheets.Hair(Packs))
        {
            Assert.False(recipe.Tone.HasValue);
            Assert.All(recipe.Layers, layer => Assert.False(layer.IsSkin));
        }
    }

    [Fact]
    public void All_BakesToTheCuratedGeometry()
        => Assert.All(RoostSheets.All(Packs), recipe => Assert.Equal(SheetGeometry.Curated, recipe.Geometry));

    /// <summary>
    /// The preset ticks the body trio and hair and nothing else — a selection the planner accepts,
    /// since the required slots are filled all-or-nothing.
    /// </summary>
    [Fact]
    public void Selection_TicksTheBodySlotsAndHair()
    {
        AssetSlot[] expected = [AssetSlot.Bottom, AssetSlot.Top, AssetSlot.Head, AssetSlot.Hair];

        var actual = RoostSheets.Selection(Catalog()).AsSpan().Select(static entry => entry.Slot).ToArray();

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Nothing offers <c>(none)</c>. On the required trio that would be
    /// <see cref="PlanFailure.RequiredSlotEmpty"/> the moment the preset was loaded.
    /// </summary>
    [Fact]
    public void Selection_OffersNoEmptyChoice() => Assert.All(
        RoostSheets.Selection(Catalog()),
        entry => Assert.All(entry.Choices, choice => Assert.True(choice.HasValue)));

    /// <summary>
    /// The fixture holds three of the nine picked hairstyles. A pack pointed somewhere wrong
    /// should show an obviously short selection, not an error dialog.
    /// </summary>
    [Fact]
    public void Selection_DropsPartialsTheCatalogueDoesNotHold()
    {
        string[] expected = ["hair1", "hair10", "hair13"];

        var hair = RoostSheets.Selection(Catalog()).AsSpan().First(static entry => entry.Slot is AssetSlot.Hair);
        var actual = hair.Choices.AsSpan().Select(static choice => choice.Value.Base).ToArray();

        Assert.Equal(expected, actual, StringComparer.Ordinal);
    }

    /// <summary>A slot the catalogue cannot fill at all is left out rather than left empty.</summary>
    [Fact]
    public void Selection_OmitsASlotItCannotFill()
    {
        var slots = RoostSheets.Selection(Catalog()).AsSpan().Select(static entry => entry.Slot).ToArray();

        Assert.DoesNotContain(AssetSlot.Weapon, slots);
    }

    /// <summary>
    /// A synthetic pack tree holding some of the spec-079 picks and none of the rest. Zero-byte
    /// files are enough — the scan reads directory entries only.
    /// </summary>
    private AssetCatalog Catalog()
    {
        var core = _root.FullPath / "core";
        var one = _root.FullPath / "exp1";
        var two = _root.FullPath / "exp2";

        WriteSlot(core, AssetSlot.Bottom, "bottom1");
        WriteSlot(core, AssetSlot.Top, "top11");
        WriteSlot(core, AssetSlot.Head, "head1");
        WriteSlot(core, AssetSlot.Hair, "hair1", "hair10", "hair99");
        WriteSlot(one, AssetSlot.Hair, "hair13");

        // Expansion 2 ships nothing here; the root still has to exist or the scan fails outright.
        Directory.CreateDirectory(two.Value);

        // The weapon slot is deliberately absent, so the preset has a slot it cannot fill.
        var scanned = AssetCatalog.Scan(new()
        {
            CoreAssets = core,
            Expansion1Assets = one,
            Expansion2Assets = two,
        });

        Assert.True(scanned.IsSuccessful);

        return scanned.Value;
    }

    private static void WriteSlot(FullPath assets, AssetSlot slot, params ReadOnlySpan<string> stems)
    {
        var directory = assets / AssetSlots.FolderName(slot);

        Directory.CreateDirectory(directory.Value);

        foreach (var stem in stems)
        {
            File.WriteAllBytes((directory / (stem + ".png")).Value, []);
        }
    }
}
