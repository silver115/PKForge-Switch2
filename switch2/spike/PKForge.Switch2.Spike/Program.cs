using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using PKForge.Domain;
using PKForge.Engine;
using PKForge.Switch2.Platform;
using PKForge.Switch2.Spike;
using SkiaSharp;

// The phase 0 spike (plan Task 6 Step 5). It opens the console's Legends: Z-A save read-only, times opening it and
// legality checks, and writes key=value lines to results-<time>.txt in the logs folder as each check ends.
// A check that throws writes <check>.error, and the run goes on with the checks that do not depend on it.
// args[0] is the .nro's path, which the host passes.

// The libnx CoreLib treats "sdmc:/" as a rooted path. A bare "/" would mean romfs, the host's working directory.
using var results = new ResultsWriter("sdmc:/switch/PKForge/logs", Console.Out);
var invariant = CultureInfo.InvariantCulture;

Check("env", () =>
{
    results.Write("env.runtime", RuntimeInformation.FrameworkDescription);
    results.Write("env.aot_mode", Environment.GetEnvironmentVariable("PKF_AOT_MODE") ?? "unset");
    var (firmware, protocol) = NativeSystem.Versions();
    results.Write("env.firmware", firmware);
    results.Write("env.kaihen_protocol", protocol.ToString(invariant));
});

var engine = new SaveEngine();
NativeConsoleSaveHost? host = null;
var perUser = new List<CatalogResult>();
Check("saves", () =>
{
    var saves = host = new NativeConsoleSaveHost();
    var catalog = new ConsoleSaveCatalog(saves, engine);
    var users = saves.ListUsers();
    results.Write("users.count", users.Count.ToString(invariant));
    var skipped = new SortedSet<ulong>();
    foreach (var user in users)
    {
        var scan = catalog.Scan(user);
        perUser.Add(scan);
        foreach (var save in scan.Saves)
            results.Write($"saves.{save.DocumentId}", $"{save.GameName}|{save.Description.TrainerName}|{save.Description.PlayTime}");
        foreach (var rejection in scan.Rejections)
            results.Write($"saves.rejected.{rejection.DocumentId}", rejection.Reason);
        skipped.UnionWith(scan.SkippedTitleIds);
    }
    results.Write("saves.skipped", string.Join(',', skipped.Select(id => id.ToString("X16", invariant))));
});

// The ready point: the first frame of the scene is on screen. The art is in romfs, the host's working directory.
FrameLoop? screen = null;
Action<SKCanvas, SKPoint?>? drawScene = null;
Check("frame", () =>
{
    var (art, font) = SpikeScene.LoadAssets("/art/pksm", "/art/sprites", "/art/NDS12.ttf");
    drawScene = (canvas, cursor) => SpikeScene.Draw(canvas, art, font, cursor);
    screen = new FrameLoop(SpikeScene.Width, SpikeScene.Height);
    screen.Show(drawScene);
});
Check("coldstart", () => results.Write("coldstart.main_to_ready_ms", Ms(NativeSystem.SinceMain())));
if (screen is { } shown)
    Check("frame_png", () =>
    {
        using var png = shown.EncodePng();
        File.WriteAllBytes("sdmc:/switch/PKForge/logs/frame.png", png.ToArray());
    });

TimeSpan? openSave = null, legalityWorst = null, legalityBox = null;
double? freeFraction = null;
long? nroBytes = null;
ISaveEngineSession? opened = null;
if (SavePicker.Pick(perUser) is not { } picked || host is not { } reader)
    results.Write("saves.none", "true");
else
    Check("open_save", () =>
    {
        results.Write("open_save.document", picked.DocumentId);
        var clock = Stopwatch.StartNew();
        var bytes = reader.ReadFile(picked.TitleId, picked.User, picked.File);
        opened = engine.OpenSession(bytes, picked.GameName);
        var elapsed = clock.Elapsed;
        openSave = elapsed;
        results.Write("open_save_ms", Ms(elapsed));
    });

// The box lines come after the timings, so the warm-up check runs first and their legal= is the timed verdict.
var verdicts = new Dictionary<(int Box, int Slot), bool>();
IReadOnlyList<(int Box, int Slot)> plan = [];
if (opened is { } session)
{
    Check("legality", () =>
    {
        var snapshot = Stopwatch.StartNew();
        plan = LegalityPlan.For(session.Snapshot.Slots);
        results.Write("open_save.snapshot_ms", Ms(snapshot.Elapsed));
        if (plan.Count == 0)
        {
            results.Write("box.none", "true");
            return;
        }
        var legality = new LegalityService();
        var warmup = Stopwatch.StartNew();
        legality.Analyze(session, plan[0].Box, plan[0].Slot);
        results.Write("legality.warmup_ms", Ms(warmup.Elapsed));
        var worst = TimeSpan.Zero;
        var total = Stopwatch.StartNew();
        foreach (var (box, slot) in plan)
        {
            var one = Stopwatch.StartNew();
            verdicts[(box, slot)] = legality.Analyze(session, box, slot).Valid;
            if (one.Elapsed > worst) worst = one.Elapsed;
        }
        var all = total.Elapsed;
        (legalityWorst, legalityBox) = (worst, all);
        results.Write("legality.one_worst_ms", Ms(worst));
        results.Write("legality.box_total_ms", Ms(all));
    });
    Check("box", () =>
    {
        foreach (var (box, slot) in plan.Distinct())
        {
            var detail = session.ReadEntity(box, slot);
            var legal = verdicts.TryGetValue((box, slot), out var valid) ? (valid ? "true" : "false") : "unknown";
            results.Write($"box.{box}.slot.{slot}", $"{detail.SpeciesName}|{detail.Nickname}|legal={legal}");
        }
    });
}

// Read with the save still open: the gate's free-memory row is "with that save open".
Check("memory", () =>
{
    var memory = NativeSystem.Memory();
    freeFraction = memory.FreeFraction;
    results.Write("memory.heap_total_mb", Mb(memory.HeapTotal));
    results.Write("memory.malloc_used_mb", Mb(memory.MallocUsed));
    results.Write("memory.mono_used_mb", Mb(memory.MonoUsed));
    results.Write("memory.free_fraction", memory.FreeFraction.ToString("0.000", invariant));
});

Check("nro", () =>
{
    var length = new FileInfo(args[0]).Length;
    nroBytes = length;
    results.Write("nro_bytes", length.ToString(invariant));
});

// About eight seconds of frames; the cursor follows the finger, and + ends a run early. Run 2 split a frame: a full
// 1080p redraw takes 46 ms on one core and presenting 11-17 ms (60 fps with no drawing), and the framebuffer is very
// slow to read. So: the full scene on one core; the full scene in bands, one per core the process may use; and the
// scene drawn once and then only copied (Src, no blending) with the cursor on top, as a static screen would be.
// The gate's frame-rate row takes the better full redraw.
double? averageFps = null;
if (screen is { } frames && drawScene is { } draw)
    Check("frame_loop", () =>
    {
        var mask = NativeSystem.CoreMask();
        var cores = Enumerable.Range(0, 64).Where(core => (mask >> core & 1) != 0).ToArray();  // every core we may use
        results.Write("frame.core_mask", $"0x{mask:X}");
        using var still = SKSurface.Create(new SKImageInfo(SpikeScene.Width, SpikeScene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        draw(still.Canvas, null);
        using var scene = still.Snapshot();
        using var copy = new SKPaint { BlendMode = SKBlendMode.Src };
        (string Name, Action<SKCanvas, SKPoint?> Draw, int[]? Cores, double Seconds)[] runs =
        [
            ("full", draw, null, 2),
            ("full_bands", draw, cores, 3),
            ("cached", (canvas, cursor) => { canvas.DrawImage(scene, 0, 0, copy); SpikeScene.DrawCursor(canvas, cursor); }, null, 3),
        ];
        var touches = 0;
        var rawMax = "";
        foreach (var run in runs)
        {
            var stats = frames.Run(TimeSpan.FromSeconds(run.Seconds), run.Draw, run.Cores);
            results.Write($"frame.{run.Name}.fps", stats.AverageFps.ToString("0.0", invariant));
            results.Write($"frame.{run.Name}.draw_ms", stats.AverageDrawMs.ToString("0.0", invariant));
            results.Write($"frame.{run.Name}.present_ms", stats.AveragePresentMs.ToString("0.0", invariant));
            results.Write($"frame.{run.Name}.worst_ms", stats.WorstFrameMs.ToString("0.0", invariant));
            if (run.Name.StartsWith("full", StringComparison.Ordinal))
                averageFps = Math.Max(averageFps ?? 0, stats.AverageFps);
            touches += stats.TouchEvents;
            rawMax = $"{stats.RawMaxX},{stats.RawMaxY}";
        }
        results.Write("frame.bands", cores.Length.ToString(invariant));
        results.Write("touch.events", touches.ToString(invariant));
        results.Write("touch.raw_max", rawMax);
    });
screen?.Dispose();

// Cold start stays "not measured" here: Task 8 adds the stopwatch time.
var verdict = GateLimits.Evaluate(new GateMeasurements(null, openSave, legalityWorst, legalityBox, averageFps, freeFraction, nroBytes));
for (var i = 0; i < verdict.Rows.Count; i++)
{
    var row = verdict.Rows[i];
    var outcome = row.Passed switch { true => "pass", false => "fail", null => "not measured" };
    results.Write($"gate.{i + 1}", $"{row.Measure}|{row.Limit}|{row.Value ?? "-"}|{outcome}");
}
results.Write("gate.passed", verdict.Passed ? "true" : "false");
opened?.Dispose();
return 0;

void Check(string name, Action run)
{
    try
    {
        run();
    }
    catch (Exception error)
    {
        results.Write($"{name}.error", $"{error.GetType().Name}: {error.Message.ReplaceLineEndings(" ")}");
        Console.Error.WriteLine(error);  // the stack goes to the host's log file
    }
}

string Ms(TimeSpan time) => Math.Round(time.TotalMilliseconds).ToString(invariant);

string Mb(ulong bytes) => (bytes / (1024 * 1024)).ToString(invariant);
