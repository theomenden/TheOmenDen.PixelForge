using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Behaviors;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.ViewModels;

namespace TheOmenDen.PixelForge.Views;

/// <summary>
/// Batch sheet export. The view model is UI-free, so the mapping from its
/// <see cref="StatusLevel"/> onto InfoBar severities lives here.
/// </summary>
public sealed partial class PipelinePage : Page
{
    /// <summary>
    /// How long a notice stays up. Longer than the palette page's: a run can post several at
    /// once, and the user may be watching the progress bar rather than the bar underneath it.
    /// </summary>
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Gates <see cref="OnExportModeChanged"/> until the control has been told the view model's
    /// mode, so the Segmented's own initialisation never counts as a user choice.
    /// </summary>
    private bool _modeReady;

    /// <summary>
    /// The still above the picker. Owned by the page rather than the view model, because it deals
    /// in image types the view model deliberately cannot name.
    /// </summary>
    private readonly CompositePreview _preview;

    public PipelinePage()
    {
        InitializeComponent();

        _preview = new(ViewModel, CompositePreviewImage);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public BatchExportViewModel ViewModel { get; } = App.Services.GetRequiredService<BatchExportViewModel>();

    /// <summary>
    /// How many files the current selection and mode would write.
    /// </summary>
    /// <param name="count">
    /// <see langword="long"/>, matching <see cref="BatchExportViewModel.PlannedCount"/>: a cross
    /// product over ten slots passes <see langword="int"/> without much effort.
    /// </param>
    /// <returns>The count with its unit, singular for exactly one.</returns>
    public static string PlannedLabel(long count) => count is 1 ? "1 file" : $"{count} files";

    /// <summary>
    /// What the run consists of, rather than one total.
    /// </summary>
    /// <param name="counts">The breakdown from the view model.</param>
    /// <returns>A sentence naming the characters, skins and equipment a run would write.</returns>
    /// <remarks>
    /// <para>
    /// A static function over the whole record rather than a string on the view model, so the
    /// formatting stays in the view and the counting stays in Core — the same split
    /// <see cref="PlannedLabel"/> already keeps, and the reason there is no converter.
    /// </para>
    /// <para>
    /// The number used to be a warning: five figures meant "you probably did not mean this". A
    /// layer run is double digits, so it describes instead, and the breakdown is what makes the
    /// remaining multiplier — the skin tones — visible while the user is still choosing.
    /// </para>
    /// </remarks>
    public static string PlannedBreakdown(PlannedCounts counts)
    {
        if (counts.Sheets is 0)
        {
            return "Nothing to export yet";
        }

        var parts = new List<string>(2);

        if (counts.Heroes > 0 && counts.Tones > 0)
        {
            parts.Add($"{Plural(counts.Heroes, "character")} x {Plural(counts.Tones, "skin tone")}");
        }

        if (counts.Attachments > 0)
        {
            parts.Add(Plural(counts.Attachments, "equipment layer"));
        }

        return $"{string.Join(" + ", parts)} = {PlannedLabel(counts.Sheets)}";
    }

    private static string Plural(long count, string noun) =>
        count is 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>
    /// The automation id of a per-slot control, unique across the ten groups that share the page.
    /// </summary>
    /// <param name="prefix">The control's role, e.g. <c>SlotFilter</c> or <c>SlotVariants</c>.</param>
    /// <param name="slot">The group's slot, which is what makes the id unique.</param>
    /// <returns>The prefix and the slot joined by <c>_</c>, e.g. <c>SlotFilter_Top</c>.</returns>
    /// <remarks>
    /// A function rather than two more properties on <see cref="SlotGroupViewModel"/>: an
    /// automation id is a fact about the view, and the view model already carries the one id
    /// (<see cref="SlotGroupViewModel.ExpanderAutomationId"/>) that a caller outside the page
    /// needs.
    /// </remarks>
    public static string SlotAutomationId(string prefix, AssetSlot slot) => $"{prefix}_{slot}";

    /// <summary>
    /// What a screen reader announces for a per-slot control.
    /// </summary>
    /// <param name="label">What the control does, e.g. <c>Filter</c>.</param>
    /// <param name="header">The group's header, so ten identical controls read apart.</param>
    /// <returns>The two joined by a space, e.g. <c>Filter Back hair</c>.</returns>
    public static string SlotLabel(string label, string header) => $"{label} {header}";

    /// <summary>
    /// The automation id of a tone swatch.
    /// </summary>
    /// <param name="name">The ramp's name, which is already unique within the tone list.</param>
    /// <returns>The name prefixed with <c>Tone_</c>.</returns>
    /// <remarks>
    /// The name is used verbatim, spaces and brackets included — an automation id is an opaque
    /// string to UIA, and slugging it here would only make the id harder to predict from the
    /// label beside it. The stem's tone segment is slugged, but that is a file name, which this
    /// is not.
    /// </remarks>
    public static string ToneAutomationId(string name) => $"Tone_{name}";

    /// <summary>
    /// The swatch colour for a tone: the ramp's mid step, forced opaque.
    /// </summary>
    /// <param name="ramp">The ramp the swatch stands for.</param>
    /// <returns>A brush over <see cref="SkinRamp.BaseTone"/>.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="SKColor"/> is a SkiaSharp value and XAML cannot bind one to a
    /// <see cref="Brush"/>. The conversion lives here rather than as a second property on
    /// <see cref="ToneSelectionItem"/> because that is what keeps the view models free of
    /// <c>Microsoft.UI.*</c>, and it is an <c>x:Bind</c> function rather than an
    /// <c>IValueConverter</c> because a function is compiled and type-checked.
    /// </para>
    /// <para>
    /// It takes the ramp rather than the row so the call site can bind a property path. An
    /// <c>x:Bind</c> function taking the row itself needs the "current object" (<c>.</c>) form,
    /// which crashes this SDK's XAML compiler — see <see cref="PalettePage"/>'s <c>StepBrush</c>.
    /// </para>
    /// </remarks>
    public static Brush ToneBrush(SkinRamp ramp) =>
        ramp is null
            ? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            : new SolidColorBrush(Windows.UI.Color.FromArgb(
                0xFF, ramp.BaseTone.Red, ramp.BaseTone.Green, ramp.BaseTone.Blue));

    /// <summary>
    /// Gives a realised slot expander's header button the slot's automation id.
    /// </summary>
    /// <param name="sender">The repeater the ten groups are laid out by.</param>
    /// <param name="args">Carries the element just realised, or reused from the recycle pool.</param>
    /// <remarks>
    /// <para>
    /// <see cref="SettingsExpander"/> reports itself to UIA as a <c>Group</c> supporting no
    /// pattern; the element carrying <c>ExpandCollapse</c> is the <see cref="Expander"/> inside
    /// its template. An id set in XAML therefore lands on something that cannot be invoked, and
    /// the picker's ten groups could not be opened by automation or by anything else driving UIA.
    /// </para>
    /// <para>
    /// Re-stamped on every preparation rather than once on <c>Loaded</c>, because
    /// <see cref="ItemsRepeater"/> recycles: a scrolled-away expander comes back holding a
    /// different <see cref="SlotGroupViewModel"/>, and a one-shot stamp would leave it answering
    /// to the previous slot's id.
    /// </para>
    /// </remarks>
    private void OnSlotElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        // The index, not the element's DataContext: a DataTemplate using x:Bind is driven through
        // ProcessBindings, and ItemsRepeater leaves DataContext null for one — reading it here
        // silently stamps nothing.
        if (args.Element is not SettingsExpander expander || args.Index >= ViewModel.Groups.Count)
        {
            return;
        }

        var id = ViewModel.Groups[args.Index].ExpanderAutomationId;

        if (TryStampSlotId(expander, id))
        {
            return;
        }

        // First realisation: the control's template has not been applied, so the Expander does
        // not exist yet to stamp. Loaded is the first moment it does. Tag carries the id across
        // because the handler is unsubscribed by reference and so cannot be a closure.
        expander.Tag = id;
        expander.Loaded -= OnSlotExpanderLoaded;
        expander.Loaded += OnSlotExpanderLoaded;
    }

    private void OnSlotExpanderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is SettingsExpander expander && expander.Tag is string id)
        {
            expander.Loaded -= OnSlotExpanderLoaded;

            TryStampSlotId(expander, id);
        }
    }

    /// <summary>
    /// Copies a slot's id onto the expander that actually answers to it.
    /// </summary>
    /// <param name="expander">The realised expander.</param>
    /// <param name="id">The slot's <see cref="SlotGroupViewModel.ExpanderAutomationId"/>.</param>
    /// <returns>
    /// <see langword="true"/> once the inner control exists and carries the id;
    /// <see langword="false"/> while the template is still unapplied.
    /// </returns>
    private static bool TryStampSlotId(SettingsExpander expander, string id)
    {
        // The Expander inside the template, not its header ToggleButton: ExpanderAutomationPeer
        // is what reports ExpandCollapse to UIA, and it hangs off the Expander itself.
        // FindDescendant is CommunityToolkit.WinUI.Extensions' visual-tree walk rather than a
        // hand-rolled VisualTreeHelper recursion.
        if (expander.FindDescendant<Expander>() is not { } header)
        {
            return false;
        }

        AutomationProperties.SetAutomationId(header, id);

        return true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded can re-fire on one instance, so unsubscribe before subscribing rather than
        // assuming this only ever runs once. The view model is a singleton and outlives the page.
        ViewModel.Notified -= OnNotified;
        ViewModel.Notified += OnNotified;

        // The view model owns the mode; the control is told it once the template has been
        // applied. Only then does a selection change mean the user picked something.
        _modeReady = false;
        ExportModeSegmented.SelectedIndex = ViewModel.SelectedModeIndex;
        _modeReady = true;

        _preview.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _modeReady = false;

        ViewModel.Notified -= OnNotified;

        _preview.Stop();
    }

    /// <summary>
    /// A selection of -1 is the control clearing itself while it re-realises its items, never a
    /// user choice — taking it would reset the mode to Curated behind the user's back.
    /// </summary>
    private void OnExportModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_modeReady && ExportModeSegmented.SelectedIndex >= 0)
        {
            ViewModel.SelectedModeIndex = ExportModeSegmented.SelectedIndex;
        }
    }

    /// <summary>
    /// Navigation is platform glue, so it stays in code-behind. Going through the shell rather
    /// than this page's Frame keeps the nav pane's selection honest.
    /// </summary>
    private void OnGoToSettings(object sender, RoutedEventArgs e) => MainWindow.Shell?.NavigateToSettings();

    /// <summary>
    /// Maps the view model's UI-free <see cref="StatusLevel"/> onto the toolkit's notification
    /// queue. Errors have no Duration, so they stay until dismissed.
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
}
