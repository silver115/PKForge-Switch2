using System.Collections.ObjectModel;
using Microsoft.Maui.Graphics;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Microsoft.Maui.Controls;

/// <summary>MAUI's brush: how an area is filled.</summary>
public abstract class Brush : Element
{
    public static implicit operator Brush(Color color) => new SolidColorBrush(color);

    public static SolidColorBrush Transparent => new(Colors.Transparent);
    public static SolidColorBrush Black => new(Colors.Black);
    public static SolidColorBrush White => new(Colors.White);

    public abstract bool IsEmpty { get; }

    /// <summary>A paint that fills <paramref name="area"/> (pixels) with this brush.</summary>
    internal abstract void Apply(SKPaint paint, SKRect area);
}

public class SolidColorBrush : Brush
{
    public static readonly BindableProperty ColorProperty = BindableProperty.Create(nameof(Color), typeof(Color), typeof(SolidColorBrush), null);

    public SolidColorBrush()
    {
    }

    public SolidColorBrush(Color color) => Color = color;

    public Color? Color { get => (Color?)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    public override bool IsEmpty => Color is null;

    internal override void Apply(SKPaint paint, SKRect area) => paint.Color = (Color ?? Colors.Transparent).ToSKColor();
}

public class GradientStop : Element
{
    public static readonly BindableProperty ColorProperty = BindableProperty.Create(nameof(Color), typeof(Color), typeof(GradientStop), null);
    public static readonly BindableProperty OffsetProperty = BindableProperty.Create(nameof(Offset), typeof(float), typeof(GradientStop), 0f);

    public GradientStop()
    {
    }

    public GradientStop(Color color, float offset)
    {
        Color = color;
        Offset = offset;
    }

    public Color? Color { get => (Color?)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public float Offset { get => (float)GetValue(OffsetProperty)!; set => SetValue(OffsetProperty, value); }
}

public sealed class GradientStopCollection : ObservableCollection<GradientStop>
{
}

public abstract class GradientBrush : Brush
{
    public static readonly BindableProperty GradientStopsProperty = BindableProperty.Create(nameof(GradientStops), typeof(GradientStopCollection), typeof(GradientBrush), null,
        defaultValueCreator: _ => new GradientStopCollection());

    public GradientStopCollection GradientStops { get => (GradientStopCollection)GetValue(GradientStopsProperty)!; set => SetValue(GradientStopsProperty, value); }

    // The default collection is made now: reading it first while bands draw at once (DrawBands) would store it twice.
    private protected GradientBrush() => _ = GradientStops;

    public override bool IsEmpty => GradientStops.Count == 0;

    private protected (SKColor[] Colors, float[] Offsets) Stops()
    {
        var stops = GradientStops.OrderBy(stop => stop.Offset).ToArray();
        return ([.. stops.Select(stop => (stop.Color ?? Colors.Transparent).ToSKColor())], [.. stops.Select(stop => stop.Offset)]);
    }
}

public class LinearGradientBrush : GradientBrush
{
    public static readonly BindableProperty StartPointProperty = BindableProperty.Create(nameof(StartPoint), typeof(Point), typeof(LinearGradientBrush), new Point(0, 0));
    public static readonly BindableProperty EndPointProperty = BindableProperty.Create(nameof(EndPoint), typeof(Point), typeof(LinearGradientBrush), new Point(1, 1));

    public LinearGradientBrush()
    {
    }

    public LinearGradientBrush(GradientStopCollection gradientStops) => GradientStops = gradientStops;

    public LinearGradientBrush(GradientStopCollection gradientStops, Point startPoint, Point endPoint)
    {
        GradientStops = gradientStops;
        StartPoint = startPoint;
        EndPoint = endPoint;
    }

    /// <summary>Relative to the filled area: (0, 0) is its top-left corner and (1, 1) its bottom-right.</summary>
    public Point StartPoint { get => (Point)GetValue(StartPointProperty)!; set => SetValue(StartPointProperty, value); }
    public Point EndPoint { get => (Point)GetValue(EndPointProperty)!; set => SetValue(EndPointProperty, value); }

    internal override void Apply(SKPaint paint, SKRect area)
    {
        var (colors, offsets) = Stops();
        if (colors.Length == 1)
        {
            paint.Color = colors[0];
            return;
        }
        var start = new SKPoint(area.Left + (float)StartPoint.X * area.Width, area.Top + (float)StartPoint.Y * area.Height);
        var end = new SKPoint(area.Left + (float)EndPoint.X * area.Width, area.Top + (float)EndPoint.Y * area.Height);
        paint.Shader = SKShader.CreateLinearGradient(start, end, colors, offsets, SKShaderTileMode.Clamp);
    }
}
