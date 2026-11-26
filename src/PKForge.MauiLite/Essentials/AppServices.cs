using PKForge.MauiLite;
using PKForge.MauiLite.Rendering;

namespace Microsoft.Maui.Storage
{
    /// <summary>MAUI's file system paths. MauiLite takes them from <see cref="LitePlatform.Configure"/>.</summary>
    public static class FileSystem
    {
        static LitePaths Paths => LitePlatform.Paths ?? throw new InvalidOperationException("MauiLite's paths are not configured: call LitePlatform.Configure first.");

        public static string AppDataDirectory
        {
            get
            {
                Directory.CreateDirectory(Paths.AppData);
                return Paths.AppData;
            }
        }

        public static string CacheDirectory
        {
            get
            {
                Directory.CreateDirectory(Paths.Cache);
                return Paths.Cache;
            }
        }

        public static Task<Stream> OpenAppPackageFileAsync(string filename) => Task.FromResult(LitePackage.Open(filename));

        public static Task<bool> AppPackageFileExistsAsync(string filename)
        {
            try
            {
                using var stream = LitePackage.Open(filename);
                return Task.FromResult(true);
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or IOException)
            {
                return Task.FromResult(false);
            }
        }
    }
}

namespace Microsoft.Maui.ApplicationModel
{
    public enum AppTheme { Unspecified, Light, Dark }

    public interface IAppInfo
    {
        string PackageName { get; }
        string Name { get; }
        string VersionString { get; }
        Version Version { get; }
        string BuildString { get; }
        AppTheme RequestedTheme { get; }
    }

    public static class AppInfo
    {
        public static IAppInfo Current { get; internal set; } = new LiteAppInfo("PKForge", "org.pkforge.app", "0.0.0", "0");

        public static string PackageName => Current.PackageName;
        public static string Name => Current.Name;
        public static string VersionString => Current.VersionString;
        public static Version Version => Current.Version;
        public static string BuildString => Current.BuildString;
        public static AppTheme RequestedTheme => Current.RequestedTheme;
    }

    sealed class LiteAppInfo(string name, string packageName, string version, string build) : IAppInfo
    {
        public string PackageName => packageName;
        public string Name => name;
        public string VersionString => version;
        public Version Version => Version.TryParse(version, out var parsed) ? parsed : new Version(0, 0);
        public string BuildString => build;

        // PKForge draws its own dark theme; Android would report the system theme here.
        public AppTheme RequestedTheme => AppTheme.Dark;
    }
}

namespace Microsoft.Maui.Devices
{
    public enum DisplayOrientation { Unknown, Portrait, Landscape }

    public enum DisplayRotation { Unknown, Rotation0, Rotation90, Rotation180, Rotation270 }

    public readonly struct DisplayInfo(double width, double height, double density, DisplayOrientation orientation, DisplayRotation rotation, float rate)
    {
        public double Width { get; } = width;
        public double Height { get; } = height;
        public double Density { get; } = density;
        public DisplayOrientation Orientation { get; } = orientation;
        public DisplayRotation Rotation { get; } = rotation;
        public float RefreshRate { get; } = rate;
    }

    public interface IDeviceDisplay
    {
        DisplayInfo MainDisplayInfo { get; }
        bool KeepScreenOn { get; set; }
    }

    /// <summary>The Switch 2's one screen: 1920×1080 pixels at MauiLite's density, in both handheld and docked mode.</summary>
    public static class DeviceDisplay
    {
        public static IDeviceDisplay Current { get; } = new LiteDisplay();

        public static DisplayInfo MainDisplayInfo => Current.MainDisplayInfo;

        public static bool KeepScreenOn
        {
            get => Current.KeepScreenOn;
            set => Current.KeepScreenOn = value;
        }

        sealed class LiteDisplay : IDeviceDisplay
        {
            public DisplayInfo MainDisplayInfo => new(1920, 1080, Display.Density, DisplayOrientation.Landscape, DisplayRotation.Rotation0, 60);

            // The console keeps the screen on while an application runs.
            public bool KeepScreenOn { get; set; }
        }
    }
}
