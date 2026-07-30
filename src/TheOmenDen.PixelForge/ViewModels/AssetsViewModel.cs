using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DotNext;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Spritesheets;
using TheOmenDen.PixelForge.Services;

namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>
/// Browses the scanned catalogue one slot at a time, and describes what the preview should play.
/// </summary>
/// <remarks>
/// <para>
/// Organised by slot rather than as one flat list of 995: the slot is the axis the art is authored
/// on, it is what <see cref="AssetCatalog.Partials"/> indexes by, and it keeps a realized grid to
/// tens of tiles rather than hundreds.
/// </para>
/// <para>
/// Holds no image types. What to draw is a <see cref="SheetRecipe"/> and a
/// <see cref="GeneratorClip"/>; turning those into pixels is the view's job, the same division
/// <see cref="BatchExportViewModel"/> keeps with <c>CompositePreview</c>.
/// </para>
/// </remarks>
public sealed partial class AssetsViewModel : ObservableObject
{
    /// <summary>The body the selection is previewed over, matching the spec-079 assembly.</summary>
    private static readonly (AssetSlot Slot, string Stem)[] BaseBody =
    [
        (AssetSlot.Bottom, "bottom1"),
        (AssetSlot.Top, "top11"),
        (AssetSlot.Head, "head1"),
    ];

    private readonly CatalogService _catalog;
    private readonly SourcePackService _packs;

    /// <summary>
    /// Which component's partials are currently in <see cref="Partials"/>, if any have been loaded.
    /// </summary>
    /// <remarks>
    /// <see cref="Optional{T}"/> rather than a nullable or a sentinel member: "nothing loaded yet"
    /// is a real state distinct from every slot, and <see cref="AssetSlot"/> has no spare value to
    /// mean it — 0 is Shadow, because the enum's value is its draw order.
    /// </remarks>
    private Optional<AssetSlot> _loaded = Optional<AssetSlot>.None;

    /// <summary>Wires the page to the catalogue it browses.</summary>
    /// <param name="catalog">The scanned partials.</param>
    /// <param name="packs">Where those partials came from, for composing the base body.</param>
    public AssetsViewModel(CatalogService catalog, SourcePackService packs)
    {
        _catalog = catalog;
        _packs = packs;

        _catalog.Changed += (_, _) => Reload();

        foreach (var clip in GeneratorClips.All)
        {
            Clips.Add(clip);
        }

        for (var row = 0; row < GeneratorClips.Facings.Length; row++)
        {
            FacingOptions.Add(new(row, GeneratorClips.Facings[row]));
        }

        SelectedClip = GeneratorClips.All.AsValueEnumerable().First(static c => c.Name is "walk");

        Reload();
    }

    /// <summary>Every component that has art, in generator draw order.</summary>
    public ObservableCollection<SlotOption> Slots { get; } = [];

    /// <summary>The partials of <see cref="SelectedSlot"/>.</summary>
    public ObservableCollection<AssetPartial> Partials { get; } = [];

    /// <summary>Every animation the generator ships, including the ones curated output drops.</summary>
    public ObservableCollection<GeneratorClip> Clips { get; } = [];

    /// <summary>Facings, south first — the source row order.</summary>
    /// <remarks>
    /// Built once from the generator's own table, so the picker cannot list a direction the sheets
    /// do not have. The row is carried alongside the name because it is what
    /// <c>SpriteFilmstrip.RenderCell</c> takes.
    /// </remarks>
    public ObservableCollection<FacingOption> FacingOptions { get; } = [];

    /// <summary>Whether the packs resolve at all; drives the page's warning bar.</summary>
    public bool PacksMissing => !_packs.Resolved.HasValue;

    /// <summary>How many partials the catalogue holds across every slot.</summary>
    public int TotalCount => _catalog.Current.TryGet(out var catalog) ? catalog.Count : 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewRecipe))]
    public partial AssetSlot SelectedSlot { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewRecipe))]
    public partial AssetPartial? SelectedPartial { get; set; }

    /// <summary>Whether to composite the selection onto a base body.</summary>
    /// <remarks>
    /// On by default because a hat, a weapon or back hair animating against nothing is very hard to
    /// judge. Off is what you want for checking a partial's own alignment.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewRecipe))]
    public partial bool ShowOverBody { get; set; } = true;

    [ObservableProperty]
    public partial GeneratorClip SelectedClip { get; set; }

    [ObservableProperty]
    public partial int SelectedFacing { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; } = true;

    /// <summary>
    /// What the preview should draw, or nothing when there is no selection to draw.
    /// </summary>
    /// <remarks>
    /// A recipe rather than a bitmap, so this stays unit-testable and the view decides how to
    /// rasterise it. The base body is dropped when the selection already fills one of its slots —
    /// previewing <c>head4</c> over <c>head1</c> would hide the very thing being looked at.
    /// </remarks>
    public Optional<SheetRecipe> PreviewRecipe
    {
        get
        {
            if (SelectedPartial is not { } chosen)
            {
                return Optional<SheetRecipe>.None;
            }

            var chosenSlots = new List<AssetPartial>(BaseBody.Length + 1) { chosen };

            if (ShowOverBody && _catalog.Current.TryGet(out var catalog))
            {
                foreach (var (slot, stem) in BaseBody)
                {
                    // Skipped when the selection already fills that slot: previewing head4 over
                    // head1 would hide the very thing being looked at.
                    if (slot != chosen.Slot && catalog.Find(slot, stem, variant: 0).TryGet(out var part))
                    {
                        chosenSlots.Add(part);
                    }
                }
            }

            // Back to front. AssetSlot's value IS its draw order, so ordering by the enum is the
            // compositing order rather than an approximation of it — ordering by folder name would
            // be alphabetical, which puts backhair before bottom and hat before head.
            chosenSlots.Sort(static (left, right) => left.Slot.CompareTo(right.Slot));

            return new SheetRecipe
            {
                Name = chosen.Stem,
                Layers = [.. chosenSlots.AsValueEnumerable()
                    .Select(static p => new AssetLayer(p.Path, AssetSlots.IsSkinBearing(p.Slot)))],
            };
        }
    }

    /// <summary>Re-reads the catalogue after a rescan.</summary>
    private void Reload()
    {
        Slots.Clear();
        Partials.Clear();

        _loaded = Optional<AssetSlot>.None;

        OnPropertyChanged(nameof(PacksMissing));
        OnPropertyChanged(nameof(TotalCount));

        if (!_catalog.Current.TryGet(out var catalog))
        {
            return;
        }

        foreach (var slot in AssetSlots.DrawOrder)
        {
            var partials = catalog.Partials(slot);

            if (!partials.IsDefaultOrEmpty)
            {
                Slots.Add(new(slot, partials.Length));
            }
        }

        if (Slots.Count is not 0)
        {
            Select(Slots[0].Slot);
        }
    }

    /// <summary>Chooses a component and loads its partials.</summary>
    /// <param name="slot">The component to show.</param>
    /// <remarks>
    /// <para>
    /// One method rather than a change handler on <see cref="SelectedSlot"/>, because a change
    /// handler cannot be relied on to run. <see cref="AssetSlot.Shadow"/> is 0 — its value is its
    /// draw order, so it has to be — which makes it <c>default(AssetSlot)</c> too. Assigning Shadow
    /// to a freshly constructed view model therefore raised no change, the handler never fired, and
    /// the first component in the picker came up empty while every other one worked.
    /// </para>
    /// <para>
    /// Loading is driven by the call, not by whether a value differed. Repeats are cheap: the slot
    /// already loaded is remembered, so re-selecting it does nothing.
    /// </para>
    /// </remarks>
    public void Select(AssetSlot slot)
    {
        SelectedSlot = slot;

        if (_loaded.TryGet(out var already) && already == slot)
        {
            return;
        }

        Partials.Clear();
        SelectedPartial = null;

        if (!_catalog.Current.TryGet(out var catalog))
        {
            return;
        }

        foreach (var partial in catalog.Partials(slot))
        {
            Partials.Add(partial);
        }

        _loaded = slot;

        if (Partials.Count is not 0)
        {
            SelectedPartial = Partials[0];
        }
    }
}
