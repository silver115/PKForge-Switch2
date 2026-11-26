using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Controls;

/// <summary>MAUI's drop shadow behind an element.</summary>
public class Shadow : Element
{
    public static readonly BindableProperty BrushProperty = BindableProperty.Create(nameof(Brush), typeof(Brush), typeof(Shadow), null);
    public static readonly BindableProperty OffsetProperty = BindableProperty.Create(nameof(Offset), typeof(Point), typeof(Shadow), new Point(0, 0));
    public static readonly BindableProperty RadiusProperty = BindableProperty.Create(nameof(Radius), typeof(float), typeof(Shadow), 10f);
    public static readonly BindableProperty OpacityProperty = BindableProperty.Create(nameof(Opacity), typeof(float), typeof(Shadow), 1f);

    public Brush? Brush { get => (Brush?)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    public Point Offset { get => (Point)GetValue(OffsetProperty)!; set => SetValue(OffsetProperty, value); }
    public float Radius { get => (float)GetValue(RadiusProperty)!; set => SetValue(RadiusProperty, value); }
    public float Opacity { get => (float)GetValue(OpacityProperty)!; set => SetValue(OpacityProperty, value); }
}
