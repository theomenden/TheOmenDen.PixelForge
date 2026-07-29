namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>One thing worth telling the user about.</summary>
public readonly record struct StatusNotice(string Message, StatusLevel Level);
