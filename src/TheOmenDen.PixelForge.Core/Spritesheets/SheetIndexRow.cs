namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>One clip on one facing, and the output row it occupies.</summary>
public sealed record SheetIndexRow
{
    public required string Clip { get; init; }

    public required string Facing { get; init; }

    /// <summary>Output row, 0-based.</summary>
    public required int Row { get; init; }

    public required int FrameCount { get; init; }

    /// <summary>Column of frame 0 in the 23-column source, kept for traceability.</summary>
    public required int FirstColumn { get; init; }

    public required int CellSize { get; init; }
}
