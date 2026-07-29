using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Palettes;

public sealed class RampStoreTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    private static SkinRamp Custom(string name) => new()
    {
        Name = name,
        IsHuman = false,
        Steps = [.. new[]
        {
            new SKColor(0x11, 0x22, 0x33),
            new SKColor(0x44, 0x55, 0x66),
            new SKColor(0x77, 0x88, 0x99),
            new SKColor(0xAA, 0xBB, 0xCC),
            new SKColor(0xDD, 0xEE, 0xFF),
        }],
    };

    [Fact]
    public void ReadWrite_RoundTripsARamp()
    {
        using var writer = new StringWriter();

        var written = RampStore.Write(writer, [Custom("My Ramp")]);

        Assert.True(written.IsSuccessful, $"write failed with {written.Error}");
        Assert.Equal(1, written.Value);

        using var reader = new StringReader(writer.ToString());

        var read = RampStore.Read(reader);

        Assert.True(read.IsSuccessful, $"read failed with {read.Error}");

        var ramp = Assert.Single(read.Value.AsSpan().ToArray());

        Assert.Equal("My Ramp", ramp.Name);
        Assert.False(ramp.IsHuman);
        Assert.Equal(SkinRamps.StepCount, ramp.Steps.Length);
        Assert.Equal(new SKColor(0x11, 0x22, 0x33), ramp.Steps[0]);
        Assert.Equal(new SKColor(0xDD, 0xEE, 0xFF), ramp.Steps[4]);
    }

    /// <summary>
    /// The shipped ramps are the reference case — a file written from them must read back
    /// identical, or the format cannot represent what we already ship.
    /// </summary>
    [Fact]
    public void ReadWrite_RoundTripsEveryBuiltInRamp()
    {
        using var writer = new StringWriter();

        RampStore.Write(writer, [.. SkinRamps.All]);

        using var reader = new StringReader(writer.ToString());

        var read = RampStore.Read(reader);

        Assert.True(read.IsSuccessful, $"read failed with {read.Error}");
        Assert.Equal(SkinRamps.All.Length, read.Value.Length);

        for (var i = 0; i < SkinRamps.All.Length; i++)
        {
            Assert.Equal(SkinRamps.All[i].Name, read.Value[i].Name);
            Assert.Equal(SkinRamps.All[i].IsHuman, read.Value[i].IsHuman);
            Assert.Equal(SkinRamps.All[i].Steps, read.Value[i].Steps);
        }
    }

    [Fact]
    public void Read_ReportsStoreMalformed_WhenAStepIsNotHex()
    {
        using var reader = new StringReader(
            """
            Name,IsHuman,Step1,Step2,Step3,Step4,Step5
            Bad,False,#GGGGGG,#445566,#778899,#AABBCC,#DDEEFF
            """);

        var read = RampStore.Read(reader);

        Assert.False(read.IsSuccessful);
        Assert.Equal(RampFailure.StoreMalformed, read.Error);
    }

    [Fact]
    public void Read_ReportsNameEmpty_WhenARampHasNoName()
    {
        using var reader = new StringReader(
            """
            Name,IsHuman,Step1,Step2,Step3,Step4,Step5
            ,False,#112233,#445566,#778899,#AABBCC,#DDEEFF
            """);

        var read = RampStore.Read(reader);

        Assert.False(read.IsSuccessful);
        Assert.Equal(RampFailure.NameEmpty, read.Error);
    }

    [Fact]
    public void Read_ReportsDuplicateName_WhenTwoRampsShareOne()
    {
        using var reader = new StringReader(
            """
            Name,IsHuman,Step1,Step2,Step3,Step4,Step5
            Same,False,#112233,#445566,#778899,#AABBCC,#DDEEFF
            Same,False,#112233,#445566,#778899,#AABBCC,#DDEEFF
            """);

        var read = RampStore.Read(reader);

        Assert.False(read.IsSuccessful);
        Assert.Equal(RampFailure.DuplicateName, read.Error);
    }

    [Fact]
    public void Write_ReportsWrongStepCount_WhenARampIsNotFiveSteps()
    {
        using var writer = new StringWriter();

        var truncated = new SkinRamp
        {
            Name = "Short",
            IsHuman = false,
            Steps = [new SKColor(1, 2, 3)],
        };

        var written = RampStore.Write(writer, [truncated]);

        Assert.False(written.IsSuccessful);
        Assert.Equal(RampFailure.WrongStepCount, written.Error);
    }

    [Fact]
    public void Load_ReturnsEmpty_WhenTheFileDoesNotExist()
    {
        var store = new RampStore(_directory.FullPath / "absent.csv");

        var loaded = store.Load();

        Assert.True(loaded.IsSuccessful, $"load failed with {loaded.Error}");
        Assert.Empty(loaded.Value);
    }

    [Fact]
    public void SaveLoad_RoundTripsThroughDisk()
    {
        var store = new RampStore(_directory.FullPath / "ramps.csv");

        var saved = store.Save([Custom("Disk Ramp")]);

        Assert.True(saved.IsSuccessful, $"save failed with {saved.Error}");

        var loaded = store.Load();

        Assert.True(loaded.IsSuccessful, $"load failed with {loaded.Error}");
        Assert.Equal("Disk Ramp", Assert.Single(loaded.Value.AsSpan().ToArray()).Name);
    }

    /// <summary>Saving into a directory that does not exist creates it rather than failing.</summary>
    [Fact]
    public void Save_CreatesTheParentDirectory()
    {
        var store = new RampStore(_directory.FullPath / "nested" / "ramps.csv");

        var saved = store.Save([Custom("Nested")]);

        Assert.True(saved.IsSuccessful, $"save failed with {saved.Error}");
        Assert.True(File.Exists((_directory.FullPath / "nested" / "ramps.csv").Value));
    }
}
