using System.Diagnostics;

namespace PKForge.MauiLite;

/// <summary>Time the app's PaintSurface handlers took since the runner last asked (UI thread only).</summary>
internal static class PaintClock
{
    static long _ticks;

    public static void Add(long ticks) => _ticks += ticks;

    public static double TakeMs()
    {
        var ms = _ticks * 1000.0 / Stopwatch.Frequency;
        _ticks = 0;
        return ms;
    }
}
