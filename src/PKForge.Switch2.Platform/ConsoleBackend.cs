using System.Diagnostics;
using Microsoft.Maui.Graphics;
using PKForge.MauiLite;
using SkiaSharp;

namespace PKForge.Switch2.Platform;

/// <summary>
/// The console's one touch point as pointer events. libnx reports touch in 1280x720; MauiLite's window is the
/// 1920x1080 screen at the Thor's 2.25 pixels per unit, 853⅓ by 480 units, so a raw point is two thirds of a unit point.
/// </summary>
public sealed class TouchTracker
{
    // Divided last, so whole raw points land on whole unit points where they can (300 raw is exactly 200 units).
    static double ToUnits(int raw) => raw * 1920.0 / (1280 * 2.25);
    const double MaxX = 1920.0 / 2.25 - 0.01, MaxY = 480 - 0.01;

    long _id;
    bool _down;
    Point _last;

    public IReadOnlyList<PointerEvent> Update(int touchCount, int rawX, int rawY)
    {
        if (touchCount <= 0)
        {
            if (!_down)
                return [];
            _down = false;
            return [new PointerEvent(PointerAction.Released, _id, _last)];
        }
        var at = new Point(Math.Clamp(ToUnits(rawX), 0, MaxX), Math.Clamp(ToUnits(rawY), 0, MaxY));
        if (!_down)
        {
            _down = true;
            _id++;
            _last = at;
            return [new PointerEvent(PointerAction.Pressed, _id, at)];
        }
        if (at == _last)
            return [];
        _last = at;
        return [new PointerEvent(PointerAction.Moved, _id, at)];
    }
}

/// <summary>MauiLite on the Switch 2: the host's linear framebuffer, the touch screen and the controller.</summary>
public sealed class ConsoleBackend : IHostBackend, IDisposable
{
    static readonly double FrameMs = 1000.0 / 60;

    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly TouchTracker _touch = new();
    HostInput _input;

    // framebufferEnd and the display refresh take about 16 ms a frame: a thread of their own takes them off the UI thread.
    readonly FramePresenter _presenter = new(new SKImageInfo(1920, 1080, SKColorType.Rgba8888, SKAlphaType.Premul),
        frame => NativeVideo.Present(frame.GetPixels(), frame.RowBytes));

    public SKSizeI PixelSize => new(1920, 1080);

    /// <summary>The AYN Thor's Android density: PKForge's layouts were tuned on its 1920×1080 screen at 2.25.</summary>
    public float Scale => 2.25f;

    public TimeSpan Now => _clock.Elapsed;
    public bool QuitRequested => _input.AppletQuit != 0;

    /// <summary>Polls the host once per frame (the runner asks for pointers first) and returns the touch changes.</summary>
    public IReadOnlyList<PointerEvent> PollPointers()
    {
        _input = NativeVideo.Poll();
        return _touch.Update(_input.TouchCount, _input.RawX, _input.RawY);
    }

    // libnx's sticks are up-positive; PKForge reads Android's, which are down-positive.
    public PadState PollPad() => new(_input.ButtonsHeld, Axis(_input.StickLX), -Axis(_input.StickLY), Axis(_input.StickRX), -Axis(_input.StickRY));

    static float Axis(int value) => Math.Clamp(value / 32767f, -1f, 1f);

    public void Present(SKPixmap frame) => _presenter.Submit(frame);

    public void WaitForNextFrame()
    {
        var wait = FrameMs - _clock.Elapsed.TotalMilliseconds % FrameMs;
        Thread.Sleep(TimeSpan.FromMilliseconds(wait));
    }

    /// <summary>Stops presenting; the display may be closed after this.</summary>
    public void Dispose() => _presenter.Dispose();
}
