using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace PKForge.MauiLite;

/// <summary>Where the app's data, cache and preferences live on this host.</summary>
public sealed record LitePaths(string AppData, string Cache, string PreferencesFile);

/// <summary>What the host tells MauiLite before the app builds: its folders, its package files and its own services.</summary>
public static class LitePlatform
{
    internal static LitePaths? Paths { get; private set; }

    internal static Action<IServiceCollection>? PlatformServices { get; private set; }

    /// <param name="platformServices">Registered after the app's own services, so they win (the last registration does).</param>
    public static void Configure(LitePaths paths, Func<string, Stream> openPackageFile, Action<IServiceCollection>? platformServices = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(openPackageFile);
        Paths = paths;
        LitePackage.Opener = openPackageFile;
        PlatformServices = platformServices;
        Preferences.SetDefault(new JsonPreferences(paths.PreferencesFile));
    }

    /// <summary>What <c>AppInfo</c> reports (Android takes these from the app's manifest).</summary>
    public static void SetAppInfo(string name, string packageName, string version, string build) =>
        AppInfo.Current = new LiteAppInfo(name, packageName, version, build);
}
