using System.Runtime.InteropServices;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// What a planned layer run consists of, broken out rather than summed.
/// </summary>
/// <remarks>
/// The parts are worth more than the total here. Under the old cross product the number was a
/// warning — five figures meant "you probably did not mean this". A layer run is double digits, so
/// the label stops warning and starts describing, and the only multiplier left is the tone axis,
/// which the breakdown makes visible at the moment the user is deciding what to tick.
/// </remarks>
/// <param name="Heroes">Distinct bodies — the required trio's combinations.</param>
/// <param name="Tones">Ticked skin ramps. Multiplies <paramref name="Heroes"/> and nothing else.</param>
/// <param name="Attachments">Optional partials, each baked once and shared by every hero.</param>
/// <remarks>
/// The first all-primitive struct here, so the first to trip MA0008: every other value type in this
/// assembly carries a <see langword="string"/> or a <c>FullPath</c>, which makes the layout the
/// runtime's business already. <see cref="LayoutKind.Auto"/> states the intent that was implicit.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PlannedCounts(long Heroes, long Tones, long Attachments)
{
    /// <summary>Sheets the run would write.</summary>
    /// <remarks>
    /// Saturating rather than wrapping, for the same reason <see cref="BatchPlan.Count"/> is: a
    /// label reading a negative number is worse than one reading an implausible one.
    /// </remarks>
    public long Sheets
    {
        get
        {
            var bodies = BatchPlan.Saturate(Heroes, Tones);

            return bodies > long.MaxValue - Attachments ? long.MaxValue : bodies + Attachments;
        }
    }
}
