using System.Collections;
using System.Collections.Specialized;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Controls;

/// <summary>MAUI's template: makes a view for one item.</summary>
public class DataTemplate
{
    readonly Func<object>? _load;

    public DataTemplate(Func<object> loadTemplate) => _load = loadTemplate;

    public DataTemplate(Type type) => _load = () => Activator.CreateInstance(type)!;

    public object CreateContent() => _load?.Invoke() ?? throw new InvalidOperationException("The template has nothing to create.");
}

public enum SelectionMode { None, Single, Multiple }

public class SelectionChangedEventArgs(IReadOnlyList<object> previousSelection, IReadOnlyList<object> currentSelection) : EventArgs
{
    public IReadOnlyList<object> PreviousSelection { get; } = previousSelection;
    public IReadOnlyList<object> CurrentSelection { get; } = currentSelection;
}

/// <summary>
/// MAUI's items view: one view per item in a vertical list that scrolls.
/// shortcut: every item gets a view at once (no recycling); upgrade if a long list is slow.
/// </summary>
public class ItemsView : View
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(ItemsView), null,
        propertyChanged: (bindable, old, now) => ((ItemsView)bindable).OnSourceChanged((IEnumerable?)old, (IEnumerable?)now));
    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(nameof(ItemTemplate), typeof(DataTemplate), typeof(ItemsView), null,
        propertyChanged: (bindable, _, _) => ((ItemsView)bindable).Rebuild());

    readonly ScrollView _scroll = new();
    readonly VerticalStackLayout _items = new();

    public ItemsView()
    {
        _scroll.Content = _items;
        _scroll.Parent = this;
    }

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public DataTemplate? ItemTemplate { get => (DataTemplate?)GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }

    void OnSourceChanged(IEnumerable? old, IEnumerable? now)
    {
        if (old is INotifyCollectionChanged oldChanges)
            oldChanges.CollectionChanged -= OnCollectionChanged;
        if (now is INotifyCollectionChanged nowChanges)
            nowChanges.CollectionChanged += OnCollectionChanged;
        Rebuild();
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    public void ScrollTo(object item, object? group = null, ScrollToPosition position = ScrollToPosition.MakeVisible, bool animate = true)
    {
        if (ItemViews.FirstOrDefault(view => Equals(view.BindingContext, item)) is { } view)
            _ = _scroll.ScrollToAsync(view, position, animate);
    }

    public void ScrollTo(int index, int groupIndex = -1, ScrollToPosition position = ScrollToPosition.MakeVisible, bool animate = true)
    {
        if (index >= 0 && index < _items.Count && _items[index] is View view)
            _ = _scroll.ScrollToAsync(view, position, animate);
    }

    private protected IEnumerable<View> ItemViews => _items.OfType<View>();

    void Rebuild()
    {
        _items.Clear();
        if (ItemsSource is null)
            return;
        foreach (var item in ItemsSource)
            _items.Add(CreateItemView(item));
        OnItemsRebuilt();
    }

    private protected virtual void OnItemsRebuilt()
    {
    }

    private protected virtual View CreateItemView(object? item)
    {
        var view = ItemTemplate?.CreateContent() as View ?? new Label { Text = item?.ToString() };
        view.BindingContext = item;
        return view;
    }

    internal override Size MeasureNative(double widthConstraint, double heightConstraint) => _scroll.Measure(widthConstraint, heightConstraint);

    internal override void ArrangeContent(Rect bounds) => _scroll.Arrange(bounds);

    internal override IEnumerable<VisualElement> VisualChildren => [_scroll];
}

public class SelectableItemsView : ItemsView
{
    public static readonly BindableProperty SelectionModeProperty = BindableProperty.Create(nameof(SelectionMode), typeof(SelectionMode), typeof(SelectableItemsView), SelectionMode.None);
    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(nameof(SelectedItem), typeof(object), typeof(SelectableItemsView), null, BindingMode.TwoWay,
        propertyChanged: (bindable, old, now) => ((SelectableItemsView)bindable).OnSelectedItemChanged(old, now));

    public SelectionMode SelectionMode { get => (SelectionMode)GetValue(SelectionModeProperty)!; set => SetValue(SelectionModeProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }

    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    void OnSelectedItemChanged(object? old, object? now)
    {
        ShowSelection();
        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(old is null ? [] : [old], now is null ? [] : [now]));
    }

    private protected override void OnItemsRebuilt() => ShowSelection();

    // As MAUI: each item view is in its "Selected" or "Normal" visual state.
    void ShowSelection()
    {
        foreach (var view in ItemViews)
            VisualStateManager.GoToState(view, Equals(view.BindingContext, SelectedItem) ? VisualStateManager.CommonStates.Selected : VisualStateManager.CommonStates.Normal);
    }

    private protected override View CreateItemView(object? item)
    {
        var view = base.CreateItemView(item);
        var select = new TapGestureRecognizer();
        select.Tapped += (_, _) =>
        {
            if (SelectionMode != SelectionMode.None)
                SelectedItem = item;
        };
        view.GestureRecognizers.Add(select);
        return view;
    }
}

public class CollectionView : SelectableItemsView
{
}
