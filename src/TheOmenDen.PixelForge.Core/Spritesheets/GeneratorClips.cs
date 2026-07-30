using System.Collections.Immutable;

namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>
/// Every animation the Elements generator ships, transcribed from its <c>Settings.json</c>
/// <c>CharacterAnimations</c> block.
/// </summary>
/// <remarks>
/// <para>
/// This is the machine-readable spec for the source art, so it is copied rather than inferred:
/// <c>jump</c> opening on the crouch frame and <c>attack_tool</c> opening on the wind-up frame are
/// the generator's decisions, not observations about the pixels.
/// </para>
/// <para>
/// Used only by <see cref="Baking.SheetGeometry.Full"/> output. The curated Corvus sheet keeps its
/// own eight-clip subset in <see cref="SheetLayout.Clips"/> and is unaffected by anything here.
/// </para>
/// </remarks>
public static class GeneratorClips
{
    /// <summary>
    /// The generator's <c>AnimationDelayInMilliseconds</c>. Roughly 3.3 FPS, which is the
    /// deliberate cadence of this art style's walk cycle — shipped in the manifest so a consumer
    /// plays it at the rate it was authored for rather than guessing.
    /// </summary>
    public const int FrameDurationMilliseconds = 300;

    /// <summary>
    /// The same cadence in seconds, divided once at type initialisation.
    /// </summary>
    /// <remarks>
    /// Here rather than at the call site so the division happens once for the process instead of
    /// on every caller that wants a clip length in seconds, and so there is one definition of the
    /// conversion rather than a <c>/ 1000.0</c> repeated wherever it is needed.
    /// </remarks>
    public static double FrameDurationSeconds { get; } = FrameDurationMilliseconds / 1000.0;

    /// <summary>
    /// Source rows top to bottom. Full geometry keeps all four; the curated sheet drops
    /// <c>north</c> — see <see cref="SheetLayout.FacingCount"/>.
    /// </summary>
    public static ImmutableArray<string> Facings { get; } = ["south", "west", "east", "north"];

    /// <summary>All twelve animations, in the order the generator declares them.</summary>
    public static ImmutableArray<GeneratorClip> All { get; } =
    [
        Clip("stand", [1], rendered: false),
        Clip("walk", [1, 2, 1, 0], rendered: true),
        Clip("arms_up", [4, 5, 4, 3], rendered: true),
        Clip("crouch", [6], rendered: false),
        Clip("jump", [6, 7, 8, 9], rendered: true),
        Clip("wind_up", [10], rendered: false),
        Clip("attack_tool", [10, 11, 12, 13, 14], rendered: true),
        Clip("nock", [15], rendered: false),
        Clip("bow", [17, 18, 17, 16], rendered: false),
        Clip("nock_and_bow", [15, 16, 17, 18], rendered: true),
        Clip("climb", [20, 21, 20, 19], rendered: true, reverseDrawOrder: true),
        Clip("sleep_dead", [22], rendered: true),
    ];

    private static GeneratorClip Clip(
        string name,
        ImmutableArray<int> frames,
        bool rendered,
        bool reverseDrawOrder = false) =>
        new()
        {
            Name = name,
            Frames = frames,
            IsRenderedByDefault = rendered,
            ReverseDrawOrder = reverseDrawOrder,
        };
}
