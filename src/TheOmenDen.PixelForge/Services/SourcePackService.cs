using System.Text.Json;
using DotNext;
using Meziantou.Framework;
using Microsoft.Extensions.Logging;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// Holds the three Time Elements pack directories and persists them to LocalState.
/// <para>
/// The packs live outside every repo — a directory of raw per-slot partials <em>is</em> the
/// asset pack, which the licence does not let us redistribute. So the app cannot ship them and
/// has to be told where they are.
/// </para>
/// <para>
/// No interface: there is one implementation and nothing mocks it. The concrete class is
/// registered directly.
/// </para>
/// </summary>
public sealed class SourcePackService(ILogger<SourcePackService> logger)
{
    public Optional<FullPath> Core { get; private set; } = Optional<FullPath>.None;

    public Optional<FullPath> Expansion1 { get; private set; } = Optional<FullPath>.None;

    public Optional<FullPath> Expansion2 { get; private set; } = Optional<FullPath>.None;

    /// <summary>Raised whenever a path changes, so pages can re-evaluate whether export is possible.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The packs, but only when all three are set and still on disk. A pack directory that was
    /// deleted since it was configured must not produce a <see cref="SourcePacks"/> that fails
    /// 79 times during a batch run.
    /// </summary>
    public Optional<SourcePacks> Resolved
    {
        get
        {
            if (!Core.TryGet(out var core)
                || !Expansion1.TryGet(out var expansion1)
                || !Expansion2.TryGet(out var expansion2))
            {
                return Optional<SourcePacks>.None;
            }

            if (!Directory.Exists(core.Value)
                || !Directory.Exists(expansion1.Value)
                || !Directory.Exists(expansion2.Value))
            {
                return Optional<SourcePacks>.None;
            }

            return new SourcePacks
            {
                CoreAssets = core,
                Expansion1Assets = expansion1,
                Expansion2Assets = expansion2,
            };
        }
    }

    public void Set(ElementsPack pack, FullPath path)
    {
        switch (pack)
        {
            case ElementsPack.Core:
                Core = path;
                break;
            case ElementsPack.CharacterExpansion1:
                Expansion1 = path;
                break;
            case ElementsPack.CharacterExpansion2:
                Expansion2 = path;
                break;
            default:
                return;
        }

        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Load()
    {
        var file = AppPaths.PackSettingsFile;

        if (!File.Exists(file.Value))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(file.Value);

            var settings = JsonSerializer.Deserialize(stream, PackSettingsContext.Default.PackSettings);

            if (settings is null)
            {
                return;
            }

            Core = ToPath(settings.CoreAssets);
            Expansion1 = ToPath(settings.Expansion1Assets);
            Expansion2 = ToPath(settings.Expansion2Assets);

            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt settings file must not stop the app starting — the user can re-pick.
            logger.LogWarning(exception, "Could not read pack settings from {File}", file);
        }
    }

    private void Save()
    {
        var file = AppPaths.PackSettingsFile;

        try
        {
            Directory.CreateDirectory(AppPaths.LocalState.Value);

            using var stream = File.Create(file.Value);

            JsonSerializer.Serialize(stream, new PackSettings
            {
                CoreAssets = Core.TryGet(out var core) ? core.Value : null,
                Expansion1Assets = Expansion1.TryGet(out var one) ? one.Value : null,
                Expansion2Assets = Expansion2.TryGet(out var two) ? two.Value : null,
            }, PackSettingsContext.Default.PackSettings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not write pack settings to {File}", file);
        }
    }

    private static Optional<FullPath> ToPath(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Optional<FullPath>.None : FullPath.FromPath(value);
}
