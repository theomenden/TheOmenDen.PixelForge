using Meziantou.Framework;
using Windows.ApplicationModel;
using Windows.Storage;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// Where the app keeps writable state.
/// <para>
/// A packaged app's install directory is read-only, so everything writable goes to LocalState.
/// This is the single place that branch is made — <c>ApplicationData.Current</c> throws without
/// package identity, and the "Unpackaged" launch profile has none.
/// </para>
/// <para>
/// Deliberately not <c>ApplicationData.Current.LocalSettings</c>: it has the same identity
/// requirement, so a settings API would need this same branch anyway. Plain files under one
/// directory keep both launch modes on one path.
/// </para>
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// <c>Package.Current</c> throws when the app runs without package identity, which is the
    /// only reliable way to detect it.
    /// </summary>
    public static bool IsPackaged
    {
        get
        {
            try
            {
                return Package.Current is not null;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// A static property initialiser runs once, which is all the caching this needs — no
    /// <c>Lazy&lt;T&gt;</c> (banned) and no hand-rolled null check.
    /// </summary>
    public static FullPath LocalState { get; } = FullPath.FromPath(IsPackaged
        ? ApplicationData.Current.LocalFolder.Path
        : AppContext.BaseDirectory);

    public static FullPath Logs => LocalState / "logs";

    public static FullPath RampStoreFile => LocalState / "ramps.csv";

    public static FullPath PackSettingsFile => LocalState / "packs.json";
}
