namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>What a run emits.</summary>
public enum ExportMode
{
    /// <summary>One file per recipe. Hair stays its own texture — the Corvus contract.</summary>
    Layered,

    /// <summary>Body and hair composited into one texture per pair.</summary>
    Flattened,

    Both,
}
