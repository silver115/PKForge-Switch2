using System.Runtime.InteropServices;
using PKForge.MauiLite;
using PKForge.Switch2;
using PKForge.Switch2.Platform;
using SkiaSharp;

// PKForge on the Switch 2. The host runs this from romfs, whose root is "/"; the app's data, cache and logs are on SD.
// Phase 1 builds are read-only on console saves. On exit, a results file in the logs folder records the start-up time,
// frame times per screen and any errors. The first frame of each screen is saved there too, as screen-<Page>.png,
// and the first frame drawn 5 s or more after it, once scans and animations have settled, as screen-<Page>-late.png.
const string Logs = "sdmc:/switch/PKForge/logs";
using var results = new ResultsWriter(Logs, Console.Out);
results.Write("env.runtime", RuntimeInformation.FrameworkDescription);
var (firmware, protocol) = NativeSystem.Versions();
results.Write("env.firmware", firmware);
results.Write("env.kaihen_protocol", protocol.ToString(System.Globalization.CultureInfo.InvariantCulture));

if (args.Contains("--backup-saves"))
{
    // netload.py --backup-saves: every Pokémon save copied to the SD card as it is, then quit (phase 2 design 4.1).
    var backup = ConsoleSaveBackup.All(new NativeConsoleSaveHost(), "sdmc:/switch/PKForge/save-backups", DateTime.UtcNow);
    results.Write("backup.folder", backup.Folder);
    results.Write("backup.files", backup.Files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
    results.Write("backup", backup.Passed ? "pass" : $"fail: {backup.Failures[0]}");
    return 0;
}
results.Write("env.live_log", NativeSystem.StartLiveLog() ? "connected" : "off");
SoftKeyboard.Show = NativeKeyboard.Show;  // a text field taking focus opens the console's keyboard
var errors = 0;
UiErrors.Reported += (_, _) => errors++;
var saved = new HashSet<string>();
var firstShown = new Dictionary<string, TimeSpan>();
var homeShown = false;
LiteRunner? runner = null;
NativeVideo.Open(1920, 1080);
var backend = new ConsoleBackend();
// netload.py --script <name>: a test script from romfs drives the app in place of the person, then the app quits.
var script = args.SkipWhile(arg => arg != "--script").Skip(1).FirstOrDefault();
var scripted = script is null ? null : new ScriptedBackend(backend);
try
{
    runner = AppHost.Start((IHostBackend?)scripted ?? backend, new NativeConsoleSaveHost(),
        new LitePaths("sdmc:/switch/PKForge/data", "sdmc:/switch/PKForge/cache", "sdmc:/switch/PKForge/data/preferences.json"),
        name => File.OpenRead("/app/" + name), new NativeMusicOutput());
    runner.Profiling = true;  // phase 1 builds find slow screens: frame.<Page>.draws and .dirt below
    // Composition draws in bands on every core the process has; the host gave the UI thread the lowest one.
    var mask = NativeSystem.CoreMask();
    int[] cores = [.. Enumerable.Range(0, 64).Where(core => (mask >> core & 1) != 0)];
    if (cores.Length > 1)
        runner.Bands = new DrawBands(cores, NativeSystem.PinThread);
    results.Write("env.draw_bands", (runner.Bands?.Count ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
    runner.Presented += (page, frame) =>
    {
        if (!homeShown && page == "HomePage")
        {
            homeShown = true;
            results.Write("coldstart.main_to_home_ms", Math.Round(NativeSystem.SinceMain().TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (saved.Add(page))
        {
            SaveScreen(page, frame);
            firstShown[page] = NativeSystem.SinceMain();
        }
        else if (NativeSystem.SinceMain() - firstShown[page] >= TimeSpan.FromSeconds(5) && saved.Add(page + "-late"))
            SaveScreen(page + "-late", frame);
    };
    if (scripted is null)
        runner.Run();
    else
    {
        var passed = ScriptRunner.Run(runner, scripted, File.ReadAllLines($"/app/scripts/{script}.txt"), $"{Logs}/script-{script}") == 0;
        results.Write($"script.{script}", passed ? "pass" : "fail");
        Console.WriteLine($"script {script}: {(passed ? "pass" : "fail")}");
    }
}
catch (Exception error)
{
    results.Write("app.error", $"{error.GetType().Name}: {error.Message.ReplaceLineEndings(" ")}");
    Console.Error.WriteLine(error);  // the stack goes to the host's log file
}
finally
{
    NativeMusicOutput.Shutdown();  // the audio thread ends before Mono does
    backend.Dispose();
    if (runner is not null)
        foreach (var (page, open) in runner.Frames.Opens)
        {
            results.Write($"open.{page}", string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"count {open.Count} avg_ms {open.AverageMs:0} worst_ms {open.WorstMs:0}"));
            results.Write($"open.{page}.parts_ms", PartsText(open.AverageParts));
        }
    if (runner is not null)
        foreach (var (page, frames) in runner.Frames.Pages)
        {
            results.Write($"frame.{page}.frames", frames.Frames.ToString(System.Globalization.CultureInfo.InvariantCulture));
            results.Write($"frame.{page}.avg_ms", frames.AverageMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            results.Write($"frame.{page}.worst_ms", frames.WorstMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            results.Write($"frame.{page}.small_frames", frames.SmallFrames.ToString(System.Globalization.CultureInfo.InvariantCulture));
            results.Write($"frame.{page}.small_avg_ms", frames.SmallAverageMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            results.Write($"frame.{page}.parts_ms", PartsText(frames.AverageParts));
            if (runner.Profiles.TryGetValue(page, out var profile))
            {
                results.Write($"frame.{page}.draws", profile.DrawSummary(14));
                results.Write($"frame.{page}.dirt", profile.DirtSummary(8));
            }
        }
    var (wait, copy, queue) = NativeVideo.PresentTimes();
    results.Write("present.parts_ms", string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"wait {wait:0.0} copy {copy:0.0} queue {queue:0.0}"));
    results.Write("ui_errors", errors.ToString(System.Globalization.CultureInfo.InvariantCulture));
    NativeVideo.Close();
}
return 0;

static string PartsText(FrameParts parts) => string.Create(System.Globalization.CultureInfo.InvariantCulture,
    $"loop {parts.LoopMs:0.0} layout {parts.LayoutMs:0.0} draw {parts.RenderMs:0.0} (paint {parts.PaintMs:0.0}) present {parts.PresentMs:0.0}");

// The frame is copied at once and encoded off the UI thread: encoding 1080p on the console's CPU takes a while.
static void SaveScreen(string page, SKPixmap frame)
{
    var copy = new SKBitmap(frame.Info);
    frame.ReadPixels(copy.Info, copy.GetPixels(), copy.RowBytes, 0, 0);
    _ = Task.Run(() =>
    {
        using (copy)
        using (var file = File.Create($"{Logs}/screen-{page}.png"))
            copy.Encode(file, SKEncodedImageFormat.Png, 100);
    });
}
