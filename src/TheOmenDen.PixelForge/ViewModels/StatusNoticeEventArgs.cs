namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>One thing worth telling the user.</summary>
/// <remarks>
/// Derives from <see cref="EventArgs"/> so it can ride on <see cref="EventHandler{TEventArgs}"/>,
/// which is the .NET convention for event payloads — and the suffix is the other half of that
/// convention. That is also why this is a class rather than the <see langword="readonly"/>
/// <see langword="record"/> <see langword="struct"/> it would otherwise be: a record may only
/// inherit from <see langword="object"/> or another record (CS8864), so deriving forces a plain
/// class.
/// </remarks>
/// <param name="message">Text to show the user.</param>
/// <param name="level">How loudly to show it.</param>
public sealed class StatusNoticeEventArgs(string message, StatusLevel level) : EventArgs
{
    /// <summary>Text to show the user.</summary>
    public string Message { get; } = message;

    /// <summary>How loudly to show it.</summary>
    public StatusLevel Level { get; } = level;
}
