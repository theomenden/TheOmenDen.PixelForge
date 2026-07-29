using CommunityToolkit.Diagnostics;
using Meziantou.Framework;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Where the source packs live. They are deliberately outside every repo — a directory of raw
/// per-slot partials <em>is</em> the asset pack, which the licence does not permit us to
/// redistribute. Only baked output is ever committed.
/// </summary>
public sealed record SourcePacks
{
    /// <summary>The <c>assets</c> directory of the core pack.</summary>
    public required FullPath CoreAssets { get; init; }

    /// <summary>The <c>assets</c> directory of Character Expansion 1.</summary>
    public required FullPath Expansion1Assets { get; init; }

    /// <summary>The <c>assets</c> directory of Character Expansion 2.</summary>
    public required FullPath Expansion2Assets { get; init; }

    public FullPath Partial(ElementsPack pack, string slot, string file) => pack switch
    {
        ElementsPack.Core => CoreAssets / slot / file,
        ElementsPack.CharacterExpansion1 => Expansion1Assets / slot / file,
        ElementsPack.CharacterExpansion2 => Expansion2Assets / slot / file,
        _ => ThrowHelper.ThrowArgumentOutOfRangeException<FullPath>(nameof(pack)),
    };
}
