using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Graphics;
using PKForge.MauiLite.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Microsoft.Maui.Controls;

/// <summary>MAUI's label: text, or a formatted string of spans, laid out as Android's TextView lays it out.</summary>
public class Label : View
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(Label), null).Measured();
    public static readonly BindableProperty FormattedTextProperty = BindableProperty.Create(nameof(FormattedText), typeof(FormattedString), typeof(Label), null,
        propertyChanged: (bindable, old, now) => ((Label)bindable).WatchSpans((FormattedString?)old, (FormattedString?)now)).Measured();
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(Label), null);
    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(nameof(FontFamily), typeof(string), typeof(Label), null).Measured();
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(nameof(FontSize), typeof(double), typeof(Label), 14d).Measured();
    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(nameof(FontAttributes), typeof(FontAttributes), typeof(Label), FontAttributes.None).Measured();
    public static readonly BindableProperty FontAutoScalingEnabledProperty = BindableProperty.Create(nameof(FontAutoScalingEnabled), typeof(bool), typeof(Label), true);
    public static readonly BindableProperty HorizontalTextAlignmentProperty = BindableProperty.Create(nameof(HorizontalTextAlignment), typeof(TextAlignment), typeof(Label), TextAlignment.Start);
    public static readonly BindableProperty VerticalTextAlignmentProperty = BindableProperty.Create(nameof(VerticalTextAlignment), typeof(TextAlignment), typeof(Label), TextAlignment.Start);
    public static readonly BindableProperty LineBreakModeProperty = BindableProperty.Create(nameof(LineBreakMode), typeof(LineBreakMode), typeof(Label), LineBreakMode.WordWrap).Measured();
    public static readonly BindableProperty MaxLinesProperty = BindableProperty.Create(nameof(MaxLines), typeof(int), typeof(Label), -1).Measured();
    public static readonly BindableProperty LineHeightProperty = BindableProperty.Create(nameof(LineHeight), typeof(double), typeof(Label), -1d).Measured();
    public static readonly BindableProperty CharacterSpacingProperty = BindableProperty.Create(nameof(CharacterSpacing), typeof(double), typeof(Label), 0d).Measured();
    public static readonly BindableProperty TextDecorationsProperty = BindableProperty.Create(nameof(TextDecorations), typeof(TextDecorations), typeof(Label), TextDecorations.None);
    public static readonly BindableProperty TextTransformProperty = BindableProperty.Create(nameof(TextTransform), typeof(TextTransform), typeof(Label), TextTransform.Default).Measured();
    public static readonly BindableProperty TextTypeProperty = BindableProperty.Create(nameof(TextType), typeof(TextType), typeof(Label), TextType.Text).Measured();
    public static readonly BindableProperty PaddingProperty = BindableProperty.Create(nameof(Padding), typeof(Thickness), typeof(Label), Thickness.Zero).Measured();

    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public FormattedString? FormattedText { get => (FormattedString?)GetValue(FormattedTextProperty); set => SetValue(FormattedTextProperty, value); }
    public Color? TextColor { get => (Color?)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    public string? FontFamily { get => (string?)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty)!; set => SetValue(FontSizeProperty, value); }
    public FontAttributes FontAttributes { get => (FontAttributes)GetValue(FontAttributesProperty)!; set => SetValue(FontAttributesProperty, value); }
    public bool FontAutoScalingEnabled { get => (bool)GetValue(FontAutoScalingEnabledProperty)!; set => SetValue(FontAutoScalingEnabledProperty, value); }
    public TextAlignment HorizontalTextAlignment { get => (TextAlignment)GetValue(HorizontalTextAlignmentProperty)!; set => SetValue(HorizontalTextAlignmentProperty, value); }
    public TextAlignment VerticalTextAlignment { get => (TextAlignment)GetValue(VerticalTextAlignmentProperty)!; set => SetValue(VerticalTextAlignmentProperty, value); }
    public LineBreakMode LineBreakMode { get => (LineBreakMode)GetValue(LineBreakModeProperty)!; set => SetValue(LineBreakModeProperty, value); }
    public int MaxLines { get => (int)GetValue(MaxLinesProperty)!; set => SetValue(MaxLinesProperty, value); }
    public double LineHeight { get => (double)GetValue(LineHeightProperty)!; set => SetValue(LineHeightProperty, value); }
    public double CharacterSpacing { get => (double)GetValue(CharacterSpacingProperty)!; set => SetValue(CharacterSpacingProperty, value); }
    public TextDecorations TextDecorations { get => (TextDecorations)GetValue(TextDecorationsProperty)!; set => SetValue(TextDecorationsProperty, value); }
    public TextTransform TextTransform { get => (TextTransform)GetValue(TextTransformProperty)!; set => SetValue(TextTransformProperty, value); }
    public TextType TextType { get => (TextType)GetValue(TextTypeProperty)!; set => SetValue(TextTypeProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty)!; set => SetValue(PaddingProperty, value); }

    /// <summary>Android's default text color for MAUI's light theme. PKForge sets its own colors almost everywhere.</summary>
    static readonly SKColor DefaultTextColor = SKColors.Black;

    TextLayout? _layout;
    float _layoutWidth = float.NaN;

    internal override Size MeasureContent(double availableWidth, double availableHeight)
    {
        var padding = Padding;
        var scale = Display.Density;
        var width = double.IsInfinity(availableWidth) ? float.PositiveInfinity : (float)Math.Max(0, (availableWidth - padding.HorizontalThickness) * scale);
        var layout = LayoutFor(width);
        return new Size(layout.Width / scale + padding.HorizontalThickness, layout.Height / scale + padding.VerticalThickness);
    }

    TextLayout LayoutFor(float widthPixels)
    {
        if (_layout is null || !_layoutWidth.Equals(widthPixels))
        {
            _layout = TextLayout.Build(Runs(), widthPixels, LineBreakMode, MaxLines, LineHeight, Display.Density);
            _layoutWidth = widthPixels;
        }
        return _layout;
    }

    List<(string Text, TextStyle Style)> Runs()
    {
        var own = new TextStyle(FontFamily, FontSize, FontAttributes, TextColor?.ToSKColor() ?? DefaultTextColor, null, CharacterSpacing, TextDecorations);
        if (FormattedText is { } formatted)
            return [.. formatted.Spans.Select(span => (Transform(span.Text ?? "", span.TextTransform), span.StyleFrom(own)))];
        return [(Transform(Text ?? "", TextTransform), own)];
    }

    static string Transform(string text, TextTransform transform) => transform switch
    {
        TextTransform.Uppercase => text.ToUpperInvariant(),
        TextTransform.Lowercase => text.ToLowerInvariant(),
        _ => text,
    };

    internal override void Prepare(SKSize size, float scale) =>
        LayoutFor(Math.Max(0, size.Width - (float)Padding.HorizontalThickness * scale));

    internal override void Draw(SKCanvas canvas, SKSize size, float scale)
    {
        base.Draw(canvas, size, scale);
        var padding = Padding;
        var left = (float)padding.Left * scale;
        var top = (float)padding.Top * scale;
        var width = Math.Max(0, size.Width - (float)padding.HorizontalThickness * scale);
        var height = Math.Max(0, size.Height - (float)padding.VerticalThickness * scale);
        // Android lays the text out again at the width the view was given.
        var layout = LayoutFor(width);
        var y = top + VerticalTextAlignment switch
        {
            TextAlignment.Center => (height - layout.Height) / 2,
            TextAlignment.End => height - layout.Height,
            _ => 0,
        };
        canvas.Save();
        canvas.ClipRect(SKRect.Create(size));
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var line in layout.Lines)
        {
            var x = left + HorizontalTextAlignment switch
            {
                TextAlignment.Center => (width - line.Width) / 2,
                TextAlignment.End => width - line.Width,
                _ => 0,
            };
            var baseline = y + line.Top + (line.Height - (line.Descent - line.Ascent)) / 2 - line.Ascent;
            foreach (var piece in line.Pieces)
                DrawPiece(canvas, paint, piece, x + piece.X, baseline, line, y);
        }
        canvas.Restore();
    }

    static void DrawPiece(SKCanvas canvas, SKPaint paint, TextLayout.Piece piece, float x, float baseline, TextLayout.Line line, float top)
    {
        if (piece.Style.Background is { } background)
        {
            paint.Color = background;
            canvas.DrawRect(new SKRect(x, top + line.Top, x + piece.Width, top + line.Top + line.Height), paint);
        }
        paint.Color = piece.Style.Color;
        var spacing = TextLayout.LetterSpacing(piece.Font, piece.Style);
        if (spacing == 0)
            canvas.DrawText(piece.Text, x, baseline, SKTextAlign.Left, piece.Font, paint);
        else
        {
            var e = System.Globalization.StringInfo.GetTextElementEnumerator(piece.Text);
            var cursor = x;
            while (e.MoveNext())
            {
                var element = e.GetTextElement();
                canvas.DrawText(element, cursor, baseline, SKTextAlign.Left, piece.Font, paint);
                cursor += piece.Font.MeasureText(element) + spacing;
            }
        }
        var thickness = Math.Max(1, piece.Font.Size / 14);
        if (piece.Style.Decorations.HasFlag(TextDecorations.Underline))
            canvas.DrawRect(new SKRect(x, baseline + thickness, x + piece.Width, baseline + 2 * thickness), paint);
        if (piece.Style.Decorations.HasFlag(TextDecorations.Strikethrough))
        {
            var middle = baseline + piece.Font.Metrics.Ascent * 0.35f;
            canvas.DrawRect(new SKRect(x, middle - thickness / 2, x + piece.Width, middle + thickness / 2), paint);
        }
    }

    internal override void OnBindablePropertyChanged(BindableProperty property, object? oldValue, object? newValue)
    {
        _layout = null;
        base.OnBindablePropertyChanged(property, oldValue, newValue);
    }

    void WatchSpans(FormattedString? old, FormattedString? now)
    {
        if (old is not null)
            old.Changed -= OnSpansChanged;
        if (now is not null)
            now.Changed += OnSpansChanged;
    }

    void OnSpansChanged(object? sender, EventArgs e)
    {
        _layout = null;
        InvalidateLayout();
    }
}

/// <summary>MAUI's formatted text: a list of spans.</summary>
public class FormattedString : Element
{
    public FormattedString()
    {
        Spans.CollectionChanged += OnSpansChanged;
    }

    public ObservableCollection<Span> Spans { get; } = [];

    internal event EventHandler? Changed;

    public static implicit operator FormattedString(string text) => new() { Spans = { new Span { Text = text } } };

    public override string ToString() => string.Concat(Spans.Select(span => span.Text));

    void OnSpansChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var span in e.OldItems?.OfType<Span>() ?? [])
            span.PropertyChanged -= OnSpanChanged;
        foreach (var span in e.NewItems?.OfType<Span>() ?? [])
            span.PropertyChanged += OnSpanChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void OnSpanChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>MAUI's span: a run of text in a formatted string. Unset values come from the label.</summary>
public class Span : Element
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(Span), "");
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(Span), null);
    public static readonly BindableProperty BackgroundColorProperty = BindableProperty.Create(nameof(BackgroundColor), typeof(Color), typeof(Span), null);
    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(nameof(FontFamily), typeof(string), typeof(Span), null);
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(nameof(FontSize), typeof(double), typeof(Span), 14d);
    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(nameof(FontAttributes), typeof(FontAttributes), typeof(Span), FontAttributes.None);
    public static readonly BindableProperty CharacterSpacingProperty = BindableProperty.Create(nameof(CharacterSpacing), typeof(double), typeof(Span), 0d);
    public static readonly BindableProperty TextDecorationsProperty = BindableProperty.Create(nameof(TextDecorations), typeof(TextDecorations), typeof(Span), TextDecorations.None);
    public static readonly BindableProperty TextTransformProperty = BindableProperty.Create(nameof(TextTransform), typeof(TextTransform), typeof(Span), TextTransform.Default);
    public static readonly BindableProperty LineHeightProperty = BindableProperty.Create(nameof(LineHeight), typeof(double), typeof(Span), -1d);

    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Color? TextColor { get => (Color?)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    public Color? BackgroundColor { get => (Color?)GetValue(BackgroundColorProperty); set => SetValue(BackgroundColorProperty, value); }
    public string? FontFamily { get => (string?)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty)!; set => SetValue(FontSizeProperty, value); }
    public FontAttributes FontAttributes { get => (FontAttributes)GetValue(FontAttributesProperty)!; set => SetValue(FontAttributesProperty, value); }
    public double CharacterSpacing { get => (double)GetValue(CharacterSpacingProperty)!; set => SetValue(CharacterSpacingProperty, value); }
    public TextDecorations TextDecorations { get => (TextDecorations)GetValue(TextDecorationsProperty)!; set => SetValue(TextDecorationsProperty, value); }
    public TextTransform TextTransform { get => (TextTransform)GetValue(TextTransformProperty)!; set => SetValue(TextTransformProperty, value); }
    public double LineHeight { get => (double)GetValue(LineHeightProperty)!; set => SetValue(LineHeightProperty, value); }

    internal TextStyle StyleFrom(TextStyle label) => new(
        IsSet(FontFamilyProperty) ? FontFamily : label.Family,
        IsSet(FontSizeProperty) ? FontSize : label.FontSize,
        IsSet(FontAttributesProperty) ? FontAttributes : label.Attributes,
        TextColor?.ToSKColor() ?? label.Color,
        BackgroundColor?.ToSKColor(),
        IsSet(CharacterSpacingProperty) ? CharacterSpacing : label.CharacterSpacing,
        IsSet(TextDecorationsProperty) ? TextDecorations : label.Decorations);
}
