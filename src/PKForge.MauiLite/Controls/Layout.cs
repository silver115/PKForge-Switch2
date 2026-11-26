using System.Collections;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Microsoft.Maui.Controls;

/// <summary>MAUI's layout: a view with children, laid out by one of MAUI's layout managers.</summary>
public abstract class Layout : View, ILayout
{
    public static readonly BindableProperty PaddingProperty = BindableProperty.Create(nameof(Padding), typeof(Thickness), typeof(Layout), Thickness.Zero).Measured();
    public static readonly BindableProperty IsClippedToBoundsProperty = BindableProperty.Create(nameof(IsClippedToBounds), typeof(bool), typeof(Layout), false);
    public static readonly BindableProperty CascadeInputTransparentProperty = BindableProperty.Create(nameof(CascadeInputTransparent), typeof(bool), typeof(Layout), true);

    readonly List<IView> _children = [];

    /// <summary>Created on first use; MAUI's FlexLayout reads it directly.</summary>
    internal ILayoutManager? _layoutManager;

    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty)!; set => SetValue(PaddingProperty, value); }
    public bool IsClippedToBounds { get => (bool)GetValue(IsClippedToBoundsProperty)!; set => SetValue(IsClippedToBoundsProperty, value); }
    public bool CascadeInputTransparent { get => (bool)GetValue(CascadeInputTransparentProperty)!; set => SetValue(CascadeInputTransparentProperty, value); }

    public IList<IView> Children => this;

    public int Count => _children.Count;
    public bool IsReadOnly => false;

    public IView this[int index]
    {
        get => _children[index];
        set
        {
            var old = _children[index];
            Detach(old);
            _children[index] = value;
            Attach(value);
            OnUpdate(index, value, old);
        }
    }

    public void Add(IView child)
    {
        ArgumentNullException.ThrowIfNull(child);
        var index = _children.Count;
        _children.Add(child);
        Attach(child);
        OnAdd(index, child);
    }

    public void Insert(int index, IView child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Insert(index, child);
        Attach(child);
        OnInsert(index, child);
    }

    public bool Remove(IView child)
    {
        var index = _children.IndexOf(child);
        if (index < 0)
            return false;
        RemoveAt(index);
        return true;
    }

    public void RemoveAt(int index)
    {
        var child = _children[index];
        _children.RemoveAt(index);
        Detach(child);
        OnRemove(index, child);
    }

    public void Clear()
    {
        foreach (var child in _children)
            Detach(child);
        _children.Clear();
        OnClear();
    }

    public bool Contains(IView item) => _children.Contains(item);
    public int IndexOf(IView item) => _children.IndexOf(item);
    public void CopyTo(IView[] array, int arrayIndex) => _children.CopyTo(array, arrayIndex);
    public IEnumerator<IView> GetEnumerator() => _children.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    protected virtual void OnAdd(int index, IView view)
    {
    }

    protected virtual void OnInsert(int index, IView view)
    {
    }

    protected virtual void OnUpdate(int index, IView view, IView oldView)
    {
    }

    protected virtual void OnRemove(int index, IView view)
    {
    }

    protected virtual void OnClear()
    {
    }

    protected abstract ILayoutManager CreateLayoutManager();

    public Size CrossPlatformMeasure(double widthConstraint, double heightConstraint) =>
        (_layoutManager ??= CreateLayoutManager()).Measure(widthConstraint, heightConstraint);

    public Size CrossPlatformArrange(Rect bounds) =>
        (_layoutManager ??= CreateLayoutManager()).ArrangeChildren(bounds);

    // Through the interface: FlexLayout re-implements CrossPlatformMeasure, as in MAUI.
    internal override Size MeasureNative(double widthConstraint, double heightConstraint)
    {
        var (width, height) = ContentConstraints(widthConstraint, heightConstraint);
        return ((ILayout)this).CrossPlatformMeasure(width, height);
    }

    internal override void ArrangeContent(Rect bounds) => ((ILayout)this).CrossPlatformArrange(bounds);

    VisualElement[]? _ordered;

    // In drawing order (ZIndex, then order added), kept until a child or a ZIndex changes: every frame walks it several times.
    internal override IEnumerable<VisualElement> VisualChildren => _ordered ??= [.. _children.OfType<VisualElement>().OrderBy(child => child.ZIndex)];

    internal void ChildOrderChanged() => _ordered = null;

    internal override bool ClipsChildren => IsClippedToBounds;

    internal override SkiaSharp.SKPath? ChildClip(SkiaSharp.SKSize size)
    {
        if (!IsClippedToBounds)
            return null;
        var path = new SkiaSharp.SKPath();
        path.AddRect(SkiaSharp.SKRect.Create(size));
        return path;
    }

    void Attach(IView child)
    {
        _ordered = null;
        if (child is Element element)
            element.Parent = this;
        InvalidateLayout();
    }

    void Detach(IView child)
    {
        _ordered = null;
        if (child is Element element && ReferenceEquals(element.Parent, this))
            element.Parent = null;
        InvalidateLayout();
    }
}
