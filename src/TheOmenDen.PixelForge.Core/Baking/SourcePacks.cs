using CommunityToolkit.Diagnostics;
using DotNext;
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

    /// <summary>
    /// The Time Fantasy characters pack, when one is configured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Optional{T}"/> and not <see langword="required"/>, for two reasons. A fourth
    /// required member would break every construction site including the tests, and — more
    /// importantly — this pack is genuinely optional: the Assets and Pipeline pages gate on the
    /// three Time Elements packs being set, and folding this into that gate would blank the app for
    /// every existing user until they supplied art they may not own.
    /// </para>
    /// <para>
    /// A pack root rather than an <c>assets</c> directory, because Time Fantasy is not organised
    /// into per-slot folders. It ships finished characters under <c>sheets/</c> and single frames
    /// under <c>frames/</c>, so a recipe names a file beneath this rather than a slot within it.
    /// </para>
    /// </remarks>
    public Optional<FullPath> FantasyRoot { get; init; } = Optional<FullPath>.None;

    public FullPath Partial(ElementsPack pack, string slot, string file) => pack switch
    {
        ElementsPack.Core => CoreAssets / slot / file,
        ElementsPack.CharacterExpansion1 => Expansion1Assets / slot / file,
        ElementsPack.CharacterExpansion2 => Expansion2Assets / slot / file,
        _ => ThrowHelper.ThrowArgumentOutOfRangeException<FullPath>(nameof(pack)),
    };
}
