using DotNext;
using Meziantou.Framework;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>One recipe's outcome, reported the moment it finishes.</summary>
/// <remarks>
/// <see cref="BakeFailure"/> is numbered from 1, which is load-bearing here: a
/// <see cref="Failure"/> of <c>default</c> <em>is</em> the success signal, so no separate
/// boolean can drift out of step with it.
/// </remarks>
public readonly record struct BakeProgress
{
    public required string Name { get; init; }

    /// <summary>Absent on failure — there is nothing to have written.</summary>
    public required Optional<ByteSize> Written { get; init; }

    /// <summary><c>default</c> means the sheet was written.</summary>
    public required BakeFailure Failure { get; init; }

    /// <summary>Running position in the run, 1-based. Not an index — order is not guaranteed.</summary>
    public required int Completed { get; init; }

    public required int Total { get; init; }

    public bool IsSuccess => Failure is default(BakeFailure);
}
