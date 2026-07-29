namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// Why a ramp could not be read, written or accepted.
/// <para>
/// All environmental or data conditions — a hand-edited CSV, a name collision, a file someone
/// has open in Excel. None is a programming error, so none is an exception: they travel as
/// <see cref="DotNext.Result{T, TError}"/> values.
/// </para>
/// <para>Numbered from 1 so <c>default</c> is never mistaken for a real failure.</para>
/// </summary>
public enum RampFailure
{
    /// <summary>The file exists but could not be opened.</summary>
    StoreUnreadable = 1,

    /// <summary>The CSV parsed, but a row is not a ramp — a bad hex, a missing column.</summary>
    StoreMalformed,

    /// <summary>The file could not be written.</summary>
    StoreUnwritable,

    /// <summary>Not exactly <see cref="SkinRamps.StepCount"/> colours.</summary>
    WrongStepCount,

    /// <summary>A ramp with no name cannot be selected or referenced.</summary>
    NameEmpty,

    /// <summary>Ramps are identified by name, across built-ins and customs alike.</summary>
    DuplicateName,

    /// <summary>The seven shipped ramps are the contract and cannot be edited in place.</summary>
    BuiltInImmutable,

    /// <summary>No ramp by that name.</summary>
    NotFound,
}
