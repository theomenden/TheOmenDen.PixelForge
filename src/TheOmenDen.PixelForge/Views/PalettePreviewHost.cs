using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp.Views.Windows;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Services;
using TheOmenDen.PixelForge.ViewModels;

namespace TheOmenDen.PixelForge.Views;

/// <summary>
/// Keeps the recoloured sprite strip beside the ramp editor in step with the selected ramp.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="CompositePreview"/>, and its own type for the same two reasons:
/// the still is a <c>WriteableBitmap</c>, so a view model naming it stops being unit-testable, and
/// the page it came out of was carrying rendering, hint toggling and two event subscriptions on
/// top of its markup duties. Both pages now follow one rule — the view owns markup, <c>x:Bind</c>
/// helpers and automation ids; preview rendering is a type like this one; everything else is the
/// view model.
/// </para>
/// <para>
/// Nothing here is hand-rolled: <see cref="PalettePreview"/> does the raster work in Core, and the
/// bitmap crossing into XAML goes through <c>ToWriteableBitmap</c> from
/// <c>SkiaSharp.Views.WinUI</c> — a first-party bridge, never hand-written COM interop.
/// </para>
/// <para>
/// Deliberately not debounced, unlike <see cref="CompositePreview"/>. That one coalesces a run of
/// ticks down a ten-slot list; this one re-renders on a ramp selection, which is a single discrete
/// change, and <see cref="PalettePreview"/> caches the curated sheet so a recolour costs only the
/// substitution and the upscale.
/// </para>
/// </remarks>
/// <param name="viewModel">The page's view model; its selected ramp is what the strip follows.</param>
/// <param name="image">The control the strip is written to.</param>
/// <param name="hint">Shown in the image's place whenever there is nothing to render.</param>
internal sealed class PalettePreviewHost(PaletteViewModel viewModel, Image image, TextBlock hint)
{
    /// <summary>Shown whenever there is nothing to render — no ramp, or no packs configured.</summary>
    private const string NoPreviewHint = "Set the source pack folders in Settings to see a live preview.";

    /// <summary>
    /// Nearest-neighbour multiplier for the preview strip: 48px cells are unreadable at 1:1, and
    /// 4x keeps the three faces (576x192) inside the editor column without a scrollbar.
    /// </summary>
    private const int PreviewScale = 4;

    /// <summary>The curated, un-recoloured sheet, built once and reused for every ramp.</summary>
    private PalettePreview? _preview;

    /// <summary>Subscribes and draws the first strip.</summary>
    /// <remarks>
    /// Unsubscribes first: <c>Loaded</c> can re-fire on one instance under
    /// <c>NavigationCacheMode.Enabled</c>, so this cannot assume it runs once.
    /// </remarks>
    public void Start()
    {
        viewModel.PropertyChanged -= OnViewModelChanged;
        viewModel.PropertyChanged += OnViewModelChanged;

        // PaletteViewModel reads SourcePackService.Resolved fresh but never subscribes to its
        // Changed event — it is a singleton, so if the packs are (re)configured while the page is
        // alive, nothing would otherwise trigger a re-render.
        var packs = App.Services.GetRequiredService<SourcePackService>();

        packs.Changed -= OnPacksChanged;
        packs.Changed += OnPacksChanged;

        Render();
    }

    /// <summary>Unsubscribes and releases the cached sheet.</summary>
    public void Stop()
    {
        viewModel.PropertyChanged -= OnViewModelChanged;

        App.Services.GetRequiredService<SourcePackService>().Changed -= OnPacksChanged;

        _preview?.Dispose();
        _preview = null;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PaletteViewModel.PreviewRamp))
        {
            Render();
        }
    }

    /// <summary>Rebuilds from scratch when the pack roots change.</summary>
    /// <remarks>
    /// The cached sheet was baked from the old packs, so reconfiguring makes it wrong rather than
    /// merely stale — it has to be rebuilt, not just redrawn.
    /// </remarks>
    private void OnPacksChanged(object? sender, EventArgs e)
    {
        _preview?.Dispose();
        _preview = null;

        Render();
    }

    private void Render()
    {
        if (viewModel.PreviewRamp is not { } ramp)
        {
            ShowHint(NoPreviewHint);

            return;
        }

        if (_preview is null && !TryCreate())
        {
            return;
        }

        var rendered = _preview!.RenderIdleRow(ramp, PreviewScale);

        if (!rendered.TryGet(out var bitmap))
        {
            ShowHint($"Preview unavailable: {rendered.Error}.");

            return;
        }

        using (bitmap)
        {
            image.Source = bitmap.ToWriteableBitmap();
        }

        hint.Visibility = Visibility.Collapsed;
        image.Visibility = Visibility.Visible;
    }

    /// <summary>Builds the cached sheet, reporting into the hint when it cannot be built.</summary>
    private bool TryCreate()
    {
        if (!viewModel.PreviewRecipe.TryGet(out var recipe))
        {
            ShowHint(NoPreviewHint);

            return false;
        }

        var created = PalettePreview.Create(recipe);

        if (created.TryGet(out _preview))
        {
            return true;
        }

        ShowHint($"Preview unavailable: {created.Error}.");

        return false;
    }

    private void ShowHint(string text)
    {
        hint.Text = text;
        hint.Visibility = Visibility.Visible;
        image.Visibility = Visibility.Collapsed;
    }
}
