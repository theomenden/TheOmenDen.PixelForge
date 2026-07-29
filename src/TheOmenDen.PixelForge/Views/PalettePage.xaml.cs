using System.Collections.Immutable;
using System.ComponentModel;
using CommunityToolkit.WinUI.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Services;
using TheOmenDen.PixelForge.ViewModels;

namespace TheOmenDen.PixelForge.Views;

/// <summary>
/// Palette editor. All Skia and <c>Windows.UI.Color</c> conversion lives here rather than in the
/// view model, which deals only in <see cref="SKColor"/> — that is what keeps it free of UI types.
/// </summary>
public sealed partial class PalettePage : Page
{
    /// <summary>Shown whenever there is nothing to render — no ramp, or no packs configured.</summary>
    private const string NoPreviewHint = "Set the source pack folders in Settings to see a live preview.";

    /// <summary>
    /// Nearest-neighbour multiplier for the preview strip: 48px cells are unreadable at 1:1, and
    /// 4x keeps the three faces (576x192) inside the editor column without a scrollbar.
    /// </summary>
    private const int PreviewScale = 4;

    /// <summary>How long a non-error notice stays up before the behavior dismisses it.</summary>
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(4);

    private PalettePreview? _preview;

    public PalettePage()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public PaletteViewModel ViewModel { get; } = App.Services.GetRequiredService<PaletteViewModel>();

    /// <summary>
    /// Swatch for one step of a ramp. Constant index arguments are legal in x:Bind. Takes the
    /// step array rather than the <see cref="SkinRamp"/> itself — x:Bind function calls resolve
    /// a property path, and this SDK's XAML compiler crashes on the "current object" (".") form.
    /// </summary>
    public static Brush StepBrush(ImmutableArray<SKColor> steps, int index) =>
        steps.IsDefaultOrEmpty || index >= steps.Length
            ? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            : new SolidColorBrush(Windows.UI.Color.FromArgb(
                steps[index].Alpha, steps[index].Red, steps[index].Green, steps[index].Blue));

    /// <summary>
    /// Names each row for its ramp. Without this a row announces the record's generated
    /// <c>ToString</c> — "SkinRamp { Name = Tone 1, Steps = System.Collections.Immutable…" —
    /// since UIA falls back to the data item when the container has no name of its own, and the
    /// swatch strip carries no text to announce instead. A <c>Style</c> setter cannot fix it:
    /// WinUI does not support <c>{Binding}</c> in <c>Setter.Value</c>, so it binds to nothing
    /// and silently changes nothing.
    /// </summary>
    // CA1822: a XAML event handler has to be an instance method — the generated Connect() code
    // calls it through `this`, so taking the analyzer's advice fails the build with CS0176.
#pragma warning disable CA1822
    private void OnRampContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is SkinRamp ramp)
        {
            AutomationProperties.SetName(args.ItemContainer, ramp.Name);
        }
    }
#pragma warning restore CA1822

    /// <summary>
    /// Maps the view model's UI-free <see cref="StatusLevel"/> onto the toolkit's notification
    /// queue. The behavior handles stacking and timed dismissal.
    /// </summary>
    private void OnNotified(object? sender, StatusNoticeEventArgs notice) =>
        StatusNotifications.Show(new Notification
        {
            Message = notice.Message,
            Severity = notice.Level switch
            {
                StatusLevel.Success => InfoBarSeverity.Success,
                StatusLevel.Warning => InfoBarSeverity.Warning,
                StatusLevel.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational,
            },
            Duration = notice.Level is StatusLevel.Error ? null : NoticeDuration,
        });

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded can re-fire on one instance (e.g. NavigationCacheMode.Enabled), so unsubscribe
        // before subscribing rather than assuming this only ever runs once.
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ViewModel.Notified -= OnNotified;
        ViewModel.Notified += OnNotified;

        // PaletteViewModel reads SourcePackService.Resolved fresh but never subscribes to its
        // Changed event — it is a singleton, so if the packs are (re)configured while this page
        // is alive, nothing would otherwise trigger a re-render.
        var packs = App.Services.GetRequiredService<SourcePackService>();
        packs.Changed -= OnPacksChanged;
        packs.Changed += OnPacksChanged;

        RenderPreview();
    }

    private void OnPacksChanged(object? sender, EventArgs e)
    {
        // The cached preview was baked from the old packs — reconfiguring means the cached
        // sheet is now wrong, not just stale, so it must be rebuilt, not just re-rendered.
        _preview?.Dispose();
        _preview = null;

        RenderPreview();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Notified -= OnNotified;

        App.Services.GetRequiredService<SourcePackService>().Changed -= OnPacksChanged;

        _preview?.Dispose();
        _preview = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PaletteViewModel.PreviewRamp))
        {
            RenderPreview();
        }
    }

    /// <summary>
    /// Re-renders the recoloured sprite. The <see cref="PalettePreview"/> is built once per
    /// session — it caches the curated, un-recoloured sheet, so a colour change costs only the
    /// substitution and the upscale.
    /// </summary>
    private void RenderPreview()
    {
        if (ViewModel.PreviewRamp is not { } ramp)
        {
            PreviewHint.Text = NoPreviewHint;
            ShowHint(visible: true);
            return;
        }

        if (_preview is null)
        {
            if (!ViewModel.PreviewRecipe.TryGet(out var recipe))
            {
                PreviewHint.Text = NoPreviewHint;
                ShowHint(visible: true);
                return;
            }

            var created = PalettePreview.Create(recipe);

            if (!created.TryGet(out _preview))
            {
                PreviewHint.Text = $"Preview unavailable: {created.Error}.";
                ShowHint(visible: true);
                return;
            }
        }

        var rendered = _preview.RenderIdleRow(ramp, PreviewScale);

        if (!rendered.TryGet(out var bitmap))
        {
            PreviewHint.Text = $"Preview unavailable: {rendered.Error}.";
            ShowHint(visible: true);
            return;
        }

        using (bitmap)
        {
            // Extension from SkiaSharp.Views.WinUI (namespace SkiaSharp.Views.Windows).
            // First-party bridge — no hand-rolled COM interop.
            PreviewImage.Source = bitmap.ToWriteableBitmap();
        }

        ShowHint(visible: false);
    }

    private void ShowHint(bool visible)
    {
        PreviewHint.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PreviewImage.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }
}
