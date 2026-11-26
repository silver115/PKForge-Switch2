using System.Diagnostics;
using PKForge.Switch2.Platform;
using SkiaSharp;

namespace PKForge.Switch2.Spike;

/// <param name="AverageDrawMs">Time in the draw callback, per frame.</param>
/// <param name="AveragePresentMs">Time getting the frame on screen (the copy and waiting for a buffer), per frame.</param>
/// <param name="RawMaxX">The largest raw touch X seen (calibrates the touch range).</param>
public sealed record FrameStats(int Frames, double AverageFps, double WorstFrameMs, double AverageDrawMs, double AveragePresentMs,
    int TouchEvents, int RawMaxX, int RawMaxY);

/// <summary>Draws frames on the screen the host opened: each frame polls input, draws with the last touch point
/// (null before the first touch) into the loop's own surface, and copies it to the screen. Frames are drawn in normal
/// memory: the Switch 2's framebuffer is very slow to read, so drawing there (which blends against what is there)
/// is slower. Presenting waits for the display, so the loop runs at its pace.</summary>
public sealed class FrameLoop : IDisposable
{
    private readonly SKSurface _surface;
    private readonly SKImageInfo _info;

    public FrameLoop(int width, int height)
    {
        _info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(_info) ?? throw new InvalidOperationException("Could not make a raster surface");
        NativeVideo.Open(width, height);
    }

    public SKPoint? Cursor { get; private set; }

    /// <summary>Draws one frame and shows it.</summary>
    public void Show(Action<SKCanvas, SKPoint?> draw)
    {
        draw(_surface.Canvas, Cursor);
        _surface.Canvas.Flush();
        Present();
    }

    /// <summary>The last frame drawn, as PNG.</summary>
    public SKData EncodePng()
    {
        using var image = _surface.Snapshot();
        return image.Encode(SKEncodedImageFormat.Png, 100);
    }

    /// <summary>Runs frames for <paramref name="duration"/>, or until + is pressed. With more than one core in
    /// <paramref name="cores"/>, each frame is drawn in that many horizontal bands at once, one per core.</summary>
    public FrameStats Run(TimeSpan duration, Action<SKCanvas, SKPoint?> draw, int[]? cores = null)
    {
        var frames = 0;
        var touches = 0;
        var worst = TimeSpan.Zero;
        TimeSpan drawing = TimeSpan.Zero, presenting = TimeSpan.Zero;
        var input = default(HostInput);
        var total = Stopwatch.StartNew();
        var clock = new Stopwatch();
        while (total.Elapsed < duration)
        {
            var frame = Stopwatch.StartNew();
            input = NativeVideo.Poll();
            if (input.Quit != 0) break;
            if (input.TouchCount > 0)
            {
                touches++;
                Cursor = new SKPoint(input.TouchX, input.TouchY);
            }
            clock.Restart();
            if (cores is { Length: > 1 })
                DrawInBands(draw, cores);
            else
                draw(_surface.Canvas, Cursor);
            _surface.Canvas.Flush();
            drawing += clock.Elapsed;
            clock.Restart();
            Present();
            presenting += clock.Elapsed;
            frames++;
            if (frame.Elapsed > worst) worst = frame.Elapsed;
        }
        var seconds = total.Elapsed.TotalSeconds;
        double PerFrame(TimeSpan t) => frames > 0 ? t.TotalMilliseconds / frames : 0;
        return new FrameStats(frames, seconds > 0 ? frames / seconds : 0, worst.TotalMilliseconds, PerFrame(drawing),
            PerFrame(presenting), touches, input.RawMaxX, input.RawMaxY);
    }

    // Each band is its own surface over a slice of the frame's pixels, drawn on a thread pinned to its own core.
    private void DrawInBands(Action<SKCanvas, SKPoint?> draw, int[] cores)
    {
        using var pixmap = _surface.PeekPixels();
        var pixels = pixmap.GetPixels();
        var rowBytes = pixmap.RowBytes;
        var bandHeight = (_info.Height + cores.Length - 1) / cores.Length;
        var cursor = Cursor;
        Parallel.For(0, cores.Length, new ParallelOptions { MaxDegreeOfParallelism = cores.Length }, band =>
        {
            NativeSystem.PinThread(cores[band]);
            var top = band * bandHeight;
            var rows = Math.Min(bandHeight, _info.Height - top);
            var info = new SKImageInfo(_info.Width, rows, _info.ColorType, _info.AlphaType);
            using var surface = SKSurface.Create(info, pixels + top * rowBytes, rowBytes);
            surface.Canvas.Translate(0, -top);
            draw(surface.Canvas, cursor);
            surface.Canvas.Flush();
        });
    }

    private void Present()
    {
        using var pixmap = _surface.PeekPixels();
        NativeVideo.Present(pixmap.GetPixels(), pixmap.RowBytes);
    }

    public void Dispose()
    {
        NativeVideo.Close();
        _surface.Dispose();
    }
}
