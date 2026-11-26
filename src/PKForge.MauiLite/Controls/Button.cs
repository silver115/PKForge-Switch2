using System.Windows.Input;
using Microsoft.Maui.Graphics;
using PKForge.MauiLite.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Microsoft.Maui.Controls;

/// <summary>
/// MAUI's button: text on a rounded background, clicked by a tap that ends inside it.
/// shortcut: drawn as a plain filled button; PKForge's Android skin (CapsuleSkin) is matched in the screen tasks.
/// </summary>
public class Button : View
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(Button), null).Measured();
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(Button), null);
    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(nameof(FontFamily), typeof(string), typeof(Button), null).Measured();
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(nameof(FontSize), typeof(double), typeof(Button), 14d).Measured();
    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(nameof(FontAttributes), typeof(FontAttributes), typeof(Button), FontAttributes.None).Measured();
    public static readonly BindableProperty CharacterSpacingProperty = BindableProperty.Create(nameof(CharacterSpacing), typeof(double), typeof(Button), 0d).Measured();
    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(nameof(CornerRadius), typeof(int), typeof(Button), -1);
    public static readonly BindableProperty BorderColorProperty = BindableProperty.Create(nameof(BorderColor), typeof(Color), typeof(Button), null);
    public static readonly BindableProperty BorderWidthProperty = BindableProperty.Create(nameof(BorderWidth), typeof(double), typeof(Button), -1d);
    public static readonly BindableProperty PaddingProperty = BindableProperty.Create(nameof(Padding), typeof(Thickness), typeof(Button), new Thickness(16, 10)).Measured();
    public static readonly BindableProperty LineBreakModeProperty = BindableProperty.Create(nameof(LineBreakMode), typeof(LineBreakMode), typeof(Button), LineBreakMode.NoWrap).Measured();
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(Button), null);
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(Button), null);

    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Color? TextColor { get => (Color?)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    public string? FontFamily { get => (string?)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty)!; set => SetValue(FontSizeProperty, value); }
    public FontAttributes FontAttributes { get => (FontAttributes)GetValue(FontAttributesProperty)!; set => SetValue(FontAttributesProperty, value); }
    public double CharacterSpacing { get => (double)GetValue(CharacterSpacingProperty)!; set => SetValue(CharacterSpacingProperty, value); }
    public int CornerRadius { get => (int)GetValue(CornerRadiusProperty)!; set => SetValue(CornerRadiusProperty, value); }
    public Color? BorderColor { get => (Color?)GetValue(BorderColorProperty); set => SetValue(BorderColorProperty, value); }
    public double BorderWidth { get => (double)GetValue(BorderWidthProperty)!; set => SetValue(BorderWidthProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty)!; set => SetValue(PaddingProperty, value); }
    public LineBreakMode LineBreakMode { get => (LineBreakMode)GetValue(LineBreakModeProperty)!; set => SetValue(LineBreakModeProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    public bool IsPressed { get; private set; }

    public event EventHandler? Clicked;
    public event EventHandler? Pressed;
    public event EventHandler? Released;

    public void SendClicked()
    {
        if (Command is { } command && command.CanExecute(CommandParameter))
            command.Execute(CommandParameter);
        Clicked?.Invoke(this, EventArgs.Empty);
    }

    public void SendPressed()
    {
        IsPressed = true;
        Invalidate();
        Pressed?.Invoke(this, EventArgs.Empty);
    }

    public void SendReleased()
    {
        IsPressed = false;
        Invalidate();
        Released?.Invoke(this, EventArgs.Empty);
    }

    TextStyle Style => new(FontFamily, FontSize, FontAttributes, TextColor?.ToSKColor() ?? SKColors.White, null, CharacterSpacing, TextDecorations.None);

    TextLayout Layout(float widthPixels) =>
        TextLayout.Build([(Text ?? "", Style)], widthPixels, LineBreakMode, -1, -1, Display.Density);

    internal override Size MeasureContent(double availableWidth, double availableHeight)
    {
        var padding = Padding;
        var scale = Display.Density;
        var width = double.IsInfinity(availableWidth) ? float.PositiveInfinity : (float)Math.Max(0, (availableWidth - padding.HorizontalThickness) * scale);
        var layout = Layout(width);
        return new Size(layout.Width / scale + padding.HorizontalThickness, layout.Height / scale + padding.VerticalThickness);
    }

    internal override SKPath ShapePath(SKSize size, float scale) =>
        Paths.RoundRect(SKRect.Create(size), new CornerRadius(Math.Max(0, CornerRadius)), scale);

    // Laid out on the UI thread before drawing, which may run on several threads.
    TextLayout? _drawLayout;

    internal override void Prepare(SKSize size, float scale) =>
        _drawLayout = Layout(Math.Max(0, size.Width - (float)Padding.HorizontalThickness * scale));

    internal override void Draw(SKCanvas canvas, SKSize size, float scale)
    {
        using (var outline = ShapePath(size, scale))
        {
            using (var fill = BackgroundPaint.Create(Background, BackgroundColor, size))
                if (fill is not null)
                    canvas.DrawPath(outline, fill);
            if (BorderColor is { } border && BorderWidth > 0)
            {
                using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)BorderWidth * scale, Color = border.ToSKColor() };
                canvas.DrawPath(outline, stroke);
            }
        }
        var padding = Padding;
        var width = Math.Max(0, size.Width - (float)padding.HorizontalThickness * scale);
        var layout = _drawLayout ?? Layout(width);
        var top = (size.Height - layout.Height) / 2;
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var line in layout.Lines)
        {
            var left = (float)padding.Left * scale + (width - line.Width) / 2;
            var baseline = top + line.Top + (line.Height - (line.Descent - line.Ascent)) / 2 - line.Ascent;
            foreach (var piece in line.Pieces)
            {
                paint.Color = piece.Style.Color;
                canvas.DrawText(piece.Text, left + piece.X, baseline, SKTextAlign.Left, piece.Font, paint);
            }
        }
    }
}
