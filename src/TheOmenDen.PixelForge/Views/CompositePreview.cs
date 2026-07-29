using System.ComponentModel;
using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp.Views.Windows;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.ViewModels;

namespace TheOmenDen.PixelForge.Views;

/// <summary>
/// Keeps one still of the current selection above the slot picker.
/// </summary>
/// <remarks>
/// <para>
/// Its own type rather than more members on <see cref="BatchExportViewModel"/>, for two reasons:
/// the still is a <c>WriteableBitmap</c>, and a view model naming a <c>Microsoft.UI</c> type stops
/// being unit-testable — the same division <see cref="PalettePage"/> already makes, where the view
/// model deals in ramps and recipes and the view converts. The second is size: the view model is
/// close enough to this project's file limit that appending to it is not an option.
/// </para>
/// <para>
/// One still, never the cross product. <see cref="ExportPlan.Still"/> narrows the selection to a
/// single combination before expanding it, so a preview costs one bake however wide the plan is.
/// </para>
/// </remarks>
/// <param name="viewModel">The page's view model; its axes are what the still follows.</param>
/// <param name="image">The control the still is written to.</param>
internal sealed class CompositePreview(BatchExportViewModel viewModel, Image image)
{
    /// <summary>
    /// Nearest-neighbour multiplier. Three rather than the palette editor's four: this sits above a
    /// ten-slot picker that needs the vertical space, and 432x144 still reads at a glance.
    /// </summary>
    private const int PreviewScale = 3;

    /// <summary>
    /// How long the selection must sit still before a bake. Long enough that ticking down a slot
    /// list bakes once at the end rather than once per row.
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(350);

    /// <summary>
    /// The debounce timer. Belongs to the thread that constructed this, which is why the whole
    /// type is created from the page rather than resolved from the container.
    /// </summary>
    private readonly DispatcherQueueTimer _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();

    /// <summary>Starts following the selection and schedules a first still.</summary>
    /// <remarks>
    /// Unsubscribes first: <c>Loaded</c> can re-fire on one page instance, and the view model is a
    /// singleton that outlives it.
    /// </remarks>
    public void Start()
    {
        // Sized to what Render actually produces, so the row keeps its height while a plan is
        // half-built and the picker below does not jump as stills come and go. Derived rather
        // than written into the XAML, or the two would drift the moment PreviewScale changed.
        image.Width = PalettePreview.IdleRowWidth * PreviewScale;
        image.Height = PalettePreview.IdleRowHeight * PreviewScale;

        viewModel.PropertyChanged -= OnViewModelChanged;
        viewModel.PropertyChanged += OnViewModelChanged;

        Schedule();
    }

    /// <summary>Stops following the selection and drops any bake still pending.</summary>
    public void Stop()
    {
        viewModel.PropertyChanged -= OnViewModelChanged;

        _timer.Stop();
    }

    /// <summary>
    /// Reschedules the still whenever an axis moves.
    /// </summary>
    /// <remarks>
    /// <see cref="BatchExportViewModel.PlannedCount"/> is re-raised by everything that changes what
    /// would be baked — a ticked base, the variants toggle, a tone, a catalogue reload — so it is
    /// the one signal worth watching rather than five subscriptions that could fall out of step
    /// with it. <see cref="BatchExportViewModel.IsExporting"/> is the trailing edge: a run's end
    /// has to bring the still back, since <see cref="Render"/> declines to bake during one.
    /// </remarks>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BatchExportViewModel.PlannedCount)
            or nameof(BatchExportViewModel.IsExporting))
        {
            Schedule();
        }
    }

    /// <summary>
    /// Coalesces a burst of changes into one bake.
    /// </summary>
    /// <remarks>
    /// <c>Debounce</c> is CommunityToolkit.WinUI's extension over
    /// <see cref="DispatcherQueueTimer"/> rather than a hand-rolled start/stop dance; it is
    /// documented as the way to drive the timer, and it already handles restarting an armed one.
    /// </remarks>
    private void Schedule() => _timer.Debounce(Render, SettleDelay);

    /// <summary>
    /// Bakes the still, or clears it.
    /// </summary>
    /// <remarks>
    /// Skipped outright while a run is in flight. The run is already decoding every partial it owns
    /// across every core, and a preview competing for the same files buys nothing the progress bar
    /// does not already say.
    /// </remarks>
    private void Render()
    {
        if (viewModel.IsExporting)
        {
            return;
        }

        image.Source = Compose();
    }

    /// <summary>
    /// The still for the current selection.
    /// </summary>
    /// <returns>
    /// The composed idle row, or <see langword="null"/> when there is nothing to show.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Every failure is silent on purpose. A plan is half-built for most of the time anyone spends
    /// on this page, so a preview that raised a notice per rejected plan or missing file would bury
    /// the run's own messages under its noise.
    /// </para>
    /// <para>
    /// The <see cref="PalettePreview"/> is disposed per render rather than cached the way the
    /// palette editor caches its own. There, only the ramp changes underneath one fixed recipe;
    /// here every rebuild is a different set of layers, so a cached sheet would always be the
    /// wrong one.
    /// </para>
    /// <para>
    /// The bake runs on the UI thread — around ten decodes and a curate, so a settled tick costs a
    /// frame or two, which the debounce above already bounds to one per burst. Moving it to a
    /// background task would need a generation counter to drop stale results, and is only worth it
    /// if a source partial ever grows.
    /// </para>
    /// </remarks>
    private WriteableBitmap? Compose()
    {
        if (!viewModel.PreviewRecipe.TryGet(out var recipe))
        {
            return null;
        }

        var created = PalettePreview.Create(recipe);

        if (!created.TryGet(out var preview))
        {
            return null;
        }

        using (preview)
        {
            var rendered = preview.RenderIdleRow(recipe.Tone.Or(SkinRamps.Source), PreviewScale);

            if (!rendered.TryGet(out var bitmap))
            {
                return null;
            }

            using (bitmap)
            {
                // Extension from SkiaSharp.Views.WinUI (namespace SkiaSharp.Views.Windows) — the
                // same first-party bridge the palette page uses, not a second conversion.
                return bitmap.ToWriteableBitmap();
            }
        }
    }
}
