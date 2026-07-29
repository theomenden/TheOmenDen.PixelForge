using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;
using TheOmenDen.PixelForge.Services;

namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>
/// Picks partials slot by slot, plans the cross product they describe, runs it, and reports each
/// sheet as it lands.
/// </summary>
/// <remarks>
/// The selection model is the catalogue, not a fixed table of recipes: what gets baked is
/// <see cref="BatchPlan.Expand"/>'s expansion of one <see cref="SlotSelection"/> per slot across
/// the ticked tones.
/// </remarks>
public sealed partial class BatchExportViewModel : ObservableObject
{
    /// <summary>
    /// Above this many files the run is announced rather than blocked. A four-figure batch is a
    /// legitimate thing to ask for; silently starting one is not.
    /// </summary>
    private const int WarnThreshold = 1000;

    private const ExportMode DefaultMode = ExportMode.Curated;

    private const string CuratedDescription =
        "The 240x1152 contract sheet: eight clips on three facings, north dropped. This is the geometry Corvus consumes.";

    private const string FullDescription =
        "The raw 1104x192 assembly: every source column on all four facings, keeping the north facing and the draws the contract sheet drops.";

    private const string BothDescription =
        "Both geometries, one file each per combination — twice the files, with index.csv and clips.csv beside them.";

    private readonly SourcePackService _packs;
    private readonly CatalogService _catalog;
    private readonly PickerService _picker;
    private bool _reloadPending;

    /// <summary>Wires the page to the services that decide what can be baked.</summary>
    /// <param name="packs">Whether the three pack roots resolve at all.</param>
    /// <param name="catalog">The scanned partials the picker lists.</param>
    /// <param name="ramps">The tones on offer — the seven shipped ones plus the user's own.</param>
    /// <param name="picker">The folder picker behind <c>Browse</c>.</param>
    /// <remarks>
    /// Only <see cref="CatalogService.Changed"/> is subscribed, not
    /// <see cref="SourcePackService.Changed"/> too: the catalogue rescans on a path change and
    /// raises its own event afterwards, so listening to both would reload twice and the first
    /// reload would read a catalogue that had not caught up.
    /// </remarks>
    public BatchExportViewModel(
        SourcePackService packs,
        CatalogService catalog,
        RampService ramps,
        PickerService picker)
    {
        _packs = packs;
        _catalog = catalog;
        _picker = picker;

        _catalog.Changed += (_, _) => Reload();

        // Snapshotted once. A ramp added from the palette page afterwards needs a restart to
        // appear here — the tone axis is a batch concern and does not warrant live shaping.
        foreach (var ramp in ramps.Ramps)
        {
            var tone = new ToneSelectionItem(ramp)
            {
                IsSelected = string.Equals(ramp.Name, SkinRamps.Source.Name, StringComparison.OrdinalIgnoreCase),
            };

            tone.PropertyChanged += OnSelectionChanged;

            Tones.Add(tone);
        }

        Reload();
    }

    /// <summary>One picker per slot the catalogue can fill, in generator draw order.</summary>
    public ObservableCollection<SlotGroupViewModel> Groups { get; } = [];

    /// <summary>The tone axis. Only combinations that carry skin are multiplied by it.</summary>
    public ObservableCollection<ToneSelectionItem> Tones { get; } = [];

    /// <summary>Index into the mode Segmented — no converter needed.</summary>
    public int SelectedModeIndex
    {
        get => field;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(ModeDescription));
            OnPropertyChanged(nameof(PlannedCount));

            ExportCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// The mode the selected index means. <see cref="ExportMode"/>'s members are declared in the
    /// order the page lists them, so the index <em>is</em> the enum value — no table of literals
    /// that has to be kept in step with the XAML by hand.
    /// </summary>
    /// <remarks>
    /// An index outside the enum is the control clearing itself (-1) rather than a choice. An
    /// unchecked cast would make that <see cref="ExportMode.Both"/> — twice the files — so the
    /// fallback is deliberately the cheapest mode, not the largest.
    /// </remarks>
    public ExportMode Mode => Enum.IsDefined((ExportMode)SelectedModeIndex)
        ? (ExportMode)SelectedModeIndex
        : DefaultMode;

    /// <summary>The tradeoff, stated where the choice is made.</summary>
    public string ModeDescription => Mode switch
    {
        ExportMode.Full => FullDescription,
        ExportMode.Both => BothDescription,
        _ => CuratedDescription,
    };

    /// <summary>Where sheets and manifests are written.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial string OutputFolder { get; private set; } = string.Empty;

    /// <summary>Whether the three pack roots resolve; drives the page's warning bar.</summary>
    public bool PacksMissing => !_packs.Resolved.HasValue;

    /// <summary>Whether a run is in flight.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial bool IsExporting { get; private set; }

    /// <summary>Run progress as a percentage, for the bar.</summary>
    [ObservableProperty]
    public partial double ProgressValue { get; private set; }

    /// <summary>Run progress as <c>completed / total</c>, for the caption beside the bar.</summary>
    [ObservableProperty]
    public partial string ProgressText { get; private set; } = string.Empty;

    /// <summary>
    /// Raised for anything worth telling the user. The page feeds these to a
    /// <c>StackedNotificationsBehavior</c>, which queues them — one run can report several
    /// distinct failures, and a single string property would show only the last.
    /// </summary>
    public event EventHandler<StatusNoticeEventArgs>? Notified;

    /// <summary>
    /// How many files the current selection, tones and mode would produce.
    /// </summary>
    /// <remarks>
    /// <see langword="long"/> because <see cref="BatchPlan.Count"/> saturates rather than wrapping:
    /// ten slots of a 995-file library pass <see langword="int"/> easily, and a label reading a
    /// negative number is worse than one reading an implausible one.
    /// </remarks>
    public long PlannedCount
    {
        get
        {
            var planned = BatchPlan.Count(Selections(), SelectedTones());

            if (Mode is not ExportMode.Both)
            {
                return planned;
            }

            // Both writes every combination in both geometries. Saturating rather than doubling
            // blindly, or the widest selections would come back negative.
            return planned > long.MaxValue / 2 ? long.MaxValue : planned * 2;
        }
    }

    /// <summary>
    /// The one recipe the page's composite still stands for, or none while the selection plans
    /// nothing.
    /// </summary>
    /// <remarks>
    /// A property rather than an event payload: the view rebuilds the still off
    /// <see cref="PlannedCount"/>'s change notification, which every axis already raises. It stops
    /// at the recipe because a <c>Microsoft.UI</c> image type here would cost the view model its
    /// testability — see <c>CompositePreview</c>, which does the rendering.
    /// </remarks>
    public Optional<SheetRecipe> PreviewRecipe => ExportPlan.Still(Selections(), SelectedTones());

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var picked = await _picker.PickFolderAsync();

        if (picked.TryGet(out var folder))
        {
            OutputFolder = folder.Value;
        }
    }

    /// <summary>
    /// Ticks the spec-079 art, so the shipped set can be inspected and varied from here.
    /// </summary>
    /// <remarks>
    /// A preset, not the deliverable: the files Corvus consumes are named explicitly by
    /// <see cref="RoostSheets.All"/>, and a stem generated from this selection would not match its
    /// cosmetic registry.
    /// </remarks>
    [RelayCommand]
    private void LoadRoostSelection()
    {
        if (!_catalog.Current.TryGet(out var catalog))
        {
            Notify("No asset catalogue yet. Set the three pack folders in Settings.", StatusLevel.Warning);

            return;
        }

        var preset = RoostSheets.Selection(catalog);

        foreach (var group in Groups)
        {
            group.Apply(ChoicesFor(preset, group.Slot));
        }

        foreach (var tone in Tones)
        {
            tone.IsSelected = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport), IncludeCancelCommand = true)]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        var planned = ExportPlan.Recipes(Selections(), SelectedTones(), Mode);

        if (!planned.TryGet(out var recipes))
        {
            Notify(ExportPlan.Explain(planned.Error), StatusLevel.Warning);

            return;
        }

        if (recipes.Length > WarnThreshold)
        {
            Notify($"{recipes.Length} files planned. This will take a while.", StatusLevel.Warning);
        }

        var output = FullPath.FromPath(OutputFolder);

        IsExporting = true;
        ProgressValue = 0;
        ProgressText = $"0 / {recipes.Length}";

        foreach (var group in Groups)
        {
            group.ClearStatuses();
        }

        // Constructed on the UI thread, so Report marshals back to it. Every status write below
        // therefore happens on the dispatcher without any explicit queueing.
        var progress = new Progress<BakeProgress>(OnBakeProgress);

        try
        {
            var summary = await BatchBaker.RunAsync(
                recipes,
                output,
                progress,
                Environment.ProcessorCount,
                cancellationToken);

            Report(summary);
            WriteManifests(output, recipes);
        }
        finally
        {
            IsExporting = false;

            if (_reloadPending)
            {
                Reload();
            }
        }
    }

    private bool CanExport() =>
        !IsExporting
        && !PacksMissing
        && !string.IsNullOrWhiteSpace(OutputFolder)
        && PlannedCount > 0;

    private void Notify(string message, StatusLevel level) =>
        Notified?.Invoke(this, new StatusNoticeEventArgs(message, level));

    /// <summary>What every slot contributes, or nothing at all until the packs resolve.</summary>
    private ImmutableArray<SlotSelection> Selections() =>
        _catalog.Current.TryGet(out var catalog)
            ? [.. Groups.AsValueEnumerable().Select(group => group.ToSelection(catalog))]
            : [];

    /// <summary>The ticked tones, in the order the page lists them.</summary>
    /// <remarks>
    /// <see cref="ObservableCollection{T}"/> is not one of the types the ZLinq drop-in generator
    /// covers, so the chain is opened explicitly or it would quietly bind to System.Linq.
    /// </remarks>
    private ImmutableArray<SkinRamp> SelectedTones() =>
    [
        .. Tones.AsValueEnumerable()
            .Where(static tone => tone.IsSelected)
            .Select(static tone => tone.Ramp),
    ];

    /// <summary>A preset's choices for one slot, or none when the preset does not fill it.</summary>
    private static ImmutableArray<Optional<AssetPartial>> ChoicesFor(
        ImmutableArray<SlotSelection> preset,
        AssetSlot slot) =>
        preset.AsSpan().FirstOrDefault(selection => selection.Slot == slot)?.Choices ?? [];

    private void Report(BatchSummary summary) => Notify(
        summary.Cancelled
            ? $"Cancelled. {summary.Succeeded} written, {summary.TotalWritten} total."
            : $"{summary.Succeeded} written, {summary.Failed} failed, {summary.TotalWritten} total.",
        summary.Cancelled || summary.Failed is not 0 ? StatusLevel.Warning : StatusLevel.Success);

    /// <summary>
    /// Writes the indexes the run's geometries need, plus the run manifest.
    /// </summary>
    /// <remarks>
    /// Each geometry's index is written only when the run actually produced that geometry, so a
    /// curated-only export does not leave a <c>clips.csv</c> describing files that are not there.
    /// </remarks>
    private void WriteManifests(FullPath output, ImmutableArray<SheetRecipe> recipes)
    {
        if (recipes.AsSpan().Any(static recipe => recipe.Geometry is SheetGeometry.Curated))
        {
            NotifyIfFailed(SheetIndex.FileName, SheetIndex.WriteTo(output));
        }

        if (recipes.AsSpan().Any(static recipe => recipe.Geometry is SheetGeometry.Full))
        {
            NotifyIfFailed(ClipIndex.FileName, ClipIndex.WriteTo(output));
        }

        NotifyIfFailed(
            BatchManifest.FileName,
            BatchManifest.WriteTo(output, BatchManifest.NewRunId(), BatchManifest.RowsFor(recipes)));
    }

    private void NotifyIfFailed(string file, Result<int, BakeFailure> written)
    {
        if (!written.IsSuccessful)
        {
            Notify($"{file} failed: {written.Error}.", StatusLevel.Error);
        }
    }

    /// <summary>
    /// Marks every row the finished sheet was composed from.
    /// </summary>
    /// <remarks>
    /// A stem now names every slot, so the old "row name is a prefix of the sheet name" heuristic
    /// is gone: the stem is split on <c>_</c> and matched whole, or <c>top1</c> would mark
    /// <c>top11</c> too.
    /// </remarks>
    private void OnBakeProgress(BakeProgress report)
    {
        ProgressValue = report.Total is 0 ? 0 : report.Completed * 100.0 / report.Total;
        ProgressText = $"{report.Completed} / {report.Total}";

        var status = report.IsSuccess
            ? report.Written.TryGet(out var size) ? size.ToString(null, CultureInfo.CurrentCulture) : "written"
            : report.Failure.ToString();

        var segments = report.Name.Split('_');

        foreach (var group in Groups)
        {
            group.Mark(segments, status, report.IsSuccess);
        }
    }

    private void Reload()
    {
        // A run survives navigation, so the user can walk off and re-pick a pack path mid-export.
        // Rebuilding the groups under an in-flight run would orphan every Mark() the run still
        // owes and silently reset the selection; the run itself still completes against its
        // snapshot. Deferred, not dropped: ExportAsync's finally replays this once the run ends.
        if (IsExporting)
        {
            _reloadPending = true;

            return;
        }

        _reloadPending = false;

        Groups.Clear();

        if (_catalog.Current.TryGet(out var catalog))
        {
            foreach (var slot in AssetSlots.DrawOrder)
            {
                var bases = catalog.Bases(slot);

                // A slot no pack ships gets no picker at all rather than an empty one — only the
                // core pack ships a shadow, and expansion 2 has no front extra.
                if (!bases.IsEmpty)
                {
                    var group = new SlotGroupViewModel(slot, bases);

                    group.PropertyChanged += OnSelectionChanged;

                    Groups.Add(group);
                }
            }
        }

        OnPropertyChanged(nameof(PacksMissing));
        OnPropertyChanged(nameof(PlannedCount));

        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// The planned count is a live label, so anything that moves an axis has to re-raise it.
    /// Filter text and captions are explicitly not such a change.
    /// </summary>
    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SlotGroupViewModel.SelectedCount)
            or nameof(SlotGroupViewModel.IncludeVariants)
            or nameof(ToneSelectionItem.IsSelected)))
        {
            return;
        }

        OnPropertyChanged(nameof(PlannedCount));

        ExportCommand.NotifyCanExecuteChanged();
    }
}
