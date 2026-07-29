using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.Core.Tests.Catalog;

/// <summary>
/// Scans a synthetic pack tree rather than the real packs: the licence keeps the art outside
/// every repo, so a test that needed it could not run on a clean checkout. The tree below
/// reproduces the shapes that actually matter — numbered bases out of lexical order, colour
/// variants, a slot present in one pack and absent from another, and the non-numeric weapon
/// names.
/// </summary>
public sealed class AssetCatalogTests : IDisposable
{
    private readonly TemporaryDirectory _root = TemporaryDirectory.Create();

    /// <summary>
    /// Every file the fixture put on disk. Expectations are derived from this rather than
    /// hand-counted: a hard-coded total restates the fixture instead of testing the scan, and
    /// drifts silently the moment a stem is added to <see cref="BuildPacks"/>.
    /// </summary>
    private readonly List<(AssetSlot Slot, string Stem)> _written = [];

    public void Dispose() => _root.Dispose();

    /// <summary>Writes a zero-byte file per name; the scan reads directory entries only.</summary>
    private void WriteSlot(FullPath assets, AssetSlot slot, params ReadOnlySpan<string> stems)
    {
        var directory = assets / AssetSlots.FolderName(slot);

        Directory.CreateDirectory(directory.Value);

        foreach (var stem in stems)
        {
            File.WriteAllBytes((directory / (stem + ".png")).Value, []);

            _written.Add((slot, stem));
        }
    }

    /// <summary>How many files the fixture wrote into <paramref name="slot"/>, across all packs.</summary>
    private int WrittenCount(AssetSlot slot) => _written.Count(entry => entry.Slot == slot);

    /// <summary>The distinct base names the fixture wrote into <paramref name="slot"/>.</summary>
    private string[] WrittenBases(AssetSlot slot) =>
    [
        .. _written
            .Where(entry => entry.Slot == slot)
            .Select(static entry => AssetName.Split(entry.Stem).Base)
            .Distinct(),
    ];

    private SourcePacks BuildPacks()
    {
        var core = _root.FullPath / "core";
        var one = _root.FullPath / "exp1";
        var two = _root.FullPath / "exp2";

        WriteSlot(core, AssetSlot.Hair, "hair1", "hair1_c1", "hair2", "hair10");
        WriteSlot(core, AssetSlot.Top, "top0", "top11", "top11_c5");
        WriteSlot(core, AssetSlot.Bottom, "bottom1");
        WriteSlot(core, AssetSlot.Head, "head1", "head1_c2");
        WriteSlot(core, AssetSlot.Weapon, "sword1", "bow1arrow1", "shield1L", "daggers");

        WriteSlot(one, AssetSlot.Hair, "hair13");
        WriteSlot(one, AssetSlot.Top, "top13");

        // Expansion 2 has no weapon folder here — a missing slot must not fail the scan.
        WriteSlot(two, AssetSlot.Hair, "hair22");

        return new()
        {
            CoreAssets = core,
            Expansion1Assets = one,
            Expansion2Assets = two,
        };
    }

    private static AssetCatalog ScanOrFail(SourcePacks packs)
    {
        var result = AssetCatalog.Scan(packs);

        Assert.True(result.IsSuccessful, $"scan failed with {result.Error}");

        return result.Value;
    }

    [Fact]
    public void Scan_FindsPartialsAcrossAllThreePacks()
    {
        var catalog = ScanOrFail(BuildPacks());
        var hair = catalog.Partials(AssetSlot.Hair);

        Assert.Equal(WrittenCount(AssetSlot.Hair), hair.Length);
        Assert.Contains(hair, p => string.Equals(p.Base, "hair13", StringComparison.Ordinal) && p.Pack == ElementsPack.CharacterExpansion1);
        Assert.Contains(hair, p => string.Equals(p.Base, "hair22", StringComparison.Ordinal) && p.Pack == ElementsPack.CharacterExpansion2);
    }

    [Fact]
    public void Scan_OrdersBasesNumerically()
    {
        var catalog = ScanOrFail(BuildPacks());

        // AsSpan() first: ImmutableArray<T> is not covered by the ZLinq drop-in, so a bare
        // .Select() here would silently bind to System.Linq.
        var names = catalog.Bases(AssetSlot.Hair).AsSpan().Select(static p => p.Base).ToArray();
        string[] expected = ["hair1", "hair2", "hair10", "hair13", "hair22"];

        Assert.Equal(expected, names);
    }

    [Fact]
    public void Bases_ExcludesColourVariants()
    {
        var catalog = ScanOrFail(BuildPacks());

        Assert.All(catalog.Bases(AssetSlot.Hair), p => Assert.Equal(0, p.Variant));

        // Top carries a variant in one pack and a plain base in another, so the two counts differ.
        Assert.Equal(WrittenBases(AssetSlot.Top).Length, catalog.Bases(AssetSlot.Top).Length);
        Assert.Equal(WrittenCount(AssetSlot.Top), catalog.Partials(AssetSlot.Top).Length);
        Assert.True(catalog.Bases(AssetSlot.Top).Length < catalog.Partials(AssetSlot.Top).Length);
    }

    [Fact]
    public void Scan_PlacesAVariantImmediatelyAfterItsBase()
    {
        var catalog = ScanOrFail(BuildPacks());
        var hair = catalog.Partials(AssetSlot.Hair);

        Assert.Equal("hair1", hair[0].Base);
        Assert.Equal(0, hair[0].Variant);
        Assert.Equal("hair1", hair[1].Base);
        Assert.Equal(1, hair[1].Variant);
    }

    [Fact]
    public void Scan_TolerantOfASlotFolderThatDoesNotExist()
    {
        var catalog = ScanOrFail(BuildPacks());

        // No pack ships a hat here, and only the core pack ships a weapon folder. Both are the
        // real-world shape (expansion 2 has no frontextra, only core has shadow) and neither is
        // allowed to fail the scan.
        Assert.Empty(catalog.Partials(AssetSlot.Hat));
        Assert.Equal(WrittenCount(AssetSlot.Weapon), catalog.Partials(AssetSlot.Weapon).Length);
    }

    [Fact]
    public void Scan_KeepsNonNumericWeaponNamesIntact()
    {
        var catalog = ScanOrFail(BuildPacks());
        var names = catalog.Partials(AssetSlot.Weapon).AsSpan().Select(static p => p.Base).ToArray();

        Assert.Contains("bow1arrow1", names, StringComparer.Ordinal);
        Assert.Contains("shield1L", names, StringComparer.Ordinal);
        Assert.Contains("daggers", names, StringComparer.Ordinal);
    }

    [Fact]
    public void Count_TotalsEveryPartialAcrossEverySlot()
    {
        var catalog = ScanOrFail(BuildPacks());

        Assert.Equal(_written.Count, catalog.Count);
    }

    [Fact]
    public void VariantsOf_ReturnsTheBaseAndItsColourVariants()
    {
        var catalog = ScanOrFail(BuildPacks());
        var variants = catalog.VariantsOf(AssetSlot.Hair, "hair1");

        Assert.NotEmpty(variants);
        Assert.All(variants, p => Assert.Equal("hair1", p.Base));
        Assert.Equal(0, variants[0].Variant);

        // hair10 shares hair1's leading characters, so a prefix match would sweep it in here.
        Assert.DoesNotContain(variants, p => string.Equals(p.Base, "hair10", StringComparison.Ordinal));
        Assert.All(catalog.VariantsOf(AssetSlot.Hair, "hair10"), p => Assert.Equal("hair10", p.Base));
    }

    [Fact]
    public void Find_LocatesAPartialByItsIdentity()
    {
        var catalog = ScanOrFail(BuildPacks());

        Assert.True(catalog.Find(AssetSlot.Top, "top11", 5).TryGet(out var found));
        Assert.Equal("top11_c5.png", found.FileName);
        Assert.Equal("top11c5", found.Stem);

        Assert.False(catalog.Find(AssetSlot.Top, "top11", 9).HasValue);
    }

    [Fact]
    public void Scan_ReportsPackDirectoryMissing_WhenARootIsNotThere()
    {
        var packs = BuildPacks() with
        {
            Expansion2Assets = _root.FullPath / "nope",
        };

        var result = AssetCatalog.Scan(packs);

        Assert.False(result.IsSuccessful);
        Assert.Equal(CatalogFailure.PackDirectoryMissing, result.Error);
    }

    [Fact]
    public void Scan_ReportsNoPartialsFound_WhenEveryPackIsEmpty()
    {
        var empty = _root.FullPath / "bare";

        Directory.CreateDirectory(empty.Value);

        var result = AssetCatalog.Scan(new()
        {
            CoreAssets = empty,
            Expansion1Assets = empty,
            Expansion2Assets = empty,
        });

        Assert.False(result.IsSuccessful);
        Assert.Equal(CatalogFailure.NoPartialsFound, result.Error);
    }
}
