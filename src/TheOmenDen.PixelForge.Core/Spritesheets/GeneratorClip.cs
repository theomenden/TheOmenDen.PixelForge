using System.Collections.Immutable;

namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>
/// One animation exactly as the Elements generator defines it.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="AnimationClip"/>, and deliberately not merged with it.
/// <see cref="AnimationClip"/> models a curated clip as a start column plus a length, which is all
/// the Corvus contract needs and must not change. That model cannot represent a playback order
/// that revisits a column — <c>walk</c> is 1, 2, 1, 0 — so full geometry carries the frame list
/// verbatim instead.
/// </para>
/// </remarks>
public sealed record GeneratorClip
{
    /// <summary>Snake-cased name, e.g. <c>arms_up</c>. Stable across manifests.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Source columns in playback order. May repeat a column, and is not necessarily ascending.
    /// </summary>
    public required ImmutableArray<int> Frames { get; init; }

    /// <summary>
    /// Whether the generator composites this animation's layers back to front. Set on
    /// <c>climb</c> alone, where the character faces away and the body must occlude the hair.
    /// </summary>
    public required bool ReverseDrawOrder { get; init; }

    /// <summary>
    /// Whether the generator exports this animation by default. Its <c>IgnoreRender</c> flag
    /// inverted — the single-frame poses (<c>stand</c>, <c>crouch</c>, <c>wind_up</c>,
    /// <c>nock</c>) and <c>bow</c> are marked ignored because a longer animation already covers
    /// their columns. Carried so a consumer can tell a pose from an animation.
    /// </summary>
    public required bool IsRenderedByDefault { get; init; }

    /// <summary>How many frames the animation plays.</summary>
    public int FrameCount => Frames.Length;
}
