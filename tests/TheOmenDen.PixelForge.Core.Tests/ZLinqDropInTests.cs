namespace TheOmenDen.PixelForge.Core.Tests;

/// <summary>
/// Guards the ZLinq drop-in wiring. If the generator stops running or the assembly
/// attribute goes missing, LINQ silently falls back to System.Linq — the build stays green
/// and the allocations come back unnoticed. These tests fail to compile in that case.
/// </summary>
// IDE0007 (var everywhere) is suppressed for this file alone. The explicit types below are
// the assertions: under `var` these chains compile no matter which Where/Select bound, which
// is precisely the silent fallback the file exists to catch.
#pragma warning disable IDE0007
public sealed class ZLinqDropInTests
{
    [Fact]
    public void ArrayWhere_BindsToZLinq_NotSystemLinq()
    {
        int[] pixels = [1, 2, 3, 4];

        // No .AsValueEnumerable() — the drop-in generator rebinds this call.
        // The explicit type is the assertion: System.Linq's Where returns
        // IEnumerable<int>, which does not convert to ValueEnumerable.
        ValueEnumerable<ArrayWhere<int>, int> query = pixels.Where(static p => p > 2);

        Assert.Equal([3, 4], query.ToArray());
    }

    [Fact]
    public void ListSelect_BindsToZLinq_NotSystemLinq()
    {
        List<int> levels = [1, 2, 3];

        // ZLinq emits a List-specialised enumerator rather than the generic
        // Select<FromList<...>> — another sign the drop-in, not System.Linq, bound here.
        ValueEnumerable<ListSelect<int, int>, int> query = levels.Select(static x => x * 2);

        Assert.Equal([2, 4, 6], query.ToArray());
    }
}
#pragma warning restore IDE0007
