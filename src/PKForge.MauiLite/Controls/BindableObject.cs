using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.Controls;

/// <summary>MAUI's base for objects with bindable properties.</summary>
public abstract class BindableObject : INotifyPropertyChanged
{
    public static readonly BindableProperty BindingContextProperty = BindableProperty.Create(nameof(BindingContext), typeof(object), typeof(BindableObject), null,
        propertyChanged: (bindable, _, _) => bindable.OnBindingContextUpdated());

    readonly Dictionary<BindableProperty, object?> _values = [];
    Dictionary<BindableProperty, BindingExpression>? _bindings;
    object? _inheritedContext;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event PropertyChangingEventHandler? PropertyChanging;
    public event EventHandler? BindingContextChanged;

    /// <summary>MauiLite has one UI thread, so every object shares its dispatcher.</summary>
    public Microsoft.Maui.Dispatching.IDispatcher Dispatcher => PKForge.MauiLite.UiLoop.Current;

    /// <summary>This object's own binding context, or else the one it inherits from its parent.</summary>
    public object? BindingContext
    {
        get => IsSet(BindingContextProperty) ? GetValue(BindingContextProperty) : _inheritedContext;
        set => SetValue(BindingContextProperty, value);
    }

    public static void SetInheritedBindingContext(BindableObject bindable, object? value)
    {
        ArgumentNullException.ThrowIfNull(bindable);
        bindable.InheritBindingContext(value);
    }

    internal void InheritBindingContext(object? context)
    {
        if (IsSet(BindingContextProperty) || ReferenceEquals(_inheritedContext, context))
        {
            _inheritedContext = context;
            return;
        }
        _inheritedContext = context;
        OnBindingContextUpdated();
    }

    /// <summary>The objects that inherit this one's binding context.</summary>
    internal virtual IEnumerable<BindableObject> InheritingChildren => [];

    void OnBindingContextUpdated()
    {
        if (_bindings is not null)
            foreach (var binding in _bindings.Values.ToArray())
                binding.Apply();
        OnBindingContextChanged();
        var context = BindingContext;
        foreach (var child in InheritingChildren)
            child.InheritBindingContext(context);
    }

    protected virtual void OnBindingContextChanged() => BindingContextChanged?.Invoke(this, EventArgs.Empty);

    public void SetBinding(BindableProperty targetProperty, BindingBase binding)
    {
        ArgumentNullException.ThrowIfNull(targetProperty);
        ArgumentNullException.ThrowIfNull(binding);
        RemoveBinding(targetProperty);
        var expression = new BindingExpression(this, targetProperty, binding);
        (_bindings ??= [])[targetProperty] = expression;
        expression.Apply();
    }

    public void RemoveBinding(BindableProperty property)
    {
        if (_bindings is not null && _bindings.Remove(property, out var expression))
            expression.Detach();
    }

    public bool IsBound(BindableProperty property) => _bindings?.ContainsKey(property) == true;

    public object GetValue(BindableProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (_values.TryGetValue(property, out var value))
            return value!;
        if (property.DefaultValueCreator is { } create)
        {
            // MAUI keeps a created default, so a collection made here is the one later edits reach.
            var created = create(this);
            _values[property] = created;
            return created;
        }
        return property.DefaultValue!;
    }

    public void SetValue(BindableProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (property.IsReadOnly)
            throw new InvalidOperationException($"{property.PropertyName} is read-only; set it through its key.");
        SetValueCore(property, value);
    }

    public void SetValue(BindablePropertyKey propertyKey, object? value)
    {
        ArgumentNullException.ThrowIfNull(propertyKey);
        SetValueCore(propertyKey.BindableProperty, value);
    }

    public void ClearValue(BindableProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!_values.ContainsKey(property))
            return;
        var old = GetValue(property);
        OnPropertyChanging(property.PropertyName);
        _values.Remove(property);
        var now = GetValue(property);
        if (Equals(old, now))
            return;
        OnPropertyChanged(property.PropertyName);
        property.PropertyChanged?.Invoke(this, old!, now!);
        OnBindablePropertyChanged(property, old, now);
    }

    public bool IsSet(BindableProperty property) => _values.ContainsKey(property);

    void SetValueCore(BindableProperty property, object? value)
    {
        value = property.ConvertValue(value);
        if (property.ValidateValue is { } validate && !validate(this, value!))
            throw new ArgumentException($"Value is an invalid value for {property.PropertyName}", nameof(value));
        if (property.CoerceValue is { } coerce)
            value = coerce(this, value!);
        var old = GetValue(property);
        var wasSet = _values.ContainsKey(property);
        if (wasSet && Equals(old, value))
            return;
        property.PropertyChanging?.Invoke(this, old!, value!);
        OnPropertyChanging(property.PropertyName);
        _values[property] = value;
        if (Equals(old, value))
            return;
        OnPropertyChanged(property.PropertyName);
        property.PropertyChanged?.Invoke(this, old!, value!);
        OnBindablePropertyChanged(property, old, value);
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected virtual void OnPropertyChanging([CallerMemberName] string? propertyName = null) =>
        PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));

    /// <summary>MauiLite's hook after a bindable property changed value (layout and redraw bookkeeping).</summary>
    internal virtual void OnBindablePropertyChanged(BindableProperty property, object? oldValue, object? newValue)
    {
    }
}
