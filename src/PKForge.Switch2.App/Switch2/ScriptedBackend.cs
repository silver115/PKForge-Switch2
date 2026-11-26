using Microsoft.Maui.Graphics;
using PKForge.MauiLite;
using SkiaSharp;

namespace PKForge.Switch2;

/// <summary>
/// Input from a test script instead of a person: <see cref="ScriptRunner"/> gives the touches and held buttons, and the
/// last frame is kept for its shots. Screen, time and pacing are the wrapped backend's: the console's, or the headless
/// host's screen in memory.
/// </summary>
public sealed class ScriptedBackend(IHostBackend inner) : IHostBackend
{
    readonly Queue<PointerEvent> _pointers = new();

    public SKSizeI PixelSize => inner.PixelSize;
    public float Scale => inner.Scale;
    public TimeSpan Now => inner.Now;
    public bool QuitRequested => inner.QuitRequested;

    /// <summary>The npad buttons held now.</summary>
    public ulong Held { get; set; }

    /// <summary>The last frame presented, or null before the first.</summary>
    public SKBitmap? LastFrame { get; private set; }

    public void Touch(PointerAction action, double x, double y) => _pointers.Enqueue(new PointerEvent(action, 1, new Point(x, y)));

    public IReadOnlyList<PointerEvent> PollPointers()
    {
        inner.PollPointers();  // still polled: the console reads all of its input, and the HOME button, in this call
        var events = _pointers.ToArray();
        _pointers.Clear();
        return events;
    }

    public PadState PollPad()
    {
        inner.PollPad();
        return new(Held, 0, 0, 0, 0);
    }

    public void Present(SKPixmap frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        LastFrame ??= new SKBitmap(frame.Info);
        frame.ReadPixels(LastFrame.Info, LastFrame.GetPixels(), LastFrame.RowBytes, 0, 0);
        inner.Present(frame);
    }

    public void WaitForNextFrame() => inner.WaitForNextFrame();
}
