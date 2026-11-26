using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Primitives;
using PKForge.MauiLite.Rendering;
using SkiaSharp;

namespace Microsoft.Maui.Controls;

/// <summary>
/// MAUI's visual element. Measuring follows MAUI on Android: a view's own size comes from a measure "spec"
/// (exact for an explicit request, at-most for a finite constraint, unspecified for an infinite one), and
/// layouts measure through MAUI's own layout managers.
/// </summary>
public abstract partial class VisualElement : Element, IView
{
    public static readonly BindableProperty WidthRequestProperty = BindableProperty.Create(nameof(WidthRequest), typeof(double), typeof(VisualElement), -1d).Measured();
    public static readonly BindableProperty HeightRequestProperty = BindableProperty.Create(nameof(HeightRequest), typeof(double), typeof(VisualElement), -1d).Measured();
    public static readonly BindableProperty MinimumWidthRequestProperty = BindableProperty.Create(nameof(MinimumWidthRequest), typeof(double), typeof(VisualElement), -1d).Measured();
    public static readonly BindableProperty MinimumHeightRequestProperty = BindableProperty.Create(nameof(MinimumHeightRequest), typeof(double), typeof(VisualElement), -1d).Measured();
    public static readonly BindableProperty MaximumWidthRequestProperty = BindableProperty.Create(nameof(MaximumWidthRequest), typeof(double), typeof(VisualElement), double.PositiveInfinity).Measured();
    public static readonly BindableProperty MaximumHeightRequestProperty = BindableProperty.Create(nameof(MaximumHeightRequest), typeof(double), typeof(VisualElement), double.PositiveInfinity).Measured();
    public static readonly BindableProperty IsVisibleProperty = BindableProperty.Create(nameof(IsVisible), typeof(bool), typeof(VisualElement), true).Measured();
    public static readonly BindableProperty OpacityProperty = BindableProperty.Create(nameof(Opacity), typeof(double), typeof(VisualElement), 1d);
    public static readonly BindableProperty InputTransparentProperty = BindableProperty.Create(nameof(InputTransparent), typeof(bool), typeof(VisualElement), false);
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(nameof(IsEnabled), typeof(bool), typeof(VisualElement), true);
    public static readonly BindableProperty ZIndexProperty = BindableProperty.Create(nameof(ZIndex), typeof(int), typeof(VisualElement), 0);
    public static readonly BindableProperty BackgroundColorProperty = BindableProperty.Create(nameof(BackgroundColor), typeof(Color), typeof(VisualElement), null);
    public static readonly BindableProperty BackgroundProperty = BindableProperty.Create(nameof(Background), typeof(Brush), typeof(VisualElement), null);
    public static readonly BindableProperty ShadowProperty = BindableProperty.Create(nameof(Shadow), typeof(Shadow), typeof(VisualElement), null);

    public Brush? Background { get => (Brush?)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public Shadow? Shadow { get => (Shadow?)GetValue(ShadowProperty); set => SetValue(ShadowProperty, value); }

    public double WidthRequest { get => (double)GetValue(WidthRequestProperty)!; set => SetValue(WidthRequestProperty, value); }
    public double HeightRequest { get => (double)GetValue(HeightRequestProperty)!; set => SetValue(HeightRequestProperty, value); }
    public double MinimumWidthRequest { get => (double)GetValue(MinimumWidthRequestProperty)!; set => SetValue(MinimumWidthRequestProperty, value); }
    public double MinimumHeightRequest { get => (double)GetValue(MinimumHeightRequestProperty)!; set => SetValue(MinimumHeightRequestProperty, value); }
    public double MaximumWidthRequest { get => (double)GetValue(MaximumWidthRequestProperty)!; set => SetValue(MaximumWidthRequestProperty, value); }
    public double MaximumHeightRequest { get => (double)GetValue(MaximumHeightRequestProperty)!; set => SetValue(MaximumHeightRequestProperty, value); }
    public bool IsVisible { get => (bool)GetValue(IsVisibleProperty)!; set => SetValue(IsVisibleProperty, value); }
    public double Opacity { get => (double)GetValue(OpacityProperty)!; set => SetValue(OpacityProperty, value); }
    public bool InputTransparent { get => (bool)GetValue(InputTransparentProperty)!; set => SetValue(InputTransparentProperty, value); }
    public bool IsEnabled { get => (bool)GetValue(IsEnabledProperty)!; set => SetValue(IsEnabledProperty, value); }
    public int ZIndex { get => (int)GetValue(ZIndexProperty)!; set => SetValue(ZIndexProperty, value); }
    public Color? BackgroundColor { get => (Color?)GetValue(BackgroundColorProperty); set => SetValue(BackgroundColorProperty, value); }

    /// <summary>The arranged frame, relative to the parent.</summary>
    public Rect Bounds { get; private set; }
    public Rect Frame => Bounds;
    public double X => Bounds.X;
    public double Y => Bounds.Y;
    public double Width { get; private set; } = -1;
    public double Height { get; private set; } = -1;
    public Size DesiredSize { get; private set; }

    public event EventHandler? SizeChanged;

    /// <summary>Where this element sits in the window, in units. Set by <see cref="PKForge.MauiLite.LayoutPass"/>.</summary>
    internal Rect WindowBounds { get; set; }

    /// <summary>True when this element or a descendant changed in a way that needs measuring again.</summary>
    internal bool NeedsLayout { get; set; } = true;

    /// <summary>Raised when how this element looks changed; the renderer redraws it.</summary>
    internal event Action<VisualElement>? Invalidated;

    double _lastWidthConstraint = double.NaN, _lastHeightConstraint = double.NaN;

    protected VisualElement() =>
        // MAUI gives a new element its implicit style; values the app sets afterwards win.
        Application.Current?.Resources.ApplyImplicitStyle(this);

    internal override IEnumerable<BindableObject> InheritingChildren => VisualChildren;

    public Size Measure(double widthConstraint, double heightConstraint)
    {
        // A clean element asked the same question gives the same answer.
        if (!NeedsLayout && widthConstraint.Equals(_lastWidthConstraint) && heightConstraint.Equals(_lastHeightConstraint))
            return DesiredSize;
        DesiredSize = MeasureOverride(widthConstraint, heightConstraint);
        (_lastWidthConstraint, _lastHeightConstraint) = (widthConstraint, heightConstraint);
        return DesiredSize;
    }

    /// <summary>MAUI's default: measure without the margin, then add it back (<c>ComputeDesiredSize</c>).</summary>
    protected virtual Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        var margin = ((IView)this).Margin;
        var inner = MeasureNative(widthConstraint - margin.HorizontalThickness, heightConstraint - margin.VerticalThickness);
        return new Size(inner.Width + margin.HorizontalThickness, inner.Height + margin.VerticalThickness);
    }

    /// <summary>The platform measure: Android's measure specs around <see cref="MeasureContent"/>.</summary>
    internal virtual Size MeasureNative(double widthConstraint, double heightConstraint)
    {
        IView view = this;
        var (width, widthMode) = Spec(widthConstraint, view.Width, view.MinimumWidth, view.MaximumWidth);
        var (height, heightMode) = Spec(heightConstraint, view.Height, view.MinimumHeight, view.MaximumHeight);
        var content = MeasureContent(widthMode == SpecMode.Unspecified ? double.PositiveInfinity : width,
            heightMode == SpecMode.Unspecified ? double.PositiveInfinity : height);
        return new Size(Resolve(width, widthMode, content.Width), Resolve(height, heightMode, content.Height));
    }

    /// <summary>
    /// The space this element's content is measured in, as Android's measure spec gives it (MAUI's CreateMeasureSpec):
    /// its explicit size, else its maximum where that is narrower than the constraint. A view that measures its own
    /// children (a layout, a content view) asks this first, as MAUI's handlers do on Android.
    /// </summary>
    internal (double Width, double Height) ContentConstraints(double widthConstraint, double heightConstraint)
    {
        IView view = this;
        var (width, widthMode) = Spec(widthConstraint, view.Width, view.MinimumWidth, view.MaximumWidth);
        var (height, heightMode) = Spec(heightConstraint, view.Height, view.MinimumHeight, view.MaximumHeight);
        return (widthMode == SpecMode.Unspecified ? double.PositiveInfinity : width,
            heightMode == SpecMode.Unspecified ? double.PositiveInfinity : height);
    }

    /// <summary>
    /// The intrinsic size of a leaf view for the space it may use. Android's default for a plain view
    /// (<c>View.getDefaultSize</c>) takes all of an at-most constraint and nothing of an unspecified one.
    /// </summary>
    internal virtual Size MeasureContent(double availableWidth, double availableHeight) =>
        new(double.IsInfinity(availableWidth) ? 0 : availableWidth, double.IsInfinity(availableHeight) ? 0 : availableHeight);

    enum SpecMode { Exactly, AtMost, Unspecified }

    // MAUI's ContextExtensions.CreateMeasureSpec (Android).
    static (double Size, SpecMode Mode) Spec(double constraint, double explicitSize, double minimum, double maximum)
    {
        constraint = Math.Max(0, constraint);
        if (Dimension.IsExplicitSet(explicitSize))
        {
            var size = Math.Max(explicitSize, Dimension.ResolveMinimum(minimum));
            if (Dimension.IsMaximumSet(maximum))
                size = Math.Min(size, maximum);
            return (size, SpecMode.Exactly);
        }
        if (Dimension.IsMaximumSet(maximum) && maximum < constraint)
            return (maximum, SpecMode.AtMost);
        if (double.IsInfinity(constraint))
            return (0, SpecMode.Unspecified);
        return (constraint, SpecMode.AtMost);
    }

    static double Resolve(double size, SpecMode mode, double content) => mode switch
    {
        SpecMode.Exactly => size,
        SpecMode.AtMost => Math.Min(content, size),
        _ => content,
    };

    public void Arrange(Rect bounds) => ArrangeOverride(bounds);

    /// <summary>MAUI's default: place the frame inside the bounds (<c>ComputeFrame</c>), then lay out the content.</summary>
    protected virtual Size ArrangeOverride(Rect bounds)
    {
        var frame = ((IView)this).ComputeFrame(bounds);
        SetFrame(frame);
        ArrangeContent(new Rect(0, 0, frame.Width, frame.Height));
        return frame.Size;
    }

    /// <summary>Lays out children inside this element's own space.</summary>
    internal virtual void ArrangeContent(Rect bounds)
    {
    }

    void SetFrame(Rect frame)
    {
        var sizeChanged = frame.Width != Width || frame.Height != Height;
        Bounds = frame;
        Width = frame.Width;
        Height = frame.Height;
        if (sizeChanged)
            SizeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Where children are drawn relative to this element's origin (a scroll view's offset).</summary>
    internal virtual Point ChildOffset => Point.Zero;

    /// <summary>The window size of the last layout pass that started at this element.</summary>
    internal Size LayoutWindow { get; set; }

    /// <summary>The children the renderer and the layout pass walk, in drawing order.</summary>
    internal virtual IEnumerable<VisualElement> VisualChildren => [];

    /// <summary>Marks this element and every ancestor for a new layout pass, and asks for a redraw.</summary>
    internal void InvalidateLayout()
    {
        for (Element? e = this; e is not null; e = e.Parent)
            if (e is VisualElement visual)
                visual.NeedsLayout = true;
        Invalidate();
    }

    /// <summary>MAUI's request for a new measure of this element.</summary>
    protected void InvalidateMeasure() => InvalidateLayout();

    // MAUI's internal form, which its FlexLayout calls.
    internal void InvalidateMeasureInternal(InvalidationTrigger trigger) => InvalidateLayout();

    /// <summary>Asks the renderer to draw this element again.</summary>
    internal void Invalidate()
    {
        IsDirty = true;
        Invalidated?.Invoke(this);
    }

    /// <summary>True when this element must be drawn again (renderer bookkeeping).</summary>
    internal bool IsDirty { get; set; } = true;

    /// <summary>The window area, in pixels, this element covered when last drawn (empty when it drew nothing).</summary>
    internal SKRectI LastDrawn { get; set; }

    /// <summary>The window area this element and everything under it covered when last drawn: drawing skips a subtree outside it.</summary>
    internal SKRectI SubtreeDrawn { get; set; }

    /// <summary>
    /// Draws this element's own content, before its children, in its own pixel space:
    /// (0, 0) is its top-left corner and <paramref name="size"/> is its size in pixels.
    /// </summary>
    internal virtual void Draw(SKCanvas canvas, SKSize size, float scale)
    {
        if (BackgroundPaint.Create(Background, BackgroundColor, size) is { } paint)
            using (paint)
                canvas.DrawRect(SKRect.Create(size), paint);
    }

    /// <summary>
    /// Work <see cref="Draw"/> needs done first (caches, a canvas view's PaintSurface), on the UI thread just before
    /// a frame draws this element: a frame may draw on several threads at once, so <see cref="Draw"/> only reads.
    /// </summary>
    internal virtual void Prepare(SKSize size, float scale)
    {
    }

    /// <summary>The draw profile's name for <see cref="Prepare"/>.</summary>
    internal virtual string PrepareName => "prepare " + GetType().Name;

    /// <summary>When set, children are clipped to this path (in this element's pixel space).</summary>
    internal virtual SKPath? ChildClip(SKSize size) => null;

    /// <summary>True when children outside this element's bounds are not shown (and so cannot be touched there).</summary>
    internal virtual bool ClipsChildren => false;

    /// <summary>
    /// True when <see cref="Draw"/> applies <see cref="Opacity"/> itself, so the renderer needs no layer: a leaf whose
    /// drawing is one picture with nothing under it.
    /// </summary>
    internal virtual bool PaintsOwnOpacity => false;

    /// <summary>This element's outline in its pixel space; its shadow takes this shape.</summary>
    internal virtual SKPath ShapePath(SKSize size, float scale)
    {
        var path = new SKPath();
        path.AddRect(SKRect.Create(size));
        return path;
    }

    /// <summary>How far, in units, this element may draw outside its bounds (a shadow).</summary>
    internal virtual double Overdraw => Shadow is { } shadow ? shadow.Radius + Math.Max(Math.Abs(shadow.Offset.X), Math.Abs(shadow.Offset.Y)) : 0;

    internal override void OnBindablePropertyChanged(BindableProperty property, object? oldValue, object? newValue)
    {
        base.OnBindablePropertyChanged(property, oldValue, newValue);
        if (property == ZIndexProperty && Parent is Layout layout)
        {
            layout.ChildOrderChanged();
            layout.Invalidate();
        }
        if (property.AffectsMeasure)
            InvalidateLayout();
        else
            Invalidate();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        InvalidateLayout();
    }

    // MAUI's IView, as the layout managers read it.
    Thickness IView.Margin => this is View view ? view.Margin : Thickness.Zero;
    double IView.Width => WidthRequest >= 0 ? WidthRequest : Dimension.Unset;
    double IView.Height => HeightRequest >= 0 ? HeightRequest : Dimension.Unset;
    double IView.MinimumWidth => MinimumWidthRequest >= 0 ? MinimumWidthRequest : Dimension.Unset;
    double IView.MinimumHeight => MinimumHeightRequest >= 0 ? MinimumHeightRequest : Dimension.Unset;
    double IView.MaximumWidth => MaximumWidthRequest;
    double IView.MaximumHeight => MaximumHeightRequest;
    LayoutAlignment IView.HorizontalLayoutAlignment => this is View view ? view.HorizontalOptions.Alignment : LayoutAlignment.Fill;
    LayoutAlignment IView.VerticalLayoutAlignment => this is View view ? view.VerticalOptions.Alignment : LayoutAlignment.Fill;
    Visibility IView.Visibility => IsVisible ? Visibility.Visible : Visibility.Collapsed;
    Size IView.Arrange(Rect bounds) => ArrangeOverride(bounds);
}
