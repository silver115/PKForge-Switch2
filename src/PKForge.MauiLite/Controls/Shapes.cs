using Microsoft.Maui.Graphics;
using SkiaSharp;

namespace Microsoft.Maui.Controls.Shapes
{
    /// <summary>MAUI's shape. MauiLite uses shapes as a border's outline.</summary>
    public abstract class Shape : View, IShape
    {
        public static readonly BindableProperty FillProperty = BindableProperty.Create(nameof(Fill), typeof(Brush), typeof(Shape), null);
        public static readonly BindableProperty StrokeProperty = BindableProperty.Create(nameof(Stroke), typeof(Brush), typeof(Shape), null);
        public static readonly BindableProperty StrokeThicknessProperty = BindableProperty.Create(nameof(StrokeThickness), typeof(double), typeof(Shape), 1d);

        public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
        public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
        public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty)!; set => SetValue(StrokeThicknessProperty, value); }

        internal override SKPath ShapePath(SKSize size, float scale) => ToPath(SKRect.Create(size), scale);

        // A shape used as a view draws itself: fill, then the stroke inside its bounds.
        internal override void Draw(SKCanvas canvas, SKSize size, float scale)
        {
            base.Draw(canvas, size, scale);
            if (Fill is { IsEmpty: false } fill)
            {
                using var path = ToPath(SKRect.Create(size), scale);
                using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
                fill.Apply(paint, SKRect.Create(size));
                canvas.DrawPath(path, paint);
            }
            var stroke = (float)StrokeThickness * scale;
            if (stroke > 0 && Stroke is { IsEmpty: false } brush)
            {
                var inset = SKRect.Create(size);
                inset.Inflate(-stroke / 2, -stroke / 2);
                using var path = ToPath(inset, scale);
                using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stroke };
                brush.Apply(paint, SKRect.Create(size));
                canvas.DrawPath(path, paint);
            }
        }

        // shortcut: MAUI's IShape path is the bounds rectangle; MauiLite draws shapes through ToPath. Upgrade if app code reads it.
        public PathF PathForBounds(Rect bounds)
        {
            var result = new PathF();
            result.AppendRectangle((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height);
            return result;
        }

        /// <summary>This shape's outline filling <paramref name="rect"/> (pixels); <paramref name="scale"/> converts unit sizes.</summary>
        internal abstract SKPath ToPath(SKRect rect, float scale);
    }

    public class Rectangle : Shape
    {
        internal override SKPath ToPath(SKRect rect, float scale)
        {
            var path = new SKPath();
            path.AddRect(rect);
            return path;
        }
    }

    public class Ellipse : Shape
    {
        internal override SKPath ToPath(SKRect rect, float scale)
        {
            var path = new SKPath();
            path.AddOval(rect);
            return path;
        }
    }

    public class RoundRectangle : Shape
    {
        public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(nameof(CornerRadius), typeof(CornerRadius), typeof(RoundRectangle), new CornerRadius());

        public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty)!; set => SetValue(CornerRadiusProperty, value); }

        internal override SKPath ToPath(SKRect rect, float scale) => PKForge.MauiLite.Rendering.Paths.RoundRect(rect, CornerRadius, scale);
    }
}

namespace PKForge.MauiLite.Rendering
{
    using Microsoft.Maui;

    static class Paths
    {
        /// <summary>A rectangle with each corner rounded by its own radius (units), clamped to fit.</summary>
        public static SKPath RoundRect(SKRect rect, CornerRadius radius, float scale)
        {
            var path = new SKPath();
            if (rect.Width <= 0 || rect.Height <= 0)
                return path;  // nothing to outline: a border thicker than its own size
            var limit = Math.Min(rect.Width, rect.Height) / 2;
            float R(double r) => Math.Clamp((float)r * scale, 0, limit);
            var (tl, tr, bl, br) = (R(radius.TopLeft), R(radius.TopRight), R(radius.BottomLeft), R(radius.BottomRight));
            if (tl == 0 && tr == 0 && bl == 0 && br == 0)
            {
                path.AddRect(rect);
                return path;
            }
            using var rrect = new SKRoundRect();
            rrect.SetRectRadii(rect, [new SKPoint(tl, tl), new SKPoint(tr, tr), new SKPoint(br, br), new SKPoint(bl, bl)]);
            path.AddRoundRect(rrect);
            return path;
        }
    }
}
