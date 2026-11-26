using Microsoft.Maui.Graphics;

namespace SkiaSharp.Views.Maui;

/// <summary>SkiaSharp's conversions between MAUI and Skia types.</summary>
public static class Extensions
{
    public static SKColor ToSKColor(this Color color)
    {
        ArgumentNullException.ThrowIfNull(color);
        return new SKColor((byte)Math.Round(color.Red * 255), (byte)Math.Round(color.Green * 255), (byte)Math.Round(color.Blue * 255), (byte)Math.Round(color.Alpha * 255));
    }

    public static Color ToMauiColor(this SKColor color) => new(color.Red / 255f, color.Green / 255f, color.Blue / 255f, color.Alpha / 255f);

    public static SKPoint ToSKPoint(this Point point) => new((float)point.X, (float)point.Y);

    public static SKPoint ToSKPoint(this PointF point) => new(point.X, point.Y);

    public static SKSize ToSKSize(this Size size) => new((float)size.Width, (float)size.Height);

    public static SKRect ToSKRect(this Rect rect) => new((float)rect.Left, (float)rect.Top, (float)rect.Right, (float)rect.Bottom);

    public static Point ToMauiPoint(this SKPoint point) => new(point.X, point.Y);

    public static Size ToMauiSize(this SKSize size) => new(size.Width, size.Height);

    public static Rect ToMauiRect(this SKRect rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);
}
