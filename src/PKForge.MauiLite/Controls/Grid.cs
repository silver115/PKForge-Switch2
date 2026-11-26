using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Layouts;

namespace Microsoft.Maui.Controls;

/// <summary>MAUI's grid, laid out by MAUI's <see cref="GridLayoutManager"/>.</summary>
public class Grid : Layout, IGridLayout
{
    public static readonly BindableProperty RowProperty = BindableProperty.CreateAttached("Row", typeof(int), typeof(Grid), 0,
        validateValue: (_, value) => (int)value >= 0).Measured();
    public static readonly BindableProperty ColumnProperty = BindableProperty.CreateAttached("Column", typeof(int), typeof(Grid), 0,
        validateValue: (_, value) => (int)value >= 0).Measured();
    public static readonly BindableProperty RowSpanProperty = BindableProperty.CreateAttached("RowSpan", typeof(int), typeof(Grid), 1,
        validateValue: (_, value) => (int)value >= 1).Measured();
    public static readonly BindableProperty ColumnSpanProperty = BindableProperty.CreateAttached("ColumnSpan", typeof(int), typeof(Grid), 1,
        validateValue: (_, value) => (int)value >= 1).Measured();
    public static readonly BindableProperty RowSpacingProperty = BindableProperty.Create(nameof(RowSpacing), typeof(double), typeof(Grid), 0d).Measured();
    public static readonly BindableProperty ColumnSpacingProperty = BindableProperty.Create(nameof(ColumnSpacing), typeof(double), typeof(Grid), 0d).Measured();
    public static readonly BindableProperty RowDefinitionsProperty = BindableProperty.Create(nameof(RowDefinitions), typeof(RowDefinitionCollection), typeof(Grid), null,
        propertyChanged: (bindable, old, now) => ((Grid)bindable).Watch((INotifyCollectionChanged?)old, (INotifyCollectionChanged?)now),
        defaultValueCreator: bindable => ((Grid)bindable).Watched(new RowDefinitionCollection())).Measured();
    public static readonly BindableProperty ColumnDefinitionsProperty = BindableProperty.Create(nameof(ColumnDefinitions), typeof(ColumnDefinitionCollection), typeof(Grid), null,
        propertyChanged: (bindable, old, now) => ((Grid)bindable).Watch((INotifyCollectionChanged?)old, (INotifyCollectionChanged?)now),
        defaultValueCreator: bindable => ((Grid)bindable).Watched(new ColumnDefinitionCollection())).Measured();

    public RowDefinitionCollection RowDefinitions { get => (RowDefinitionCollection)GetValue(RowDefinitionsProperty)!; set => SetValue(RowDefinitionsProperty, value); }
    public ColumnDefinitionCollection ColumnDefinitions { get => (ColumnDefinitionCollection)GetValue(ColumnDefinitionsProperty)!; set => SetValue(ColumnDefinitionsProperty, value); }
    public double RowSpacing { get => (double)GetValue(RowSpacingProperty)!; set => SetValue(RowSpacingProperty, value); }
    public double ColumnSpacing { get => (double)GetValue(ColumnSpacingProperty)!; set => SetValue(ColumnSpacingProperty, value); }

#nullable disable // MAUI declares these without nullable annotations
    public static int GetRow(BindableObject bindable) => (int)bindable.GetValue(RowProperty)!;
    public static int GetColumn(BindableObject bindable) => (int)bindable.GetValue(ColumnProperty)!;
    public static int GetRowSpan(BindableObject bindable) => (int)bindable.GetValue(RowSpanProperty)!;
    public static int GetColumnSpan(BindableObject bindable) => (int)bindable.GetValue(ColumnSpanProperty)!;
    public static void SetRow(BindableObject bindable, int value) => bindable.SetValue(RowProperty, value);
    public static void SetColumn(BindableObject bindable, int value) => bindable.SetValue(ColumnProperty, value);
    public static void SetRowSpan(BindableObject bindable, int value) => bindable.SetValue(RowSpanProperty, value);
    public static void SetColumnSpan(BindableObject bindable, int value) => bindable.SetValue(ColumnSpanProperty, value);

    public void SetRow(IView view, int row) => SetRow((BindableObject)view, row);
    public void SetColumn(IView view, int column) => SetColumn((BindableObject)view, column);
    public void SetRowSpan(IView view, int span) => SetRowSpan((BindableObject)view, span);
    public void SetColumnSpan(IView view, int span) => SetColumnSpan((BindableObject)view, span);
    public int GetRow(IView view) => GetRow((BindableObject)view);
    public int GetColumn(IView view) => GetColumn((BindableObject)view);
    public int GetRowSpan(IView view) => GetRowSpan((BindableObject)view);
    public int GetColumnSpan(IView view) => GetColumnSpan((BindableObject)view);
#nullable restore

    IReadOnlyList<IGridRowDefinition> IGridLayout.RowDefinitions => RowDefinitions;
    IReadOnlyList<IGridColumnDefinition> IGridLayout.ColumnDefinitions => ColumnDefinitions;

    protected override ILayoutManager CreateLayoutManager() => new GridLayoutManager(this);

    T Watched<T>(T collection) where T : INotifyCollectionChanged
    {
        Watch(null, collection);
        return collection;
    }

    // A definition added, removed or resized lays the grid out again.
    void Watch(INotifyCollectionChanged? old, INotifyCollectionChanged? now)
    {
        if (old is not null)
            old.CollectionChanged -= OnDefinitionsChanged;
        if (old is IDefinitionCollection oldDefinitions)
            oldDefinitions.ItemSizeChanged -= OnDefinitionSizeChanged;
        if (now is not null)
            now.CollectionChanged += OnDefinitionsChanged;
        if (now is IDefinitionCollection nowDefinitions)
            nowDefinitions.ItemSizeChanged += OnDefinitionSizeChanged;
    }

    void OnDefinitionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateLayout();

    void OnDefinitionSizeChanged(object? sender, EventArgs e) => InvalidateLayout();
}

/// <summary>MAUI's <c>Grid.Add(view, column, row)</c> extensions.</summary>
public static class GridExtensions
{
    public static void Add(this Grid grid, IView view, int column = 0, int row = 0)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.SetColumn(view, column);
        grid.SetRow(view, row);
        grid.Add(view);
    }

    public static void AddWithSpan(this Grid grid, IView view, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.SetRow(view, row);
        grid.SetColumn(view, column);
        grid.SetRowSpan(view, rowSpan);
        grid.SetColumnSpan(view, columnSpan);
        grid.Add(view);
    }
}

public sealed class RowDefinition : BindableObject, IGridRowDefinition
{
    public static readonly BindableProperty HeightProperty = BindableProperty.Create(nameof(Height), typeof(GridLength), typeof(RowDefinition), GridLength.Star);

    public RowDefinition()
    {
    }

    public RowDefinition(GridLength height) => Height = height;

    public GridLength Height { get => (GridLength)GetValue(HeightProperty)!; set => SetValue(HeightProperty, value); }
}

public sealed class ColumnDefinition : BindableObject, IGridColumnDefinition
{
    public static readonly BindableProperty WidthProperty = BindableProperty.Create(nameof(Width), typeof(GridLength), typeof(ColumnDefinition), GridLength.Star);

    public ColumnDefinition()
    {
    }

    public ColumnDefinition(GridLength width) => Width = width;

    public GridLength Width { get => (GridLength)GetValue(WidthProperty)!; set => SetValue(WidthProperty, value); }
}

interface IDefinitionCollection
{
    event EventHandler? ItemSizeChanged;
}

/// <summary>MAUI's row or column definition list; it reports a definition's size change.</summary>
public class DefinitionCollection<T> : ObservableCollection<T>, IDefinitionCollection where T : BindableObject
{
    protected DefinitionCollection()
    {
    }

    protected DefinitionCollection(params T[] definitions)
    {
        foreach (var definition in definitions)
            Add(definition);
    }

    public event EventHandler? ItemSizeChanged;

    protected override void InsertItem(int index, T item)
    {
        base.InsertItem(index, item);
        item.PropertyChanged += OnItemChanged;
    }

    protected override void SetItem(int index, T item)
    {
        this[index].PropertyChanged -= OnItemChanged;
        base.SetItem(index, item);
        item.PropertyChanged += OnItemChanged;
    }

    protected override void RemoveItem(int index)
    {
        this[index].PropertyChanged -= OnItemChanged;
        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        foreach (var item in this)
            item.PropertyChanged -= OnItemChanged;
        base.ClearItems();
    }

    void OnItemChanged(object? sender, PropertyChangedEventArgs e) => ItemSizeChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RowDefinitionCollection : DefinitionCollection<RowDefinition>
{
    public RowDefinitionCollection()
    {
    }

    public RowDefinitionCollection(params RowDefinition[] definitions) : base(definitions)
    {
    }
}

public sealed class ColumnDefinitionCollection : DefinitionCollection<ColumnDefinition>
{
    public ColumnDefinitionCollection()
    {
    }

    public ColumnDefinitionCollection(params ColumnDefinition[] definitions) : base(definitions)
    {
    }
}
