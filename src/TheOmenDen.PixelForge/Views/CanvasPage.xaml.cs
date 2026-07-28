using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TheOmenDen.PixelForge.Views;

public sealed partial class CanvasPage : Page
{
    /// <summary>Effective DIPs below which the tool panel collapses to an icon strip.</summary>
    private const double CompactThresholdDips = 900;

    public CanvasPage() => InitializeComponent();

    /// <summary>
    /// Drives the tool panel states from the width in *effective DIPs*.
    /// <para>
    /// AdaptiveTrigger.MinWindowWidth is deliberately not used: it compares against raw window
    /// pixels, so on a 150% display a 900 threshold does not fire until the window is down to
    /// 600 DIPs — far too late. Dividing ActualWidth by RasterizationScale is correct at any DPI.
    /// </para>
    /// </summary>
    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // RasterizationScale is 0 before the page is in a live visual tree.
        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        var effectiveDips = scale > 0 ? e.NewSize.Width / scale : e.NewSize.Width;

        var state = effectiveDips < CompactThresholdDips ? "ToolPanelCompact" : "ToolPanelWide";
        VisualStateManager.GoToState(this, state, useTransitions: false);
    }
}
