using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.Controls;

/// <summary>
/// MAUI's bindable layout: a layout gets one child per item of a source, made from a template.
/// shortcut: any change to the source rebuilds every child; upgrade to per-item updates if a large source changes often.
/// </summary>
public static class BindableLayout
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.CreateAttached("ItemsSource", typeof(IEnumerable), typeof(Layout), null,
        propertyChanged: (bindable, old, now) => Watch((Layout)bindable, (IEnumerable?)old, (IEnumerable?)now));
    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.CreateAttached("ItemTemplate", typeof(DataTemplate), typeof(Layout), null,
        propertyChanged: (bindable, _, _) => Rebuild((Layout)bindable));

    static readonly ConditionalWeakTable<Layout, WeakRebuild> Listeners = new();

    public static IEnumerable? GetItemsSource(BindableObject bindable) => (IEnumerable?)bindable.GetValue(ItemsSourceProperty);

    public static void SetItemsSource(BindableObject bindable, IEnumerable? value) => bindable.SetValue(ItemsSourceProperty, value);

    public static DataTemplate? GetItemTemplate(BindableObject bindable) => (DataTemplate?)bindable.GetValue(ItemTemplateProperty);

    public static void SetItemTemplate(BindableObject bindable, DataTemplate? value) => bindable.SetValue(ItemTemplateProperty, value);

    static void Watch(Layout layout, IEnumerable? old, IEnumerable? now)
    {
        if (old is INotifyCollectionChanged oldChanges && Listeners.TryGetValue(layout, out var previous))
            oldChanges.CollectionChanged -= previous.OnChanged;
        if (now is INotifyCollectionChanged nowChanges)
        {
            var listener = new WeakRebuild(layout, nowChanges);
            Listeners.AddOrUpdate(layout, listener);
            nowChanges.CollectionChanged += listener.OnChanged;
        }
        Rebuild(layout);
    }

    static void Rebuild(Layout layout)
    {
        layout.Clear();
        if (GetItemsSource(layout) is not { } items)
            return;
        var template = GetItemTemplate(layout);
        foreach (var item in items)
        {
            var view = template?.CreateContent() as View ?? new Label { Text = item?.ToString() };
            view.BindingContext = item;
            layout.Add(view);
        }
    }

    // The source (often a long-lived view model's collection) holds this weakly, so a page that is gone is not kept alive.
    sealed class WeakRebuild(Layout layout, INotifyCollectionChanged source)
    {
        readonly WeakReference<Layout> _layout = new(layout);

        public void OnChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_layout.TryGetTarget(out var target))
                Rebuild(target);
            else
                source.CollectionChanged -= OnChanged;
        }
    }
}
