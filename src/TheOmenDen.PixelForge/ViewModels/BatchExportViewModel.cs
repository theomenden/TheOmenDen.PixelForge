using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Spritesheets;
using TheOmenDen.PixelForge.Services;

namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>
/// Selects sheets, runs the batch, and reports each result as it lands.
/// </summary>
public sealed partial class BatchExportViewModel : ObservableObject
{
    private readonly SourcePackService _packs;
    private readonly PickerService _picker;
    private bool _reloadPending;

    public BatchExportViewModel(SourcePackService packs, PickerService picker)
    {
        _packs = packs;
        _picker = picker;

        _packs.Changed += (_, _) => Reload();

        Reload();
    }

    public ObservableCollection<SheetSelectionItem> Bodies { get; } = [];

    public ObservableCollection<SheetSelectionItem> Hair { get; } = [];

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
        }
    }

    /// <summary>
    /// The mode the selected index means. <see cref="ExportMode"/>'s members are declared in the
    /// order the page lists them, so the index <em>is</em> the enum value — no table of literals
    /// that has to be kept in step with the XAML by hand.
    /// </summary>
    /// <remarks>
    /// An index outside the enum is the control clearing itself (-1) rather than a choice. An
    /// unchecked cast would make that Both — 79 files — so the fallback is deliberately the
    /// cheapest mode, not the largest.
    /// </remarks>
    public ExportMode Mode => Enum.IsDefined((ExportMode)SelectedModeIndex)
        ? (ExportMode)SelectedModeIndex
        : DefaultMode;

    private const ExportMode DefaultMode = ExportMode.Layered;

    /// <summary>
    /// The tradeoff, stated where the choice is made. Layered keeps hair a separate texture so a
    /// style can be swapped at runtime and the engine keeps control of z-order; flattened is one
    /// texture per pair — fewer draw calls, but the hair is baked in.
    /// </summary>
    public string ModeDescription => Mode switch
    {
        ExportMode.Flattened => FlattenedDescription,
        ExportMode.Both => BothDescription,
        _ => LayeredDescription,
    };

    private const string LayeredDescription =
        "One file per sheet. Hair stays a separate texture, so a style can be swapped at runtime without rebaking.";

    private const string FlattenedDescription =
        "Body and hair composited into one texture per pair. Fewer draw calls, but the hairstyle is baked in.";

    private const string BothDescription =
        "Both: layered sheets for runtime swapping, plus a flattened texture for every body and hair pair.";

    public string OutputFolder
    {
        get => field;
        private set
        {
            field = value;

            OnPropertyChanged();

            ExportCommand.NotifyCanExecuteChanged();
        }
    } = string.Empty;

    public bool PacksMissing => !_packs.Resolved.HasValue;

    public bool IsExporting
    {
        get => field;
        private set
        {
            field = value;

            OnPropertyChanged();

            ExportCommand.NotifyCanExecuteChanged();
        }
    }

    public double ProgressValue
    {
        get => field;
        private set
        {
            field = value;

            OnPropertyChanged();
        }
    }

    public string ProgressText
    {
        get => field;
        private set
        {
            field = value;

            OnPropertyChanged();
        }
    } = string.Empty;

    /// <summary>
    /// Raised for anything worth telling the user. The page feeds these to a
    /// <c>StackedNotificationsBehavior</c>, which queues them — a 79-sheet run can report several
    /// distinct failures, and a single string property would show only the last.
    /// </summary>
    public event EventHandler<StatusNoticeEventArgs>? Notified;

    private void Notify(string message, StatusLevel level) =>
        Notified?.Invoke(this, new StatusNoticeEventArgs(message, level));

    /// <summary>How many files the current selection and mode would produce.</summary>
    public int PlannedCount => Plan().Length;

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var picked = await _picker.PickFolderAsync();

        if (picked.TryGet(out var folder))
        {
            OutputFolder = folder.Value;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport), IncludeCancelCommand = true)]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        var recipes = Plan();

        if (recipes.IsDefaultOrEmpty)
        {
            Notify("Nothing selected.", StatusLevel.Warning);
            return;
        }

        IsExporting = true;
        ProgressValue = 0;
        ProgressText = $"0 / {recipes.Length}";

        ResetStatuses();

        // Constructed on the UI thread, so Report marshals back to it. Every status write below
        // therefore happens on the dispatcher without any explicit queueing.
        var progress = new Progress<BakeProgress>(OnBakeProgress);

        try
        {
            var summary = await BatchBaker.RunAsync(
                recipes,
                FullPath.FromPath(OutputFolder),
                progress,
                Environment.ProcessorCount,
                cancellationToken);

            var index = SheetIndex.WriteTo(FullPath.FromPath(OutputFolder));

            // Separate notices rather than one concatenated string — the behavior stacks them,
            // so a failed manifest stays visible next to an otherwise successful run.
            if (summary.Cancelled)
            {
                Notify(
                    $"Cancelled. {summary.Succeeded} written, {summary.TotalWritten} total.",
                    StatusLevel.Warning);
            }
            else
            {
                Notify(
                    $"{summary.Succeeded} written, {summary.Failed} failed, {summary.TotalWritten} total.",
                    summary.Failed is 0 ? StatusLevel.Success : StatusLevel.Warning);
            }

            if (!index.IsSuccessful)
            {
                Notify($"Manifest failed: {index.Error}.", StatusLevel.Error);
            }
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

    private bool CanExport() => !IsExporting && !PacksMissing && !string.IsNullOrWhiteSpace(OutputFolder);

    private void OnBakeProgress(BakeProgress report)
    {
        ProgressValue = report.Total is 0 ? 0 : report.Completed * 100.0 / report.Total;
        ProgressText = $"{report.Completed} / {report.Total}";

        var status = report.IsSuccess
            ? report.Written.TryGet(out var size) ? size.ToString(null, CultureInfo.CurrentCulture) : "written"
            : report.Failure.ToString();

        // A flattened sheet's name is "body-NN_hair-NN", so a body row matches its own prefix.
        Mark(Bodies, report.Name, status, report.IsSuccess);
        Mark(Hair, report.Name, status, report.IsSuccess);
    }

    private static void Mark(ObservableCollection<SheetSelectionItem> items, string name, string status, bool isSuccess)
    {
        foreach (var item in items)
        {
            if (name == item.Name || name.StartsWith(item.Name + "_", StringComparison.Ordinal) || name.EndsWith("_" + item.Name, StringComparison.Ordinal))
            {
                // Sticky failures: a flattened body/hair row is written by up to nine pairs, so a
                // later success must not paper over an earlier failure on the same row.
                if (!isSuccess || item.Status.Length is 0)
                {
                    item.Status = status;
                }
            }
        }
    }

    private void ResetStatuses()
    {
        foreach (var item in Bodies)
        {
            item.Status = string.Empty;
        }

        foreach (var item in Hair)
        {
            item.Status = string.Empty;
        }
    }

    /// <summary>The recipes the current selection and mode imply.</summary>
    private ImmutableArray<SheetRecipe> Plan()
    {
        var bodies = Selected(Bodies);
        var hair = Selected(Hair);

        return Mode switch
        {
            ExportMode.Layered => [.. bodies, .. hair],
            ExportMode.Flattened => Flatten(bodies, hair),
            _ => [.. bodies, .. hair, .. Flatten(bodies, hair)],
        };
    }

    /// <summary>The cross product of bodies and hair, composited into one sheet each.</summary>
    /// <param name="bodies">The selected body recipes.</param>
    /// <param name="hair">The selected hair recipes.</param>
    /// <returns>One recipe per pair, empty when either side is.</returns>
    /// <remarks>
    /// <para>
    /// A flattened pair is simply both layer lists concatenated, because the skin substitution is
    /// per layer: the body's layers are marked <see cref="AssetLayer.IsSkin"/> and take the tone,
    /// while hair's are not and keep their authored colour. That is what replaced the old
    /// <c>Overlays</c> collection, which had to draw hair after a whole-assembly recolour.
    /// </para>
    /// <para>
    /// Flattening trades runtime flexibility for draw calls: one texture per pair, but the
    /// hairstyle can no longer be swapped without rebaking.
    /// </para>
    /// </remarks>
    private static ImmutableArray<SheetRecipe> Flatten(
        ImmutableArray<SheetRecipe> bodies,
        ImmutableArray<SheetRecipe> hair) =>
    [
        .. bodies.AsSpan().SelectMany(_ => hair, static (body, style) => new SheetRecipe
        {
            Name = $"{body.Name}_{style.Name}",
            Layers = [.. body.Layers, .. style.Layers],
            Tone = body.Tone,
        }),
    ];

    private static ImmutableArray<SheetRecipe> Selected(ObservableCollection<SheetSelectionItem> items)
    {
        var chosen = ImmutableArray.CreateBuilder<SheetRecipe>(items.Count);

        foreach (var item in items)
        {
            if (item.IsSelected)
            {
                chosen.Add(item.Recipe);
            }
        }

        return chosen.ToImmutable();
    }

    private void Reload()
    {
        // A run survives navigation, so the user can walk off and re-pick a pack path mid-export.
        // Rebuilding the grid under an in-flight run would orphan every Mark() the run still owes
        // and silently reset the selection; the run itself still completes against its snapshot.
        // Deferred, not dropped: ExportAsync's finally replays this once the run ends.
        if (IsExporting)
        {
            _reloadPending = true;
            return;
        }

        _reloadPending = false;

        Bodies.Clear();
        Hair.Clear();

        if (_packs.Resolved.TryGet(out var packs))
        {
            foreach (var recipe in RoostSheets.Bodies(packs))
            {
                Bodies.Add(Track(new SheetSelectionItem(recipe)));
            }

            foreach (var recipe in RoostSheets.Hair(packs))
            {
                Hair.Add(Track(new SheetSelectionItem(recipe)));
            }
        }

        OnPropertyChanged(nameof(PacksMissing));
        OnPropertyChanged(nameof(PlannedCount));

        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Selection drives the planned count, so each item's changes are watched.</summary>
    private SheetSelectionItem Track(SheetSelectionItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SheetSelectionItem.IsSelected))
            {
                OnPropertyChanged(nameof(PlannedCount));
            }
        };

        return item;
    }
}
