namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>
/// How serious a status message is. A plain enum rather than <c>InfoBarSeverity</c>, which is a
/// <c>Microsoft.UI.*</c> type and would put XAML in the view models. The page maps it.
/// </summary>
public enum StatusLevel
{
    Informational,
    Success,
    Warning,
    Error,
}
