using SkiaSharp;

namespace PKForge.MauiLite.Rendering;

/// <summary>The pixel rectangles a frame must draw again, merged where they overlap.</summary>
sealed class DirtyRegion
{
    // Past this many separate areas, one rectangle around them all is cheaper than many clipped passes.
    const int MaxAreas = 8;

    readonly List<SKRectI> _areas = [];

    public void Add(SKRectI area)
    {
        if (area.IsEmpty)
            return;
        _areas.Add(area);
    }

    public void Clear() => _areas.Clear();

    public IReadOnlyList<SKRectI> Rectangles(SKRectI screen)
    {
        var merged = new List<SKRectI>();
        foreach (var area in _areas)
        {
            var clipped = SKRectI.Intersect(area, screen);
            if (!clipped.IsEmpty)
                merged.Add(clipped);
        }
        // Merge any two that overlap until none do.
        for (var changed = true; changed;)
        {
            changed = false;
            for (var i = 0; i < merged.Count && !changed; i++)
                for (var j = i + 1; j < merged.Count; j++)
                    if (merged[i].IntersectsWith(merged[j]) || merged[i].Contains(merged[j]))
                    {
                        merged[i] = SKRectI.Union(merged[i], merged[j]);
                        merged.RemoveAt(j);
                        changed = true;
                        break;
                    }
        }
        if (merged.Count > MaxAreas)
            merged = [merged.Aggregate(SKRectI.Union)];
        return merged;
    }
}
