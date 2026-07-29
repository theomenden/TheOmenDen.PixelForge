using CommunityToolkit.WinUI.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOmenDen.PixelForge.ViewModels;

namespace TheOmenDen.PixelForge.Views;

/// <summary>
/// Batch sheet export. The view model is UI-free, so the mapping from its
/// <see cref="StatusLevel"/> onto InfoBar severities lives here.
/// </summary>
public sealed partial class PipelinePage : Page
{
    /// <summary>
    /// Gates <see cref="OnExportModeChanged"/> until the control has been told the view model's
    /// mode, so the Segmented's own initialisation never counts as a user choice.
    /// </summary>
    private bool _modeReady;

    public PipelinePage()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public BatchExportViewModel ViewModel { get; } = App.Services.GetRequiredService<BatchExportViewModel>();

    /// <summary>How many files the current selection and mode would write.</summary>
    public static string PlannedLabel(int count) => count is 1 ? "1 file" : $"{count} files";

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
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _modeReady = false;

        ViewModel.Notified -= OnNotified;
    }

    /// <summary>
    /// A selection of -1 is the control clearing itself while it re-realises its items, never a
    /// user choice — taking it would reset the mode to Layered behind the user's back.
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
    private void OnNotified(object? sender, StatusNotice notice) =>
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
            Duration = notice.Level is StatusLevel.Error ? null : TimeSpan.FromSeconds(6),
        });
}
