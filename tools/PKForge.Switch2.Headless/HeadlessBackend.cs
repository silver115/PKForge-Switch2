using System.Diagnostics;
using PKForge.MauiLite;
using SkiaSharp;

namespace PKForge.Switch2.Headless;

/// <summary>A screen in memory, with real time. Input and frames for <c>shot</c> come from ScriptedBackend, which wraps it.</summary>
public sealed class HeadlessBackend : IHostBackend
{
    static readonly double FrameMs = 1000.0 / 60;

    readonly Stopwatch _clock = Stopwatch.StartNew();

    public SKSizeI PixelSize => new(1920, 1080);
    public float Scale => 2.25f;  // the AYN Thor's density, as on the console
    public TimeSpan Now => _clock.Elapsed;
    public bool QuitRequested => false;

    public IReadOnlyList<PointerEvent> PollPointers() => [];

    public PadState PollPad() => default;

    public void Present(SKPixmap frame)
    {
    }

    // Paced like the console: an idle frame waits for the next 60 Hz tick.
    public void WaitForNextFrame()
    {
        var wait = FrameMs - _clock.Elapsed.TotalMilliseconds % FrameMs;
        Thread.Sleep(TimeSpan.FromMilliseconds(wait));
    }
}
