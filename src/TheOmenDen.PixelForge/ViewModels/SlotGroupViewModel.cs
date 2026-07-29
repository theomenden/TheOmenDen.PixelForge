using System.Collections.Immutable;
using System.ComponentModel;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI.Collections;
using DotNext;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>
/// One slot's picker: its base files, a filter over them, and what they contribute to a plan.
/// </summary>
/// <remarks>
/// <para>
/// The rows are the slot's <em>base</em> files only. A slot such as <see cref="AssetSlot.Hair"/>
/// holds several hundred partials once colour variants are counted, and a list that long is not a
/// list anyone picks from — <see cref="IncludeVariants"/> is the axis instead.
/// </para>
/// <para>
/// <see cref="Items"/> is an <see cref="AdvancedCollectionView"/> for filtering only, never
/// sorting. The catalogue already returns display order, and the sorted alternative would need
/// <c>SortDescription</c>, whose string-property form resolves reflectively and is not trim-safe —
/// Release publishes <c>PublishTrimmed=true</c>.
/// </para>
/// </remarks>
public sealed partial class SlotGroupViewModel : ObservableObject
{
    private readonly List<PartialSelectionItem> _bases;

    /// <summary>Builds the picker for one slot.</summary>
    /// <param name="slot">The character layer this group fills.</param>
    /// <param name="bases">The slot's base files, already in catalogue display order.</param>
    public SlotGroupViewModel(AssetSlot slot, ImmutableArray<AssetPartial> bases)
    {
        Slot = slot;

        _bases = [.. bases.AsSpan().Select(static partial => new PartialSelectionItem(partial))];

        foreach (var item in _bases)
        {
            // Never unsubscribed: the items are owned by this group and die with it.
            item.PropertyChanged += OnItemChanged;
        }

        Items = new AdvancedCollectionView(_bases, isLiveShaping: false)
        {
            Filter = Matches,
        };
    }

    /// <summary>Which character layer these rows fill.</summary>
    public AssetSlot Slot { get; }

    /// <summary>The slot's name as a person reads it.</summary>
    /// <remarks>
    /// Only the compound members need spelling out; the rest are already one word, so a full
    /// ten-arm table would restate the enum for no gain.
    /// </remarks>
    public string Header => Slot switch
    {
        AssetSlot.BackExtra => "Back extra",
        AssetSlot.BackHair => "Back hair",
        AssetSlot.FrontExtra => "Front extra",
        _ => Slot.ToString(),
    };

    /// <summary>Addressable per slot, since ten of these share one page.</summary>
    public string ExpanderAutomationId => $"SlotExpander_{Slot}";

    /// <summary>What the header line says about this slot at a glance.</summary>
    public string Description => SelectedCount is 0
        ? $"{_bases.Count} available"
        : $"{SelectedCount} of {_bases.Count} selected";

    /// <summary>
    /// Whether <c>(none)</c> is a legal choice here — the inverse of
    /// <see cref="AssetSlots.IsRequired"/>. A character may go hatless; it may not go headless.
    /// </summary>
    public bool AllowNone => !AssetSlots.IsRequired(Slot);

    /// <summary>The rows, filtered by <see cref="Filter"/>.</summary>
    public IAdvancedCollectionView Items { get; }

    /// <summary>
    /// Whether a ticked base also brings its <c>_cN</c> colour variants into the plan.
    /// </summary>
    /// <remarks>
    /// Applied in <see cref="ToSelection"/> rather than by growing <see cref="Items"/>, so the row
    /// count never changes under the user mid-selection.
    /// </remarks>
    [ObservableProperty]
    public partial bool IncludeVariants { get; set; }

    /// <summary>
    /// Substring match over <see cref="PartialSelectionItem.Name"/>, case-insensitive. Empty shows
    /// everything.
    /// </summary>
    public string Filter
    {
        get => field;
        set
        {
            if (string.Equals(field, value, StringComparison.Ordinal))
            {
                return;
            }

            field = value ?? string.Empty;

            Items.RefreshFilter();

            OnPropertyChanged();
        }
    } = string.Empty;

    /// <summary>How many rows are ticked, filtered or not.</summary>
    public int SelectedCount
    {
        get => field;
        private set
        {
            if (field == value)
            {
                return;
            }

            field = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(Description));
        }
    }

    /// <summary>
    /// What this slot contributes to a plan.
    /// </summary>
    /// <param name="catalog">The catalogue a ticked base's colour variants are looked up in.</param>
    /// <returns>
    /// The slot's choices, empty when nothing is ticked — which <see cref="BatchPlan"/> drops
    /// rather than treating as an axis of zero.
    /// </returns>
    /// <remarks>
    /// The list always shows base files; <see cref="IncludeVariants"/> is applied here rather than
    /// by growing the list, so ticking <c>hair15</c> can mean one file or eight without the row
    /// count changing under the user mid-selection.
    /// </remarks>
    public SlotSelection ToSelection(AssetCatalog catalog)
    {
        Guard.IsNotNull(catalog);

        var choices = ImmutableArray.CreateBuilder<Optional<AssetPartial>>();

        foreach (var item in _bases)
        {
            if (!item.IsSelected)
            {
                continue;
            }

            if (!IncludeVariants)
            {
                choices.Add(item.Partial);

                continue;
            }

            foreach (var variant in catalog.VariantsOf(Slot, item.Partial.Base))
            {
                choices.Add(variant);
            }
        }

        // Prepended, not appended, so "leave this slot empty" is the first combination the
        // odometer visits and the plainest sheet is the first one written.
        if (AllowNone && choices.Count is not 0)
        {
            choices.Insert(0, Optional<AssetPartial>.None);
        }

        return new()
        {
            Slot = Slot,
            Choices = choices.ToImmutable(),
        };
    }

    /// <summary>Ticks exactly the rows <paramref name="choices"/> names, and unticks the rest.</summary>
    /// <param name="choices">
    /// A preset's choices for this slot, as produced by <c>RoostSheets.Selection</c>. An
    /// <see cref="Optional{T}.None"/> entry matches nothing, since the list holds only real files.
    /// </param>
    public void Apply(ImmutableArray<Optional<AssetPartial>> choices)
    {
        foreach (var item in _bases)
        {
            item.IsSelected = !choices.IsDefaultOrEmpty && choices.Contains(item.Partial);
        }
    }

    /// <summary>Records a bake outcome against every row the baked stem names.</summary>
    /// <param name="segments">
    /// The recipe's stem split on <c>_</c>. Whole segments, because <c>top1</c> is a prefix of
    /// <c>top11</c> and a substring test would mark both.
    /// </param>
    /// <param name="status">What to show — a size, or a failure name.</param>
    /// <param name="isSuccess">Whether the sheet was written.</param>
    /// <remarks>
    /// Failures are sticky. One row feeds many sheets once the cross product is wide, so a later
    /// success must not paper over an earlier failure on the same row.
    /// </remarks>
    public void Mark(ReadOnlySpan<string> segments, string status, bool isSuccess)
    {
        foreach (var item in _bases)
        {
            if (!segments.Any(segment => string.Equals(segment, item.Name, StringComparison.Ordinal)))
            {
                continue;
            }

            if (!isSuccess || item.Status.Length is 0)
            {
                item.Status = status;
            }
        }
    }

    /// <summary>Clears every row's bake outcome, ready for a fresh run.</summary>
    public void ClearStatuses()
    {
        foreach (var item in _bases)
        {
            item.Status = string.Empty;
        }
    }

    private bool Matches(object item) =>
        Filter.Length is 0
        || (item is PartialSelectionItem row && row.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase));

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(PartialSelectionItem.IsSelected))
        {
            return;
        }

        var selected = 0;

        foreach (var item in _bases)
        {
            if (item.IsSelected)
            {
                selected++;
            }
        }

        SelectedCount = selected;
    }
}
