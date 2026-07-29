using System.Collections.Immutable;
using System.Collections.ObjectModel;
using DotNext;
using Meziantou.Framework;
using Microsoft.Extensions.Logging;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// The ramps the app knows about: the seven shipped ones plus whatever the user has added.
/// <para>
/// Built-ins are the Corvus contract and are never written to the store or edited in place —
/// editing one is offered as "duplicate to edit" in the UI. Names identify a ramp, and
/// uniqueness is enforced across both sets so a custom cannot shadow a built-in.
/// </para>
/// </summary>
public sealed class RampService(ILogger<RampService> logger)
{
    private readonly RampStore _store = new(AppPaths.RampStoreFile);

    /// <summary>
    /// Every ramp, built-ins first. <strong>One</strong> collection, not a built-in array plus a
    /// custom collection plus a computed concatenation — this is the stable source an
    /// <c>AdvancedCollectionView</c> observes, and a view over a collection that is rebuilt on
    /// every change is a view that loses its selection on every change.
    /// </summary>
    public ObservableCollection<SkinRamp> Ramps { get; } = [.. SkinRamps.All];

    // CA1822: flagged as not using instance data, but this is intentionally an instance member —
    // part of the same instance-bound surface as Ramps/Custom that Task 13 binds to.
#pragma warning disable CA1822
    public ImmutableArray<SkinRamp> BuiltIn => SkinRamps.All;
#pragma warning restore CA1822

    /// <summary>The user's ramps — everything that is not shipped.</summary>
    public IEnumerable<SkinRamp> Custom
    {
        get
        {
            foreach (var ramp in Ramps)
            {
                if (!IsBuiltIn(ramp))
                {
                    yield return ramp;
                }
            }
        }
    }

    public bool IsBuiltIn(SkinRamp ramp) =>
        BuiltIn.AsSpan().Any(r => string.Equals(r.Name, ramp.Name, StringComparison.OrdinalIgnoreCase));

    public Result<int, RampFailure> Load()
    {
        var loaded = _store.Load();

        if (!loaded.TryGet(out var ramps))
        {
            logger.LogWarning("Could not load custom ramps: {Failure}", loaded.Error);

            return new(loaded.Error);
        }

        // Drop only the customs — the built-ins stay put so the view never sees an empty list.
        for (var i = Ramps.Count - 1; i >= 0; i--)
        {
            if (!IsBuiltIn(Ramps[i]))
            {
                Ramps.RemoveAt(i);
            }
        }

        foreach (var ramp in ramps)
        {
            Ramps.Add(ramp);
        }

        return ramps.Length;
    }

    public Result<int, RampFailure> Save() => _store.Save([.. Custom]);

    public Result<int, RampFailure> Add(SkinRamp ramp)
    {
        var rejected = Validate(ramp, replacing: null);

        if (rejected.HasValue)
        {
            return new(rejected.Value);
        }

        Ramps.Add(ramp);

        return Save();
    }

    /// <summary>Replaces the custom ramp called <paramref name="name"/> — the rename path too.</summary>
    public Result<int, RampFailure> Replace(string name, SkinRamp ramp)
    {
        var index = IndexOfCustom(name);

        if (index < 0)
        {
            return new(RampFailure.NotFound);
        }

        var rejected = Validate(ramp, replacing: name);

        if (rejected.HasValue)
        {
            return new(rejected.Value);
        }

        Ramps[index] = ramp;

        return Save();
    }

    public Result<int, RampFailure> Remove(string name)
    {
        var index = IndexOfCustom(name);

        if (index < 0)
        {
            return new(RampFailure.NotFound);
        }

        Ramps.RemoveAt(index);

        return Save();
    }

    /// <summary>Merges a CSV in. Existing names are replaced rather than duplicated.</summary>
    public Result<int, RampFailure> Import(FullPath file)
    {
        var imported = new RampStore(file).Load();

        if (!imported.TryGet(out var ramps))
        {
            return new(imported.Error);
        }

        var added = 0;

        foreach (var ramp in ramps)
        {
            if (IsBuiltIn(ramp))
            {
                // A built-in's name is taken. Skip rather than fail the whole import.
                logger.SkippedBuiltInImport(ramp.Name);
                continue;
            }

            var index = IndexOfCustom(ramp.Name);

            if (index >= 0)
            {
                Ramps[index] = ramp;
            }
            else
            {
                Ramps.Add(ramp);
            }

            added++;
        }

        var saved = Save();

        if (!saved.IsSuccessful)
        {
            return new(saved.Error);
        }

        return added;
    }

    public Result<int, RampFailure> Export(FullPath file) => new RampStore(file).Save([.. Custom]);

    /// <summary>Index into <see cref="Ramps"/>, or -1. Built-ins are never a match.</summary>
    private int IndexOfCustom(string name)
    {
        for (var i = 0; i < Ramps.Count; i++)
        {
            if (!IsBuiltIn(Ramps[i]) && string.Equals(Ramps[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Null means acceptable.</summary>
    private RampFailure? Validate(SkinRamp ramp, string? replacing)
    {
        if (string.IsNullOrWhiteSpace(ramp.Name))
        {
            return RampFailure.NameEmpty;
        }

        if (ramp.Steps.Length != SkinRamps.StepCount)
        {
            return RampFailure.WrongStepCount;
        }

        if (IsBuiltIn(ramp))
        {
            return RampFailure.DuplicateName;
        }

        var clash = IndexOfCustom(ramp.Name);

        if (clash >= 0 && !string.Equals(Ramps[clash].Name, replacing, StringComparison.OrdinalIgnoreCase))
        {
            return RampFailure.DuplicateName;
        }

        return null;
    }
}

/// <summary>
/// Source-generated log methods — no boxing, and nothing is formatted when the level is off.
/// </summary>
internal static partial class RampServiceLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped imported ramp {Name}: name is a built-in")]
    public static partial void SkippedBuiltInImport(this ILogger logger, string name);
}
