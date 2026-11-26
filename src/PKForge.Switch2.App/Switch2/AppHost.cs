using Microsoft.Extensions.DependencyInjection;
using PKForge.App;
using PKForge.App.Services;
using PKForge.Domain;
using PKForge.MauiLite;
using PKForge.Switch2.Platform;

namespace PKForge.Switch2;

/// <summary>Starts PKForge on a MauiLite host: the console, or the headless host on a PC.</summary>
public static class AppHost
{
    /// <summary>PKForge.App's ApplicationDisplayVersion and ApplicationVersion (its Android manifest values).</summary>
    public const string Version = "3.2.0";
    public const string Build = "320";

    /// <param name="music">The console's music output; null (the headless host) keeps the app silent.</param>
    public static LiteRunner Start(IHostBackend backend, IConsoleSaveHost saves, LitePaths paths, Func<string, Stream> openPackageFile,
        IMusicOutput? music = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        // An exception in app code that MauiLite called is logged, as AppLog logs Android's unhandled ones.
        UiErrors.Reported += (error, where) => AppLog.Error("ui", $"Unhandled in {where}", error);
        var root = Path.GetDirectoryName(paths.AppData.TrimEnd('/', '\\'))!;
        // Next to the data folder: sdmc:/switch/PKForge/save-backups on the console. Nothing deletes what goes there.
        var saveBackups = Path.Combine(root, "save-backups");
        // The music library is the files the player copies to sdmc:/switch/PKForge/music.
        Func<IMusicPlayer>? musicPlayer = music is null
            ? null
            : () => new ConsoleMusicPlayer(music, Path.Combine(root, "music"), Preferences.Default, UiLoop.Current);
        LitePlatform.Configure(paths, openPackageFile, services => ConsoleServices.Register(services, saves, saveBackups,
            provider => new ConsoleLegalityGuard(provider.GetRequiredService<ISaveEngine>(), provider.GetRequiredService<ILegalityService>(),
                provider.GetRequiredService<IBackupService>(), () => HaXMode.IsOn), musicPlayer));
        LitePlatform.SetAppInfo("PKForge", "org.pkforge.app", Version, Build);
        ClearStaleEvents(paths.Cache, openPackageFile);
        // The console's own storage is the one save source, so the emulator-linking wizard never shows.
        Preferences.Default.Set("setup_complete", true);
        var runner = new LiteRunner(MauiProgram.CreateMauiApp(), backend);
        var services = IPlatformApplication.Current!.Services;
        var pad = new PadMapping(services.GetService<GamepadRouter>, SystemBack);
        runner.Pad = pad.Update;
        var seedFlag = Path.Combine(Path.GetDirectoryName(paths.AppData.TrimEnd('/', '\\'))!, SeedFlagName);
        if (File.Exists(seedFlag))
        {
            File.Delete(seedFlag);
            _ = SeedRestorePointsAsync(services);
        }
        // The player's default music starts with the app, as App.OnStart does on Android.
        (services.GetService<IMusicPlayer>() as ConsoleMusicPlayer)?.MaybeAutostart();
        return runner;
    }

    /// <summary>
    /// A phase 1 test aid: a file of this name next to the data folder (sdmc:/switch/PKForge/ on the console) makes
    /// restore points to look at, since a read-only build makes none of its own. The app deletes the file first.
    /// </summary>
    public const string SeedFlagName = "seed-restore-points";

    /// <summary>
    /// One restore point per listed save, two more for the first, and that save's newest pinned. Points are files
    /// on the SD card (or the headless data folder); no console save is written.
    /// </summary>
    static async Task SeedRestorePointsAsync(IServiceProvider services)
    {
        try
        {
            var scan = await services.GetRequiredService<IEmulatorDetectionService>().ScanAsync(ConsoleWatchedRootStore.Root.TreeId, EmulatorKind.Console);
            // In a stable order: the scan's follows the storage, so "the first save" would change between runs.
            var saves = scan.Saves.OrderBy(save => save.DocumentId, StringComparer.Ordinal).ToList();
            var files = services.GetRequiredService<ISaveFileAccess>();
            var engine = services.GetRequiredService<ISaveEngine>();
            var backups = services.GetRequiredService<IBackupService>();
            for (var i = 0; i < saves.Count; i++)
            {
                var save = saves[i];
                var bytes = await files.ReadAsync(save.DocumentId);
                var snapshot = await Task.Run(() => engine.Open(bytes, save.EngineHint));
                BackupReceipt? newest = null;
                for (var point = 0; point < (i == 0 ? 3 : 1); point++)
                    newest = await backups.CreateAsync(snapshot, "Phase 1 test point", save.DocumentId);
                if (i == 0 && newest is not null)
                    await backups.SetPinnedAsync(newest.BackupId, pinned: true);
            }
        }
        catch (Exception error)
        {
            AppLog.Error("restore", "The phase 1 test restore points could not be made", error);
        }
    }

    /// <summary>
    /// EventArchive unpacks each generation's mystery-gift pack into the cache once and keeps it, so a build with
    /// other packs (the Switch build ships newer ones than Android's) clears what an older build unpacked.
    /// </summary>
    public static void ClearStaleEvents(string cache, Func<string, Stream> openPackageFile)
    {
        ArgumentNullException.ThrowIfNull(openPackageFile);
        // shortcut: the packs' sizes identify them, so a new pack of exactly the old size keeps the old events; hash them if that happens.
        // "names 2": builds before the SD card's names were encoded unpacked only up to the first name it refused
        // (a Japanese card), and EventArchive keeps a partly filled folder; the new stamp unpacks everything once.
        var stamp = "names 2 " + string.Join(' ', Enumerable.Range(4, 6).Select(generation => PackSize(openPackageFile, generation)));
        var folder = Path.Combine(cache, "events");
        var file = Path.Combine(folder, "packs.txt");
        try
        {
            if (File.Exists(file) && File.ReadAllText(file) == stamp)
                return;
            // Files only, listed before any is deleted: removing the folders failed on the console's SD card ("I/O
            // error"), and EventArchive unpacks again into a folder that holds no files.
            if (Directory.Exists(folder))
                foreach (var stale in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                    File.Delete(stale);
            Directory.CreateDirectory(folder);
            File.WriteAllText(file, stamp);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Older events are better than no app: the first console run of this check closed it at start-up.
            Console.Error.WriteLine($"PKForge events: what an older pack unpacked could not be cleared: {error}");
        }
    }

    static long PackSize(Func<string, Stream> openPackageFile, int generation)
    {
        try
        {
            using var pack = openPackageFile($"events/events-g{generation}.bin.gz");
            return pack.Length;
        }
        catch (IOException)
        {
            return -1;  // EventArchive copes with a missing pack: PKHeX's embedded events still work
        }
    }

    // B that no screen took: Android's back, which pops the navigation stack unless the page handles it.
    static void SystemBack()
    {
        if (Application.Current?.Windows is [{ Page: { } page }, ..])
            page.SendBackButtonPressed();
    }
}
