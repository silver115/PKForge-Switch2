using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using PKForge.MauiLite.Rendering;
using SkiaSharp;

namespace Microsoft.Maui.Controls;

/// <summary>What ContentView, Border and ScrollView share: one child, laid out inside an inset.</summary>
public abstract class SingleContentView : View, IContentView
{
    public static readonly BindableProperty PaddingProperty = BindableProperty.Create(nameof(Padding), typeof(Thickness), typeof(SingleContentView), Thickness.Zero).Measured();

    View? _content;

    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty)!; set => SetValue(PaddingProperty, value); }

    /// <summary>The one child; MAUI types it as <see cref="View"/> on ContentView, Border and ScrollView.</summary>
    protected View? ContentCore
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value))
                return;
            if (_content is not null && ReferenceEquals(_content.Parent, this))
                _content.Parent = null;
            _content = value;
            if (value is not null)
                value.Parent = this;
            OnPropertyChanged("Content");
            InvalidateLayout();
        }
    }

    /// <summary>The space between this view's edge and its content (padding, plus a border's stroke).</summary>
    internal virtual Thickness ContentInset => Padding;

    object? IContentView.Content => _content;
    IView? IContentView.PresentedContent => _content;
    Thickness IPadding.Padding => ContentInset;

    internal override Size MeasureNative(double widthConstraint, double heightConstraint)
    {
        IView view = this;
        var (width, height) = ContentConstraints(widthConstraint, heightConstraint);
        var size = this.MeasureContent(ContentInset, width, height);
        return new Size(
            LayoutManager.ResolveConstraints(widthConstraint, view.Width, size.Width, view.MinimumWidth, view.MaximumWidth),
            LayoutManager.ResolveConstraints(heightConstraint, view.Height, size.Height, view.MinimumHeight, view.MaximumHeight));
    }

    internal override void ArrangeContent(Rect bounds) => ((IContentView)this).ArrangeContent(bounds);

    internal override IEnumerable<VisualElement> VisualChildren => _content is null ? [] : [_content];
}

public class ContentView : SingleContentView
{
    public View? Content { get => ContentCore; set => ContentCore = value; }
}

public class Border : SingleContentView
{
    public static readonly BindableProperty StrokeThicknessProperty = BindableProperty.Create(nameof(StrokeThickness), typeof(double), typeof(Border), 1d).Measured();
    public static readonly BindableProperty StrokeProperty = BindableProperty.Create(nameof(Stroke), typeof(Brush), typeof(Border), null);
    public static readonly BindableProperty StrokeShapeProperty = BindableProperty.Create(nameof(StrokeShape), typeof(IShape), typeof(Border), null);

    public View? Content { get => ContentCore; set => ContentCore = value; }

    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty)!; set => SetValue(StrokeThicknessProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public IShape? StrokeShape { get => (IShape?)GetValue(StrokeShapeProperty); set => SetValue(StrokeShapeProperty, value); }

    // MAUI's Border insets its content by the padding and the stroke.
    internal override Thickness ContentInset => Padding + StrokeThickness;

    SKPath Outline(SKRect rect, float scale)
    {
        if (StrokeShape is Shapes.Shape shape)
            return shape.ToPath(rect, scale);
        var path = new SKPath();
        path.AddRect(rect);
        return path;
    }

    internal override SKPath ShapePath(SKSize size, float scale) => Outline(SKRect.Create(size), scale);

    internal override void Draw(SKCanvas canvas, SKSize size, float scale)
    {
        var stroke = (float)StrokeThickness * scale;
        using (var outline = Outline(SKRect.Create(size), scale))
        using (var fill = BackgroundPaint.Create(Background, BackgroundColor, size))
            if (fill is not null)
                canvas.DrawPath(outline, fill);
        if (stroke <= 0 || Stroke is not { IsEmpty: false } brush)
            return;
        // The stroke is centred on the outline, inset by half its width so it stays inside the bounds.
        var inset = SKRect.Create(size);
        inset.Inflate(-stroke / 2, -stroke / 2);
        using var path = Outline(inset, scale);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stroke };
        brush.Apply(paint, SKRect.Create(size));
        canvas.DrawPath(path, paint);
    }

    internal override bool ClipsChildren => true;

    // MAUI's Border clips its content to the inside of its outline.
    internal override SKPath? ChildClip(SKSize size)
    {
        var stroke = (float)StrokeThickness * Display.Density;
        var inner = SKRect.Create(size);
        inner.Inflate(-stroke, -stroke);
        return Outline(inner, Display.Density);
    }
}

public class BoxView : View
{
    public static readonly BindableProperty ColorProperty = BindableProperty.Create(nameof(Color), typeof(Color), typeof(BoxView), null);
    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(nameof(CornerRadius), typeof(CornerRadius), typeof(BoxView), new CornerRadius());

    public Color? Color { get => (Color?)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty)!; set => SetValue(CornerRadiusProperty, value); }

    internal override SKPath ShapePath(SKSize size, float scale) => Paths.RoundRect(SKRect.Create(size), CornerRadius, scale);

    internal override void Draw(SKCanvas canvas, SKSize size, float scale)
    {
        using var fill = BackgroundPaint.Create(Color is null ? Background : null, Color ?? BackgroundColor, size);
        if (fill is null)
            return;
        using var path = ShapePath(size, scale);
        canvas.DrawPath(path, fill);
    }
}

public enum ScrollOrientation { Vertical, Horizontal, Both, Neither }

/// <summary>MAUI's scroll view: its content is measured without limit in the scroll direction.</summary>
public class ScrollView : SingleContentView
{
    public static readonly BindableProperty OrientationProperty = BindableProperty.Create(nameof(Orientation), typeof(ScrollOrientation), typeof(ScrollView), ScrollOrientation.Vertical).Measured();

    public View? Content { get => ContentCore; set => ContentCore = value; }

    public ScrollOrientation Orientation { get => (ScrollOrientation)GetValue(OrientationProperty)!; set => SetValue(OrientationProperty, value); }

    public static readonly BindableProperty HorizontalScrollBarVisibilityProperty = BindableProperty.Create(nameof(HorizontalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(ScrollView), ScrollBarVisibility.Default);
    public static readonly BindableProperty VerticalScrollBarVisibilityProperty = BindableProperty.Create(nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(ScrollView), ScrollBarVisibility.Default);

    // shortcut: no scroll bars are drawn; PKForge sets them to Never on its screens. Draw them if a screen shows one.
    public ScrollBarVisibility HorizontalScrollBarVisibility { get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty); set => SetValue(HorizontalScrollBarVisibilityProperty, value); }
    public ScrollBarVisibility VerticalScrollBarVisibility { get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty); set => SetValue(VerticalScrollBarVisibilityProperty, value); }

    public double ScrollX { get; internal set; }
    public double ScrollY { get; internal set; }
    public Size ContentSize { get; private set; }

    internal override Size MeasureNative(double widthConstraint, double heightConstraint)
    {
        IView view = this;
        var horizontal = Orientation is ScrollOrientation.Horizontal or ScrollOrientation.Both;
        var vertical = Orientation is ScrollOrientation.Vertical or ScrollOrientation.Both;
        // As MAUI: the scroll direction is unbounded, and the scroll view's own size does not cap its content there.
        var content = this.MeasureContent(ContentInset, horizontal ? double.PositiveInfinity : widthConstraint,
            vertical ? double.PositiveInfinity : heightConstraint, !horizontal, !vertical);
        return new Size(
            LayoutManager.ResolveConstraints(widthConstraint, view.Width, Math.Min(content.Width, widthConstraint), view.MinimumWidth, view.MaximumWidth),
            LayoutManager.ResolveConstraints(heightConstraint, view.Height, Math.Min(content.Height, heightConstraint), view.MinimumHeight, view.MaximumHeight));
    }

    internal override void ArrangeContent(Rect bounds) => ContentSize = ((IContentView)this).ArrangeContentUnbounded(bounds);

    internal override Point ChildOffset => new(-ScrollX, -ScrollY);

    public event EventHandler<ScrolledEventArgs>? Scrolled;

    /// <summary>Moves the content, clamped so it never scrolls past its ends.</summary>
    internal void SetScroll(double x, double y)
    {
        var horizontal = Orientation is ScrollOrientation.Horizontal or ScrollOrientation.Both;
        var vertical = Orientation is ScrollOrientation.Vertical or ScrollOrientation.Both;
        x = horizontal ? Math.Clamp(x, 0, Math.Max(0, ContentSize.Width - Width)) : 0;
        y = vertical ? Math.Clamp(y, 0, Math.Max(0, ContentSize.Height - Height)) : 0;
        if (x == ScrollX && y == ScrollY)
            return;
        ScrollX = x;
        ScrollY = y;
        Invalidate();
        Scrolled?.Invoke(this, new ScrolledEventArgs(x, y));
    }

    public Task ScrollToAsync(double x, double y, bool animated)
    {
        SetScroll(x, y);
        return Task.CompletedTask;
    }

    public Task ScrollToAsync(Element element, ScrollToPosition position, bool animated)
    {
        if (element is VisualElement visual && Content is { } content)
        {
            // The element's place inside the content, then the scroll that shows it as asked.
            var top = visual.WindowBounds.Y - content.WindowBounds.Y;
            var left = visual.WindowBounds.X - content.WindowBounds.X;
            var y = position switch
            {
                ScrollToPosition.Center => top - (Height - visual.Height) / 2,
                ScrollToPosition.End => top - (Height - visual.Height),
                ScrollToPosition.MakeVisible when top >= ScrollY && top + visual.Height <= ScrollY + Height => ScrollY,
                ScrollToPosition.MakeVisible when top + visual.Height > ScrollY + Height => top - (Height - visual.Height),
                _ => top,
            };
            SetScroll(left, y);
        }
        return Task.CompletedTask;
    }

    internal override bool ClipsChildren => true;

    // A scroll view shows its content only inside its own bounds.
    internal override SKPath? ChildClip(SKSize size)
    {
        var path = new SKPath();
        path.AddRect(SKRect.Create(size));
        return path;
    }
}

public enum ScrollToPosition { MakeVisible, Start, Center, End }

public class ScrolledEventArgs(double x, double y) : EventArgs
{
    public double ScrollX { get; } = x;
    public double ScrollY { get; } = y;
}
