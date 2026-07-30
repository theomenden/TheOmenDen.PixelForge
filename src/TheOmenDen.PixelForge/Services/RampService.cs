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

    /// <summary>The user's ramps — everything that is not shipped.</summary>
    public IEnumerable<SkinRamp> Custom
    {
        get
        {
            foreach (var ramp in Ramps)
            {
                if (!SkinRamps.IsBuiltIn(ramp))
                {
                    yield return ramp;
                }
            }
        }
    }

    /// <summary>
    /// Replaces the customs with what is on disk. The built-ins are never touched.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>How many customs were loaded, or the failure that stopped the read.</returns>
    public async Task<Result<int, RampFailure>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.LoadAsync(cancellationToken);

        if (!loaded.TryGet(out var ramps))
        {
            logger.LogWarning("Could not load custom ramps: {Failure}", loaded.Error);

            return new(loaded.Error);
        }

        // Drop only the customs — the built-ins stay put so the view never sees an empty list.
        for (var i = Ramps.Count - 1; i >= 0; i--)
        {
            if (!SkinRamps.IsBuiltIn(Ramps[i]))
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

    /// <summary>Writes the customs out. Built-ins are the contract and never reach the store.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>How many were written, or the failure that stopped it.</returns>
    public Task<Result<int, RampFailure>> SaveAsync(CancellationToken cancellationToken = default) =>
        _store.SaveAsync([.. Custom], cancellationToken);

    /// <summary>Adds a custom ramp and persists the set.</summary>
    /// <param name="ramp">The ramp to add.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The stored count, or why the ramp was rejected.</returns>
    public async Task<Result<int, RampFailure>> AddAsync(
        SkinRamp ramp,
        CancellationToken cancellationToken = default)
    {
        var rejected = Validate(ramp, replacing: null);

        if (rejected.HasValue)
        {
            return new(rejected.Value);
        }

        Ramps.Add(ramp);

        return await SaveAsync(cancellationToken);
    }

    /// <summary>Replaces the custom ramp called <paramref name="name"/> — the rename path too.</summary>
    /// <param name="name">Name of the ramp being replaced, before any rename.</param>
    /// <param name="ramp">Its replacement.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The stored count, or why the replacement was rejected.</returns>
    public async Task<Result<int, RampFailure>> ReplaceAsync(
        string name,
        SkinRamp ramp,
        CancellationToken cancellationToken = default)
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

        return await SaveAsync(cancellationToken);
    }

    /// <summary>Drops the custom ramp called <paramref name="name"/> and persists the set.</summary>
    /// <param name="name">The ramp to remove. Built-ins are never a match.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The stored count, or <see cref="RampFailure.NotFound"/>.</returns>
    public async Task<Result<int, RampFailure>> RemoveAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var index = IndexOfCustom(name);

        if (index < 0)
        {
            return new(RampFailure.NotFound);
        }

        Ramps.RemoveAt(index);

        return await SaveAsync(cancellationToken);
    }

    /// <summary>Merges a CSV in. Existing names are replaced rather than duplicated.</summary>
    /// <param name="file">The CSV to merge.</param>
    /// <param name="cancellationToken">Cancels the read and the write that follows it.</param>
    /// <returns>How many ramps were taken in, or the failure that stopped it.</returns>
    public async Task<Result<int, RampFailure>> ImportAsync(
        FullPath file,
        CancellationToken cancellationToken = default)
    {
        var imported = await new RampStore(file).LoadAsync(cancellationToken);

        if (!imported.TryGet(out var ramps))
        {
            return new(imported.Error);
        }

        var added = 0;

        foreach (var ramp in ramps)
        {
            if (SkinRamps.IsBuiltIn(ramp))
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

        var saved = await SaveAsync(cancellationToken);

        if (!saved.IsSuccessful)
        {
            return new(saved.Error);
        }

        return added;
    }

    /// <summary>Writes the customs to a CSV of the user's choosing.</summary>
    /// <param name="file">Where to write.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>How many were written, or the failure that stopped it.</returns>
    public Task<Result<int, RampFailure>> ExportAsync(
        FullPath file,
        CancellationToken cancellationToken = default) =>
        new RampStore(file).SaveAsync([.. Custom], cancellationToken);

    /// <summary>Index into <see cref="Ramps"/>, or -1. Built-ins are never a match.</summary>
    private int IndexOfCustom(string name)
    {
        for (var i = 0; i < Ramps.Count; i++)
        {
            if (!SkinRamps.IsBuiltIn(Ramps[i]) && string.Equals(Ramps[i].Name, name, StringComparison.OrdinalIgnoreCase))
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

        if (SkinRamps.IsBuiltIn(ramp))
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
