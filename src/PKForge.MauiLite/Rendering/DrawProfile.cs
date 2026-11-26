using System.Diagnostics;
using System.Globalization;
using SkiaSharp;

namespace PKForge.MauiLite;

/// <summary>
/// Where one page's drawing time goes, by element type, and which element types mark areas for redraw.
/// Opt-in (<see cref="LiteRunner.Profiling"/>): it reads the clock around every element drawn.
/// </summary>
public sealed class DrawProfile
{
    readonly Dictionary<string, (int Count, long Ticks)> _draws = [];
    readonly Dictionary<string, (int Count, long Pixels)> _dirt = [];
    // Bands of one frame draw on several threads at once (DrawBands).
    readonly Lock _gate = new();

    internal static long Now => Stopwatch.GetTimestamp();

    internal void Drew(string part, long since)
    {
        var elapsed = Stopwatch.GetTimestamp() - since;
        lock (_gate)
        {
            var (count, ticks) = _draws.GetValueOrDefault(part);
            _draws[part] = (count + 1, ticks + elapsed);
        }
    }

    internal void Dirtied(string type, SKRectI before, SKRectI now)
    {
        var (count, pixels) = _dirt.GetValueOrDefault(type);
        _dirt[type] = (count + 1, pixels + (long)before.Width * before.Height + (long)now.Width * now.Height);
    }

    /// <summary>The parts that took longest: an element type's own drawing, or its "shadow", "clip" or "layer".</summary>
    public IEnumerable<(string Part, int Count, double Ms)> SlowestDraws(int count) => _draws
        .OrderByDescending(draw => draw.Value.Ticks).Take(count)
        .Select(draw => (draw.Key, draw.Value.Count, draw.Value.Ticks * 1000.0 / Stopwatch.Frequency));

    /// <summary>For logs: "Label 40.1ms/300, shadow Border 20.3ms/80": total time and how many times.</summary>
    public string DrawSummary(int count) => string.Join(", ", SlowestDraws(count).Select(draw =>
        string.Create(CultureInfo.InvariantCulture, $"{draw.Part} {draw.Ms:0.0}ms/{draw.Count}")));

    /// <summary>For logs: "Image 12.4Mpx/30": pixels marked for redraw and how many times.</summary>
    public string DirtSummary(int count) => string.Join(", ", BiggestDirt(count).Select(dirt =>
        string.Create(CultureInfo.InvariantCulture, $"{dirt.Type} {dirt.Pixels / 1e6:0.0}Mpx/{dirt.Count}")));

    /// <summary>The element types that marked the most pixels for redraw (old and new rectangles), and how often.</summary>
    public IEnumerable<(string Type, int Count, long Pixels)> BiggestDirt(int count) => _dirt
        .OrderByDescending(dirt => dirt.Value.Pixels).Take(count)
        .Select(dirt => (dirt.Key, dirt.Value.Count, dirt.Value.Pixels));
}
