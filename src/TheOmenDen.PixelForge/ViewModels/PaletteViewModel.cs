using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Collections;
using DotNext;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Services;

namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>
/// The palette editor. Built-ins are shown but not editable — selecting one offers Duplicate.
/// </summary>
public sealed partial class PaletteViewModel : ObservableObject
{
    private readonly RampService _ramps;
    private readonly PickerService _picker;
    private readonly SourcePackService _packs;

    public PaletteViewModel(RampService ramps, PickerService picker, SourcePackService packs)
    {
        _ramps = ramps;
        _picker = picker;
        _packs = packs;

        // AdvancedCollectionView observes the service's single collection directly, so adding or
        // deleting a ramp updates the list without a clear-and-rebuild — and therefore without
        // the selection-restore dance a rebuild forces.
        RampView = new AdvancedCollectionView(_ramps.Ramps, isLiveShaping: true);
        RampView.SortDescriptions.Add(new SortDescription(nameof(SkinRamp.Name), SortDirection.Ascending));

        SelectedRamp = _ramps.Ramps.Count > 0 ? _ramps.Ramps[0] : null;
    }

    /// <summary>Sorted, live-shaped view over every ramp. Bound directly as the ListView source.</summary>
    public AdvancedCollectionView RampView { get; }

    public ObservableCollection<RampStepViewModel> Steps { get; } = [];

    public SkinRamp? SelectedRamp
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
            {
                return;
            }

            field = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(IsBuiltInSelected));

            LoadSteps(value);

            EditedName = value?.Name ?? string.Empty;

            DeleteRampCommand.NotifyCanExecuteChanged();
            SaveRampCommand.NotifyCanExecuteChanged();
            DuplicateRampCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Editable name. Renaming a custom ramp is a Save of a differently-named ramp.</summary>
    public string EditedName
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

            SaveRampCommand.NotifyCanExecuteChanged();
        }
    } = string.Empty;

    public bool IsBuiltInSelected => SelectedRamp is not null && SkinRamps.IsBuiltIn(SelectedRamp);

    public bool IsEditable => SelectedRamp is not null && !IsBuiltInSelected;

    /// <summary>
    /// What the preview renders: the selected ramp with the current, possibly unsaved, step
    /// edits applied. Rebuilt from <see cref="Steps"/> so dragging the picker updates the sprite
    /// before anything is committed.
    /// </summary>
    public SkinRamp? PreviewRamp
    {
        get
        {
            if (SelectedRamp is null || Steps.Count != SkinRamps.StepCount)
            {
                return SelectedRamp;
            }

            var steps = ImmutableArray.CreateBuilder<SKColor>(SkinRamps.StepCount);

            foreach (var step in Steps)
            {
                steps.Add(step.Color);
            }

            return SelectedRamp with { Steps = steps.ToImmutable() };
        }
    }

    /// <summary>
    /// The body recipe the preview bakes from. Absent until the packs are configured, which is
    /// what the page uses to show its hint instead of an empty frame.
    /// </summary>
    public Optional<SheetRecipe> PreviewRecipe
    {
        get
        {
            if (!_packs.Resolved.TryGet(out var packs))
            {
                return Optional<SheetRecipe>.None;
            }

            var bodies = RoostSheets.Bodies(packs);

            return bodies.Length > 0 ? bodies[0] : Optional<SheetRecipe>.None;
        }
    }

    /// <summary>
    /// Raised for anything worth telling the user. The page feeds these to a
    /// <c>StackedNotificationsBehavior</c>, which queues and auto-dismisses them — so a run that
    /// produces several messages shows all of them instead of clobbering one string.
    /// </summary>
    public event EventHandler<StatusNotice>? Notified;

    private void Notify(string message, StatusLevel level) =>
        Notified?.Invoke(this, new StatusNotice(message, level));

    [RelayCommand]
    private void NewRamp()
    {
        var ramp = new SkinRamp
        {
            Name = UniqueName("New Ramp"),
            IsHuman = false,
            Steps = SkinRamps.Source.Steps,
        };

        Apply(_ramps.Add(ramp), $"Created {ramp.Name}", () => SelectByName(ramp.Name));
    }

    [RelayCommand(CanExecute = nameof(CanDuplicate))]
    private void DuplicateRamp()
    {
        if (SelectedRamp is null)
        {
            return;
        }

        var copy = SelectedRamp with { Name = UniqueName($"{SelectedRamp.Name} copy") };

        Apply(_ramps.Add(copy), $"Duplicated to {copy.Name}", () => SelectByName(copy.Name));
    }

    private bool CanDuplicate() => SelectedRamp is not null;

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void DeleteRamp()
    {
        if (SelectedRamp is null)
        {
            return;
        }

        var name = SelectedRamp.Name;

        Apply(_ramps.Remove(name), $"Deleted {name}", () => SelectedRamp = _ramps.Ramps.Count > 0 ? _ramps.Ramps[0] : null);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveRamp()
    {
        if (SelectedRamp is null)
        {
            return;
        }

        var steps = ImmutableArray.CreateBuilder<SKColor>(SkinRamps.StepCount);

        foreach (var step in Steps)
        {
            steps.Add(step.Color);
        }

        var edited = SelectedRamp with
        {
            Name = EditedName.Trim(),
            Steps = steps.ToImmutable(),
        };

        Apply(_ramps.Replace(SelectedRamp.Name, edited), $"Saved {edited.Name}", () => SelectByName(edited.Name));
    }

    private bool CanSave() => IsEditable && !string.IsNullOrWhiteSpace(EditedName);

    private bool CanEditSelection() => IsEditable;

    [RelayCommand]
    private async Task ImportAsync()
    {
        var picked = await _picker.PickOpenFileAsync(".csv");

        if (!picked.TryGet(out var file))
        {
            return;
        }

        var imported = _ramps.Import(file);

        if (imported.TryGet(out var count))
        {
            Notify($"Imported {count} ramp(s).", StatusLevel.Success);
        }
        else
        {
            Notify($"Import failed: {imported.Error}.", StatusLevel.Error);
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var picked = await _picker.PickSaveFileAsync("ramps", ".csv", "Palette CSV");

        if (!picked.TryGet(out var file))
        {
            return;
        }

        var exported = _ramps.Export(file);

        if (exported.TryGet(out var count))
        {
            Notify($"Exported {count} ramp(s).", StatusLevel.Success);
        }
        else
        {
            Notify($"Export failed: {exported.Error}.", StatusLevel.Error);
        }
    }

    private void Apply(Result<int, RampFailure> result, string success, Action onSuccess)
    {
        if (result.IsSuccessful)
        {
            Notify(success, StatusLevel.Success);
            onSuccess();
        }
        else
        {
            Notify($"Failed: {result.Error}.", StatusLevel.Error);
        }
    }

    private void SelectByName(string name)
    {
        foreach (var ramp in _ramps.Ramps)
        {
            if (string.Equals(ramp.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                SelectedRamp = ramp;
                return;
            }
        }
    }

    private string UniqueName(string proposed)
    {
        var candidate = proposed;
        var suffix = 2;

        while (Exists(candidate))
        {
            candidate = $"{proposed} {suffix++}";
        }

        return candidate;
    }

    private bool Exists(string name)
    {
        foreach (var ramp in _ramps.Ramps)
        {
            if (string.Equals(ramp.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void LoadSteps(SkinRamp? ramp)
    {
        foreach (var step in Steps)
        {
            step.Changed -= OnStepChanged;
        }

        Steps.Clear();

        if (ramp is not null)
        {
            for (var i = 0; i < ramp.Steps.Length; i++)
            {
                var step = new RampStepViewModel(i, ramp.Steps[i]);

                step.Changed += OnStepChanged;

                Steps.Add(step);
            }
        }

        OnPropertyChanged(nameof(PreviewRamp));
    }

    private void OnStepChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(PreviewRamp));
}
