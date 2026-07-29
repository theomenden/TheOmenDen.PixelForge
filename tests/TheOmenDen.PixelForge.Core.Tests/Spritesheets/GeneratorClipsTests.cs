using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Spritesheets;

/// <summary>
/// The generator's own animation table, transcribed. These are not inferred from the art — they
/// come from <c>Settings.json</c>'s <c>CharacterAnimations</c> block, which is why <c>walk</c>
/// opens on column 1 and returns through column 0, and why <c>jump</c> opens on the crouch frame.
/// </summary>
public sealed class GeneratorClipsTests
{
    private static GeneratorClip Clip(string name)
        => GeneratorClips.All.AsSpan().First(clip => string.Equals(clip.Name, name, StringComparison.Ordinal));

    [Fact]
    public void All_HasTheTwelveGeneratorAnimations() => Assert.Equal(12, GeneratorClips.All.Length);

    /// <summary>
    /// The property the curated <see cref="SheetLayout"/> model cannot express: these are
    /// playback orders, not contiguous spans. <c>walk</c> visits 1, 2, 1, 0.
    /// </summary>
    [Theory]
    [InlineData("walk", new[] { 1, 2, 1, 0 })]
    [InlineData("arms_up", new[] { 4, 5, 4, 3 })]
    [InlineData("jump", new[] { 6, 7, 8, 9 })]
    [InlineData("attack_tool", new[] { 10, 11, 12, 13, 14 })]
    [InlineData("nock_and_bow", new[] { 15, 16, 17, 18 })]
    [InlineData("bow", new[] { 17, 18, 17, 16 })]
    [InlineData("climb", new[] { 20, 21, 20, 19 })]
    [InlineData("stand", new[] { 1 })]
    [InlineData("crouch", new[] { 6 })]
    [InlineData("wind_up", new[] { 10 })]
    [InlineData("nock", new[] { 15 })]
    [InlineData("sleep_dead", new[] { 22 })]
    public void All_CarriesTheRealPlaybackOrder(string name, int[] expected)
        => Assert.Equal(expected, Clip(name).Frames);

    /// <summary>Climb is the only animation the generator draws back to front.</summary>
    [Fact]
    public void ReverseDrawOrder_IsSetOnClimbAlone()
    {
        foreach (var clip in GeneratorClips.All)
        {
            Assert.Equal(string.Equals(clip.Name, "climb", StringComparison.Ordinal), clip.ReverseDrawOrder);
        }
    }

    [Fact]
    public void All_NeverReferencesAColumnOutsideTheSourceSheet()
    {
        foreach (var clip in GeneratorClips.All)
        {
            foreach (var column in clip.Frames)
            {
                Assert.InRange(column, 0, SheetLayout.SourceColumns - 1);
            }
        }
    }

    /// <summary>Full geometry keeps all four facings; the curated sheet drops north.</summary>
    [Fact]
    public void Facings_AreTheFourSourceRowsInOrder()
        => Assert.Equal(["south", "west", "east", "north"], GeneratorClips.Facings);

    [Fact]
    public void FrameDuration_IsTheGeneratorsOwn() => Assert.Equal(300, GeneratorClips.FrameDurationMilliseconds);
}
