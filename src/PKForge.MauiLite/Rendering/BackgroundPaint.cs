using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace PKForge.MauiLite.Rendering;

static class BackgroundPaint
{
    /// <summary>A fill for an element's background: its brush, else its background color; null when it has neither.</summary>
    public static SKPaint? Create(Brush? brush, Color? color, SKSize size)
    {
        if (brush is { IsEmpty: false })
        {
            var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            brush.Apply(paint, SKRect.Create(size));
            return paint;
        }
        return color is null ? null : new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color.ToSKColor() };
    }
}
