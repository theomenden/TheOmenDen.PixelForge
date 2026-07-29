using System.Collections.Frozen;
using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>
/// Every partial the three configured packs hold, grouped by slot and ordered for display.
/// <para>
/// This replaces the hard-coded recipe table that preceded it. The library is roughly 995 files
/// across 156 base names, which is past the point where enumerating art in source is honest.
/// </para>
/// <para>
/// The scan reads directory entries only — no image is decoded — so it is cheap enough to re-run
/// whenever a pack path changes, which is exactly where the app calls it.
/// </para>
/// </summary>
public sealed class AssetCatalog
{
    private readonly FrozenDictionary<AssetSlot, ImmutableArray<AssetPartial>> _bySlot;

    private AssetCatalog(FrozenDictionary<AssetSlot, ImmutableArray<AssetPartial>> bySlot)
    {
        _bySlot = bySlot;

        Count = 0;

        foreach (var slot in AssetSlots.DrawOrder)
        {
            Count += bySlot[slot].Length;
        }
    }

    /// <summary>How many partials the scan found, across every slot and pack.</summary>
    public int Count { get; }

    /// <summary>
    /// Walks the three packs and indexes what they hold.
    /// <para>
    /// A slot folder that a pack does not ship is skipped, not reported: expansion 2 has no
    /// <c>frontextra</c> and only the core pack has a <c>shadow</c>, so treating an absent slot
    /// directory as an error would make every real configuration fail. Only a missing pack
    /// <em>root</em> is a failure, because that one is a path the user typed.
    /// </para>
    /// </summary>
    /// <param name="packs">The three <c>assets</c> directories to walk.</param>
    /// <returns>
    /// The catalogue, or <see cref="CatalogFailure.PackDirectoryMissing"/> when a configured root
    /// is absent, or <see cref="CatalogFailure.NoPartialsFound"/> when all three exist but hold no
    /// partial in any slot folder.
    /// </returns>
    public static Result<AssetCatalog, CatalogFailure> Scan(SourcePacks packs)
    {
        Guard.IsNotNull(packs);

        (ElementsPack Pack, FullPath Assets)[] roots =
        [
            (ElementsPack.Core, packs.CoreAssets),
            (ElementsPack.CharacterExpansion1, packs.Expansion1Assets),
            (ElementsPack.CharacterExpansion2, packs.Expansion2Assets),
        ];

        foreach (var (_, assets) in roots)
        {
            if (!Directory.Exists(assets.Value))
            {
                return new(CatalogFailure.PackDirectoryMissing);
            }
        }

        var builders = NewBuilders();
        var total = 0;

        foreach (var (pack, assets) in roots)
        {
            total += Collect(builders, pack, assets);
        }

        if (total is 0)
        {
            return new(CatalogFailure.NoPartialsFound);
        }

        return new AssetCatalog(Index(builders));
    }

    /// <summary>One empty builder per slot, so <see cref="Collect"/> never has to test for absence.</summary>
    private static Dictionary<AssetSlot, ImmutableArray<AssetPartial>.Builder> NewBuilders()
    {
        var builders = new Dictionary<AssetSlot, ImmutableArray<AssetPartial>.Builder>(AssetSlots.DrawOrder.Length);

        foreach (var slot in AssetSlots.DrawOrder)
        {
            builders[slot] = ImmutableArray.CreateBuilder<AssetPartial>();
        }

        return builders;
    }

    /// <summary>
    /// Adds every partial one pack holds into <paramref name="builders"/>, and reports how many
    /// were found.
    /// </summary>
    /// <remarks>
    /// A slot folder legitimately missing from a pack is normal rather than a failure: expansion 2
    /// ships no <c>frontextra</c>, and only the core pack ships a <c>shadow</c>. Only a missing
    /// pack <em>root</em> is <see cref="CatalogFailure.PackDirectoryMissing"/>, and the caller has
    /// already checked that.
    /// </remarks>
    private static int Collect(
        Dictionary<AssetSlot, ImmutableArray<AssetPartial>.Builder> builders,
        ElementsPack pack,
        FullPath assets)
    {
        var found = 0;

        foreach (var slot in AssetSlots.DrawOrder)
        {
            var directory = new DirectoryInfo((assets / AssetSlots.FolderName(slot)).Value);

            if (!directory.Exists)
            {
                continue;
            }

            // ZLinq.FileSystem's value-enumerable walk, the project's stated replacement for
            // Directory.EnumerateFiles + LINQ. The extension sits in the ZLinq namespace,
            // which GlobalUsings.cs already imports assembly-wide.
            foreach (var entry in directory.Children())
            {
                if (entry is not FileInfo file
                    || !file.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var (baseName, variant) = AssetName.Split(Path.GetFileNameWithoutExtension(file.Name));

                builders[slot].Add(new()
                {
                    Slot = slot,
                    Pack = pack,
                    Base = baseName,
                    Variant = variant,
                    Path = FullPath.FromPath(file.FullName),
                });

                found++;
            }
        }

        return found;
    }

    /// <summary>
    /// Freezes each slot's builder into display order — see <see cref="AssetSortKey"/> for why
    /// ordering by the parsed key rather than by name is what puts <c>hair2</c> before <c>hair10</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Projects straight into the frozen dictionary rather than filling a
    /// <see cref="Dictionary{TKey, TValue}"/> and freezing that afterwards, and sorts each builder
    /// <em>in place</em> through <c>Sort</c> rather than copying it out to an array first. That
    /// drops three intermediate allocations per slot — the staging dictionary, the array copy, and
    /// the collection-expression rebuild.
    /// </para>
    /// <para>
    /// Deliberately not a ZLinq chain: <see cref="Dictionary{TKey, TValue}"/> is not one of the
    /// types the drop-in generator covers, so a bare <c>Select</c> here would quietly bind to
    /// System.Linq. <c>ToFrozenDictionary</c> is a BCL API in any case.
    /// </para>
    /// </remarks>
    private static FrozenDictionary<AssetSlot, ImmutableArray<AssetPartial>> Index(
        Dictionary<AssetSlot, ImmutableArray<AssetPartial>.Builder> builders) =>
        builders.ToFrozenDictionary(
            static slotAndBuilder => slotAndBuilder.Key,
            static slotAndBuilder =>
            {
                slotAndBuilder.Value.Sort(static (left, right) => left.SortKey.CompareTo(right.SortKey));

                return slotAndBuilder.Value.ToImmutable();
            });

    /// <summary>
    /// Every partial in <paramref name="slot"/>, base files and colour variants alike, in display
    /// order. Empty when no pack ships that slot.
    /// </summary>
    /// <param name="slot">The character layer being listed.</param>
    /// <returns>
    /// The slot's partials, ordered by <see cref="AssetPartial.SortKey"/>, so a base always
    /// immediately precedes its own <c>_cN</c> variants.
    /// </returns>
    public ImmutableArray<AssetPartial> Partials(AssetSlot slot) => _bySlot[slot];

    /// <summary>
    /// The base files of <paramref name="slot"/> only — what the picker lists. Colour variants are
    /// reached through the per-slot variants toggle, which expands a ticked base into
    /// <see cref="Partials"/> sharing its <see cref="AssetPartial.Base"/>.
    /// </summary>
    /// <param name="slot">The character layer being listed.</param>
    /// <returns>The variant-<c>0</c> partials of the slot, in display order.</returns>
    public ImmutableArray<AssetPartial> Bases(AssetSlot slot) =>
        [.. _bySlot[slot].AsSpan().Where(static asset => asset.Variant is 0)];

    /// <summary>Every colour variant of <paramref name="baseName"/>, the base file first.</summary>
    /// <param name="slot">The character layer the base belongs to.</param>
    /// <param name="baseName">A base name as produced by <see cref="AssetName.Split"/>.</param>
    /// <returns>
    /// The base and its variants in variant order, or empty when the packs hold no such base.
    /// Matching is ordinal and whole-name, so <c>hair1</c> never picks up <c>hair10</c>.
    /// </returns>
    public ImmutableArray<AssetPartial> VariantsOf(AssetSlot slot, string baseName)
    {
        Guard.IsNotNullOrWhiteSpace(baseName);

        return
        [
            .. _bySlot[slot].AsSpan()
                .Where(asset => string.Equals(asset.Base, baseName, StringComparison.Ordinal)),
        ];
    }

    /// <summary>Looks a partial up by its identity.</summary>
    /// <param name="slot">The character layer to search.</param>
    /// <param name="baseName">The base name, without any <c>_cN</c> tail.</param>
    /// <param name="variant">The colour variant, <c>0</c> for the base file.</param>
    /// <returns>
    /// The partial, or <see cref="Optional{T}.None"/> when the packs do not hold it — which is how
    /// a stale saved selection is detected after a pack is swapped.
    /// </returns>
    public Optional<AssetPartial> Find(AssetSlot slot, string baseName, int variant)
    {
        Guard.IsNotNullOrWhiteSpace(baseName);

        foreach (var partial in _bySlot[slot])
        {
            if (partial.Variant == variant
                && string.Equals(partial.Base, baseName, StringComparison.Ordinal))
            {
                return partial;
            }
        }

        return Optional<AssetPartial>.None;
    }
}
