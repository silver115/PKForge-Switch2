using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;
using PKForge.MauiLite.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace PKForge.MauiLite;

/// <summary>
/// Draws a laid-out tree into a frame. Only the areas that changed since the last call are drawn again:
/// an element that repainted, moved, appeared or disappeared, clipped to its old and new pixel rectangles.
/// </summary>
public sealed class Renderer
{
    readonly float _scale;

    // Each element's blurred shadow, made once per shape, blur and colour: a blur is costly on the console's CPU.
    readonly ConditionalWeakTable<VisualElement, ShadowImage> _shadows = [];

    sealed class ShadowImage
    {
        public string Key = "";
        public SKImage? Image;
        public SKPoint Origin;
    }

    /// <summary>How many shadow images have been made (for tests).</summary>
    internal int ShadowsMade { get; private set; }

    public Renderer(float scale)
    {
        _scale = scale;
        Display.Density = scale;
    }

    /// <summary>When set, drawing records where its time goes into this profile.</summary>
    public DrawProfile? Profile { get; set; }

    /// <summary>When set, <see cref="Render(VisualElement, SKSurface, bool)"/> draws a frame in horizontal bands on these threads.</summary>
    public DrawBands? Bands { get; set; }

    /// <summary>How many strips of rows a banded frame is cut into per thread.</summary>
    internal const int StripsPerBand = 4;

    public IReadOnlyList<SKRectI> Render(VisualElement root, SKCanvas target, bool full = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        var areas = Prepare(root, target.DeviceClipBounds, full);
        foreach (var area in areas)
            DrawArea(root, target, area);
        return areas;
    }

    /// <summary>
    /// Draws into <paramref name="frame"/>'s pixels: in bands on <see cref="Bands"/> when set. Each band is its own
    /// surface over the whole frame, clipped to its rows, so device pixels stay window pixels (layer bounds use them).
    /// </summary>
    public IReadOnlyList<SKRectI> Render(VisualElement root, SKSurface frame, bool full = false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (Bands is not { } bands)
            return Render(root, frame.Canvas, full);
        using var pixmap = frame.PeekPixels();
        var info = pixmap.Info;
        var areas = Prepare(root, SKRectI.Create(info.Width, info.Height), full);
        if (areas.Count == 0)
            return areas;
        // Only the rows that changed are split, so a small change is spread over every core too. There are more
        // strips than threads, each thread taking the next one free: the dense rows of a screen do not hold up the frame.
        var strips = bands.Count * StripsPerBand;
        var top = areas.Min(area => area.Top);
        var bottom = areas.Max(area => area.Bottom);
        var rows = (bottom - top + strips - 1) / strips;
        var (pixels, rowBytes) = (pixmap.GetPixels(), pixmap.RowBytes);
        var next = -1;
        bands.Run(_ =>
        {
            using var surface = SKSurface.Create(info, pixels, rowBytes);
            for (int strip; (strip = Interlocked.Increment(ref next)) < strips;)
            {
                var stripTop = top + strip * rows;
                var slice = new SKRectI(0, stripTop, info.Width, Math.Min(stripTop + rows, bottom));
                foreach (var area in areas)
                    if (SKRectI.Intersect(area, slice) is { IsEmpty: false } part)
                        DrawArea(root, surface.Canvas, part);
            }
            surface.Canvas.Flush();
        });
        return areas;
    }

    /// <summary>On the UI thread: finds the areas to redraw and readies every element drawn in them.</summary>
    IReadOnlyList<SKRectI> Prepare(VisualElement root, SKRectI screen, bool full)
    {
        ArgumentNullException.ThrowIfNull(root);
        var dirty = new DirtyRegion();
        Collect(root, hidden: false, dirty, SKMatrix.Identity);
        if (full)
        {
            dirty.Clear();
            dirty.Add(screen);
        }
        var areas = dirty.Rectangles(screen);
        if (areas.Count > 0)
            Prepare(root, areas);
        return areas;
    }

    // The same elements Draw reaches: shown, and covering some area.
    void Prepare(VisualElement element, IReadOnlyList<SKRectI> areas)
    {
        if (!element.IsVisible || element.Opacity <= 0 || !areas.Any(element.SubtreeDrawn.IntersectsWith))
            return;
        if (areas.Any(area => SKRectI.Intersect(element.LastDrawn, area) is { IsEmpty: false }))
        {
            var bounds = element.WindowBounds;
            var size = new SKSize((float)bounds.Width * _scale, (float)bounds.Height * _scale);
            var start = Profile is null ? 0 : DrawProfile.Now;
            try
            {
                element.Prepare(size, _scale);
                if (element.Shadow is not null)
                    PrepareShadow(element, size);
            }
            catch (Exception error)
            {
                UiErrors.Report(error, $"prepare {element.GetType().Name}");
            }
            Profile?.Drew(element.PrepareName, start);
        }
        foreach (var child in element.VisualChildren)
            Prepare(child, areas);
    }

    void DrawArea(VisualElement root, SKCanvas canvas, SKRectI area)
    {
        canvas.Save();
        canvas.ClipRect(area);
        canvas.Clear(SKColors.Black);
        Draw(root, canvas, area);
        canvas.Restore();
    }

    /// <summary>The pixels <paramref name="element"/> covers in the frame, through its own and its ancestors' transforms.</summary>
    SKRectI PixelRect(VisualElement element, SKMatrix toWindow)
    {
        var bounds = element.WindowBounds;
        var over = (float)(element.Overdraw * _scale);
        var rect = new SKRect((float)bounds.Left * _scale - over, (float)bounds.Top * _scale - over,
            (float)bounds.Right * _scale + over, (float)bounds.Bottom * _scale + over);
        if (!toWindow.IsIdentity)
            rect = toWindow.MapRect(rect);
        return SKRectI.Round(new SKRect((float)Math.Floor(rect.Left), (float)Math.Floor(rect.Top), (float)Math.Ceiling(rect.Right), (float)Math.Ceiling(rect.Bottom)));
    }

    /// <summary>The window-space matrix an element's transform adds, around its own pixel origin.</summary>
    SKMatrix Transform(VisualElement element)
    {
        if (!element.HasTransform)
            return SKMatrix.Identity;
        var bounds = element.WindowBounds;
        var (x, y) = ((float)bounds.X * _scale, (float)bounds.Y * _scale);
        var size = new SKSize((float)bounds.Width * _scale, (float)bounds.Height * _scale);
        return SKMatrix.CreateTranslation(x, y).PreConcat(element.TransformMatrix(size, _scale)).PreConcat(SKMatrix.CreateTranslation(-x, -y));
    }

    /// <summary>Records what each element covers now, and marks for redraw what changed since the last frame.</summary>
    void Collect(VisualElement element, bool hidden, DirtyRegion dirty, SKMatrix parent)
    {
        var visible = !hidden && element.IsVisible && element.Opacity > 0;
        var toWindow = parent.PreConcat(Transform(element));
        var now = visible ? PixelRect(element, toWindow) : SKRectI.Empty;
        if (element.IsDirty || now != element.LastDrawn)
        {
            Profile?.Dirtied(element.GetType().Name, element.LastDrawn, now);
            dirty.Add(element.LastDrawn);
            dirty.Add(now);
        }
        element.LastDrawn = now;
        element.IsDirty = false;
        var subtree = now;
        foreach (var child in element.VisualChildren)
        {
            Collect(child, !visible, dirty, toWindow);
            subtree = subtree.IsEmpty ? child.SubtreeDrawn : child.SubtreeDrawn.IsEmpty ? subtree : SKRectI.Union(subtree, child.SubtreeDrawn);
        }
        element.SubtreeDrawn = subtree;
    }

    void Draw(VisualElement element, SKCanvas canvas, SKRectI area)
    {
        if (!element.IsVisible || element.Opacity <= 0 || !element.SubtreeDrawn.IntersectsWith(area))
            return;
        var bounds = element.WindowBounds;
        var origin = new SKPoint((float)bounds.X * _scale, (float)bounds.Y * _scale);
        var size = new SKSize((float)bounds.Width * _scale, (float)bounds.Height * _scale);
        // Restored to this exact count at the end: a layer for opacity is a second save on the stack.
        var saved = canvas.Save();
        var layered = element.Opacity < 1 && !element.PaintsOwnOpacity;
        if (layered)
        {
            using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(element.Opacity * 255)) };
            // Only as big as what this element and its children drew: a layer with no bounds is the whole redrawn area.
            SKRect drawn = element.SubtreeDrawn;
            if (!canvas.TotalMatrix.IsIdentity && canvas.TotalMatrix.TryInvert(out var toLocal))
                drawn = toLocal.MapRect(drawn);
            canvas.SaveLayer(drawn, layer);
        }
        canvas.Translate(origin.X, origin.Y);
        // TranslationX/Y, Rotation and Scale move the element and everything inside it, as on Android.
        if (element.HasTransform)
            canvas.Concat(element.TransformMatrix(size, _scale));
        var profile = Profile;
        var name = element.GetType().Name;
        if (SKRectI.Intersect(element.LastDrawn, area) is { IsEmpty: false })
        {
            try
            {
                var start = profile is null ? 0 : DrawProfile.Now;
                if (element.Shadow is not null)
                {
                    DrawShadow(element, canvas);
                    profile?.Drew("shadow " + name, start);
                    start = profile is null ? 0 : DrawProfile.Now;
                }
                element.Draw(canvas, size, _scale);
                profile?.Drew(name, start);
            }
            catch (Exception error)
            {
                UiErrors.Report(error, $"draw {name}");
            }
        }
        var clipStart = profile is null ? 0 : DrawProfile.Now;
        if (element.ChildClip(size) is { } clip)
        {
            using (clip)
                canvas.ClipPath(clip, antialias: true);
            profile?.Drew("clip " + name, clipStart);
        }
        canvas.Translate(-origin.X, -origin.Y);
        foreach (var child in element.VisualChildren)
            Draw(child, canvas, area);
        // Restoring a layer is when its contents are blended into the frame.
        var restoreStart = profile is null ? 0 : DrawProfile.Now;
        canvas.RestoreToCount(saved);
        if (layered)
            profile?.Drew("layer " + name, restoreStart);
    }

    // On the UI thread: makes the element's shadow image, or keeps the one made for the same shape, blur and colour.
    void PrepareShadow(VisualElement element, SKSize size)
    {
        if (element.Shadow is not { } shadow || shadow.Brush is not SolidColorBrush { Color: { } color })
        {
            _shadows.Remove(element);
            return;
        }
        using var path = element.ShapePath(size, _scale);
        var sigma = shadow.Radius > 0 ? shadow.Radius * _scale / 2 : 0;
        var tint = color.ToSKColor().WithAlpha((byte)Math.Round(color.Alpha * shadow.Opacity * 255));
        var key = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{path.ToSvgPathData()}|{sigma}|{(uint)tint}");
        var cached = _shadows.GetOrCreateValue(element);
        if (cached.Key != key || cached.Image is null)
        {
            cached.Image?.Dispose();
            cached.Image = null;
            cached.Key = key;
            // The blur spreads about three sigmas past the shape.
            var bounds = path.Bounds;
            bounds.Inflate((float)Math.Ceiling(sigma * 3) + 1, (float)Math.Ceiling(sigma * 3) + 1);
            var info = new SKImageInfo((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), SKColorType.Rgba8888, SKAlphaType.Premul);
            if (info.Width <= 0 || info.Height <= 0)
                return;
            using var surface = SKSurface.Create(info);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = tint,
                MaskFilter = sigma > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma) : null,
            };
            surface.Canvas.Translate(-bounds.Left, -bounds.Top);
            surface.Canvas.DrawPath(path, paint);
            cached.Image = surface.Snapshot();
            cached.Origin = new SKPoint(bounds.Left, bounds.Top);
            ShadowsMade++;
        }
    }

    void DrawShadow(VisualElement element, SKCanvas canvas)
    {
        if (element.Shadow is { } shadow && _shadows.TryGetValue(element, out var cached) && cached.Image is { } image)
            canvas.DrawImage(image, (float)shadow.Offset.X * _scale + cached.Origin.X, (float)shadow.Offset.Y * _scale + cached.Origin.Y);
    }
}
