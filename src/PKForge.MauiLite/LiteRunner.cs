using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using SkiaSharp;

namespace PKForge.MauiLite;

/// <summary>
/// Runs the app one frame at a time: touch, then the controller, then posted work and timers, then layout, then drawing.
/// A frame that changed nothing presents nothing and waits for the next display refresh.
/// </summary>
public sealed class LiteRunner
{
    readonly IHostBackend _backend;
    readonly Application _application;
    readonly Window _window;
    readonly SKSurface _frame;
    readonly Renderer _renderer;
    readonly GestureDispatcher _input = new();
    VisualElement? _lastRoot;
    bool _quit;
    string _shownPage = "";
    long? _openingSince;
    FrameParts _openingParts;
    // The opening up to its last slow frame: a page that goes still before a smooth one opened by then.
    (double Ms, FrameParts Parts)? _openingDrawn;

    /// <summary>A page is open once a frame of it draws in this time (30 fps); its slower frames before then count as opening.</summary>
    const double SmoothMs = 1000.0 / 30;

    /// <summary>A page that stays slow this long counts as open anyway, and its frames count again.</summary>
    const double MaxOpenMs = 2000;

    public LiteRunner(MauiApp app, IHostBackend backend)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _renderer = new Renderer(backend.Scale);
        Window.ScreenUnits = new Microsoft.Maui.Graphics.Size((double)backend.PixelSize.Width / backend.Scale, (double)backend.PixelSize.Height / backend.Scale);
        UiLoop.Current.Attach();
        _application = (Application)app.Services.GetRequiredService<IApplication>();
        _application.QuitRequested += (_, _) => _quit = true;
        _window = _application.OpenWindow();
        // Drawn in ordinary memory and handed over whole: the console's framebuffer is slow to read.
        _frame = SKSurface.Create(new SKImageInfo(backend.PixelSize.Width, backend.PixelSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
    }

    public Action<PadState, TimeSpan>? Pad { get; set; }

    public FrameLog Frames { get; } = new();

    readonly Dictionary<string, DrawProfile> _profiles = [];

    /// <summary>When true, each page's drawing is profiled into <see cref="Profiles"/> (it reads the clock per element).</summary>
    public bool Profiling { get; set; }

    public IReadOnlyDictionary<string, DrawProfile> Profiles => _profiles;

    /// <summary>When set, frames draw in bands on these threads (the app's PaintSurface handlers stay on this one).</summary>
    public DrawBands? Bands { get => _renderer.Bands; set => _renderer.Bands = value; }

    DrawProfile ProfileFor(string page) => _profiles.TryGetValue(page, out var profile) ? profile : _profiles[page] = new DrawProfile();

    public event Action<string, SKPixmap>? Presented;

    /// <summary>The page on top: a navigation page's current page (modal first), or the window's page.</summary>
    public Page? CurrentPage => _window.Page is NavigationPage navigation ? navigation.CurrentPage : _window.Page;

    public string CurrentPageName => CurrentPage?.GetType().Name ?? "";

    internal Window Window => _window;

    public void RunFrame()
    {
        var started = Stopwatch.GetTimestamp();
        var clock = Stopwatch.StartNew();
        var now = _backend.Now;
        var root = _window.Page;
        // Polled every frame, page or not: the console reads all of its input in this one call.
        var pointers = _backend.PollPointers();
        if (root is not null)
            foreach (var pointer in pointers)
                _input.Handle(root, pointer, now);
        var pad = _backend.PollPad();
        if (Pad is { } handler)
        {
            try
            {
                handler(pad, now);
            }
            catch (Exception error)
            {
                UiErrors.Report(error, "controller");
            }
        }
        UiLoop.Current.RunOnce(now);
        var loopMs = clock.Elapsed.TotalMilliseconds;
        root = _window.Page;
        if (root is null)
        {
            _backend.WaitForNextFrame();
            return;
        }
        LayoutPass.Run(root, Window.ScreenUnits);
        var layoutMs = clock.Elapsed.TotalMilliseconds - loopMs;
        var full = !ReferenceEquals(root, _lastRoot);
        _lastRoot = root;
        _renderer.Profile = Profiling ? ProfileFor(CurrentPageName) : null;
        PaintClock.TakeMs();
        var drawn = _renderer.Render(root, _frame, full);
        var renderMs = clock.Elapsed.TotalMilliseconds - loopMs - layoutMs;
        var paintMs = PaintClock.TakeMs();
        if (drawn.Count == 0)
        {
            // An opening page that drew nothing (waiting for its data) still spent this frame's work.
            if (_openingSince is not null)
                _openingParts += new FrameParts(loopMs, layoutMs, renderMs, 0, paintMs);
            _backend.WaitForNextFrame();
            return;
        }
        _frame.Canvas.Flush();
        using var pixels = _frame.PeekPixels();
        var beforePresent = clock.Elapsed.TotalMilliseconds;
        _backend.Present(pixels);
        var presentMs = clock.Elapsed.TotalMilliseconds - beforePresent;
        var page = CurrentPageName;
        Presented?.Invoke(page, pixels);
        var frameMs = clock.Elapsed.TotalMilliseconds;
        var parts = new FrameParts(loopMs, layoutMs, renderMs, presentMs, paintMs);
        if (page != _shownPage)
        {
            if (_openingDrawn is { } still)
                Frames.RecordOpen(_shownPage, still.Ms, still.Parts);
            _shownPage = page;
            _openingSince = started;
            _openingParts = default;
            _openingDrawn = null;
        }
        if (_openingSince is { } since)
        {
            // Opening a page (building it, loading its lists, first paints) is timed apart from its frame rate.
            var openMs = Stopwatch.GetElapsedTime(since).TotalMilliseconds;
            _openingParts += parts;
            if (frameMs > SmoothMs && openMs < MaxOpenMs)
            {
                _openingDrawn = (openMs, _openingParts);
                return;
            }
            // Past the limit, a page that went still opened by its last drawn frame, not by this later one.
            var (ms, sum) = openMs >= MaxOpenMs && _openingDrawn is { } last ? last : (openMs, _openingParts);
            Frames.RecordOpen(page, ms, sum);
            _openingSince = null;
            _openingDrawn = null;
        }
        var area = (double)_backend.PixelSize.Width * _backend.PixelSize.Height;
        Frames.Record(page, frameMs, drawn.Sum(r => (double)r.Width * r.Height) / area, parts);
    }

    public void Run()
    {
        while (!_quit && !_backend.QuitRequested)
            RunFrame();
    }
}

/// <summary>
/// Where a frame's time went: input, controller and the UI loop's work; layout; drawing; presenting. PaintMs is the
/// part of drawing spent in the app's PaintSurface handlers; the rest of RenderMs is composition.
/// </summary>
public readonly record struct FrameParts(double LoopMs, double LayoutMs, double RenderMs, double PresentMs, double PaintMs = 0)
{
    public static FrameParts operator +(FrameParts a, FrameParts b) => new(a.LoopMs + b.LoopMs, a.LayoutMs + b.LayoutMs,
        a.RenderMs + b.RenderMs, a.PresentMs + b.PresentMs, a.PaintMs + b.PaintMs);

    public static FrameParts operator /(FrameParts parts, int count) => new(parts.LoopMs / count, parts.LayoutMs / count,
        parts.RenderMs / count, parts.PresentMs / count, parts.PaintMs / count);
}

/// <summary>A page's openings; each one's parts are summed over its frames, then averaged over the openings.</summary>
public sealed record PageOpens(int Count, double AverageMs, double WorstMs, FrameParts AverageParts);

/// <summary>Frame times per page, for frames that drew something, with the average of each part.</summary>
public sealed record PageFrames(int Frames, double AverageMs, double WorstMs, int SmallFrames, double SmallAverageMs, FrameParts AverageParts);

public sealed class FrameLog
{
    /// <summary>A frame that redrew less than this share of the screen counts as a small change.</summary>
    const double SmallShare = 0.10;

    readonly Dictionary<string, (int Frames, double Total, double Worst, int Small, double SmallTotal, FrameParts Parts)> _pages = [];
    readonly Dictionary<string, (int Count, double Total, double Worst, FrameParts Parts)> _opens = [];

    /// <summary>
    /// Page open times: from the start of the frame that showed the page to the end of its first frame drawn at
    /// 30 fps or better, or, for a page that went still before one, of its last frame drawn (recorded when the next
    /// page shows, or once the page draws again past the 2 s limit). Frames before then are not in <see cref="Pages"/>.
    /// </summary>
    public IReadOnlyDictionary<string, PageOpens> Opens => _opens.ToDictionary(open => open.Key,
        open => new PageOpens(open.Value.Count, open.Value.Total / open.Value.Count, open.Value.Worst, open.Value.Parts / open.Value.Count));

    internal void RecordOpen(string page, double milliseconds, FrameParts parts)
    {
        var (count, total, worst, sum) = _opens.GetValueOrDefault(page);
        _opens[page] = (count + 1, total + milliseconds, Math.Max(worst, milliseconds), sum + parts);
    }

    public IReadOnlyDictionary<string, PageFrames> Pages => _pages.ToDictionary(page => page.Key, page =>
    {
        var (frames, total, worst, small, smallTotal, parts) = page.Value;
        return new PageFrames(frames, total / frames, worst, small, small == 0 ? 0 : smallTotal / small, parts / frames);
    });

    internal void Record(string page, double milliseconds, double share, FrameParts parts)
    {
        var (frames, total, worst, small, smallTotal, sum) = _pages.GetValueOrDefault(page);
        var isSmall = share < SmallShare;
        _pages[page] = (frames + 1, total + milliseconds, Math.Max(worst, milliseconds), small + (isSmall ? 1 : 0), smallTotal + (isSmall ? milliseconds : 0),
            sum + parts);
    }
}
