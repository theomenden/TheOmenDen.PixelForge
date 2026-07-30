using System.Collections.Immutable;
using CommunityToolkit.WinUI.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.ViewModels;

namespace TheOmenDen.PixelForge.Views;

/// <summary>
/// Palette editor. All Skia and <c>Windows.UI.Color</c> conversion lives here rather than in the
/// view model, which deals only in <see cref="SKColor"/> — that is what keeps it free of UI types.
/// </summary>
public sealed partial class PalettePage : Page
{
    /// <summary>How long a non-error notice stays up before the behavior dismisses it.</summary>
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Owns the recoloured strip, mirroring how <see cref="PipelinePage"/> owns its
    /// <see cref="CompositePreview"/> — rendering is not a page's job.
    /// </summary>
    private readonly PalettePreviewHost _preview;

    public PalettePage()
    {
        InitializeComponent();

        _preview = new(ViewModel, PreviewImage, PreviewHint);

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
        ViewModel.Notified -= OnNotified;
        ViewModel.Notified += OnNotified;

        _preview.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Notified -= OnNotified;

        _preview.Stop();
    }
}
