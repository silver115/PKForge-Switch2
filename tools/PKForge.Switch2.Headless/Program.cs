using PKForge.MauiLite;
using PKForge.Switch2;
using PKForge.Switch2.Headless;
using PKForge.Switch2.Platform;

// Usage: --saves <dir> --data <dir> --script <file> --out <dir> [--seed] [--seed-restore-points] [--profile]
// --seed-restore-points leaves the flag file the console takes (AppHost.SeedFlagName), so both hosts make the points.
// --profile prints, per page, frame times and where drawing time went.
// Exit: 0 when the script passed with no UI errors; 1 when a script line failed; 2 when app code reported errors.
var options = args.Select((value, index) => (value, index)).Where(a => a.value.StartsWith("--", StringComparison.Ordinal))
    .ToDictionary(a => a.value[2..], a => a.index + 1 < args.Length && !args[a.index + 1].StartsWith("--", StringComparison.Ordinal) ? args[a.index + 1] : "");
string Required(string name) => options.TryGetValue(name, out var value) && value.Length > 0
    ? value
    : throw new ArgumentException($"--{name} <value> is required.");

// Full paths, as Android's app folders always are: PKForge treats a relative image path as a package file.
var saves = Path.GetFullPath(Required("saves"));
var data = Path.GetFullPath(Required("data"));
if (options.ContainsKey("seed"))
    SaveSeeder.Seed(saves);
Directory.CreateDirectory(saves);

var errors = new List<string>();
UiErrors.Reported += (error, where) =>
{
    errors.Add(where);
    Console.Error.WriteLine($"UI error in {where}: {error}");
};

if (options.ContainsKey("seed-restore-points"))
{
    Directory.CreateDirectory(data);
    File.WriteAllText(Path.Combine(data, AppHost.SeedFlagName), "");
}

var backend = new ScriptedBackend(new HeadlessBackend());
var paths = new LitePaths(Path.Combine(data, "data"), Path.Combine(data, "cache"), Path.Combine(data, "data", "preferences.json"));
var runner = AppHost.Start(backend, new FolderConsoleSaveHost(saves), paths,
    name => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "app", name)));
runner.Profiling = options.ContainsKey("profile");
var result = ScriptRunner.Run(runner, backend, File.ReadAllLines(Required("script")), Required("out"));
foreach (var (page, profile) in runner.Profiles)
{
    var frames = runner.Frames.Pages.GetValueOrDefault(page);
    Console.WriteLine($"{page}: {frames?.Frames} frames, {frames?.AverageMs:0.0} ms average ({frames?.AverageParts})");
    Console.WriteLine($"{page} draws: {profile.DrawSummary(14)}");
    Console.WriteLine($"{page} dirt: {profile.DirtSummary(8)}");
}
if (runner.Profiling)
    foreach (var (page, open) in runner.Frames.Opens)
        Console.WriteLine($"{page} opens: {open.Count}, {open.AverageMs:0} ms average, {open.WorstMs:0} ms worst ({open.AverageParts})");
if (result == 0 && errors.Count > 0)
{
    Console.Error.WriteLine($"{errors.Count} UI error(s): {string.Join(", ", errors.Distinct())}");
    return 2;
}
return result;
