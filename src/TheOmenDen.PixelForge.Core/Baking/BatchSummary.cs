using Meziantou.Framework;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>What a whole run came to.</summary>
public sealed record BatchSummary
{
    public required int Succeeded { get; init; }

    public required int Failed { get; init; }

    public required ByteSize TotalWritten { get; init; }

    /// <summary>The run stopped early. Sheets already written are kept.</summary>
    public required bool Cancelled { get; init; }
}
