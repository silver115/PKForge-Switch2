namespace Microsoft.Maui.Controls;

public enum BindingMode { Default, TwoWay, OneWay, OneWayToSource, OneTime }

/// <summary>MAUI's bindable property: a named, typed value slot on a <see cref="BindableObject"/>.</summary>
public sealed class BindableProperty
{
    public delegate void BindingPropertyChangedDelegate(BindableObject bindable, object oldValue, object newValue);
    public delegate void BindingPropertyChangingDelegate(BindableObject bindable, object oldValue, object newValue);
    public delegate object CoerceValueDelegate(BindableObject bindable, object value);
    public delegate bool ValidateValueDelegate(BindableObject bindable, object value);
    public delegate object CreateDefaultValueDelegate(BindableObject bindable);

    public static readonly object UnsetValue = new();

    BindableProperty(string propertyName, Type returnType, Type declaringType, object? defaultValue, BindingMode defaultBindingMode,
        ValidateValueDelegate? validateValue, BindingPropertyChangedDelegate? propertyChanged, BindingPropertyChangingDelegate? propertyChanging,
        CoerceValueDelegate? coerceValue, CreateDefaultValueDelegate? defaultValueCreator, bool isReadOnly)
    {
        PropertyName = propertyName;
        ReturnType = returnType;
        DeclaringType = declaringType;
        // A value type with no default gets default(T), as in MAUI.
        DefaultValue = defaultValue ?? (returnType.IsValueType ? Activator.CreateInstance(returnType) : null);
        DefaultBindingMode = defaultBindingMode == BindingMode.Default ? BindingMode.OneWay : defaultBindingMode;
        ValidateValue = validateValue;
        PropertyChanged = propertyChanged;
        PropertyChanging = propertyChanging;
        CoerceValue = coerceValue;
        DefaultValueCreator = defaultValueCreator;
        IsReadOnly = isReadOnly;
    }

    public string PropertyName { get; }
    public Type ReturnType { get; }
    public Type DeclaringType { get; }
    public object? DefaultValue { get; }
    public BindingMode DefaultBindingMode { get; }
    public bool IsReadOnly { get; }

    internal ValidateValueDelegate? ValidateValue { get; }
    internal BindingPropertyChangedDelegate? PropertyChanged { get; }
    internal BindingPropertyChangingDelegate? PropertyChanging { get; }
    internal CoerceValueDelegate? CoerceValue { get; }
    internal CreateDefaultValueDelegate? DefaultValueCreator { get; }

    /// <summary>
    /// The value as this property's type, converted as MAUI converts it: implicit operators (a Color to a Brush,
    /// a double to a GridLength or Thickness), enums from numbers, and numbers between numeric types.
    /// </summary>
    internal object? ConvertValue(object? value)
    {
        if (value is null || ReturnType.IsInstanceOfType(value))
            return value;
        var target = Nullable.GetUnderlyingType(ReturnType) ?? ReturnType;
        if (target.IsInstanceOfType(value))
            return value;
        foreach (var owner in new[] { target, value.GetType() })
            foreach (var method in owner.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                if (method.Name == "op_Implicit" && method.ReturnType == target && method.GetParameters() is [var parameter] && parameter.ParameterType.IsInstanceOfType(value))
                    return method.Invoke(null, [value]);
        try
        {
            if (target.IsEnum && value is IConvertible)
                return Enum.ToObject(target, value);
            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(target))
                return Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception error) when (error is InvalidCastException or FormatException or OverflowException)
        {
        }
        throw new ArgumentException($"A {value.GetType().Name} is not a {ReturnType.Name}, the type of {PropertyName}.", nameof(value));
    }

    /// <summary>Set for properties whose change needs a new layout pass (MauiLite's own bookkeeping).</summary>
    internal bool AffectsMeasure { get; private set; }

    internal BindableProperty Measured()
    {
        AffectsMeasure = true;
        return this;
    }

    public static BindableProperty Create(string propertyName, Type returnType, Type declaringType, object? defaultValue = null,
        BindingMode defaultBindingMode = BindingMode.OneWay, ValidateValueDelegate? validateValue = null,
        BindingPropertyChangedDelegate? propertyChanged = null, BindingPropertyChangingDelegate? propertyChanging = null,
        CoerceValueDelegate? coerceValue = null, CreateDefaultValueDelegate? defaultValueCreator = null) =>
        new(propertyName, returnType, declaringType, defaultValue, defaultBindingMode, validateValue, propertyChanged, propertyChanging,
            coerceValue, defaultValueCreator, isReadOnly: false);

    public static BindableProperty CreateAttached(string propertyName, Type returnType, Type declaringType, object? defaultValue = null,
        BindingMode defaultBindingMode = BindingMode.OneWay, ValidateValueDelegate? validateValue = null,
        BindingPropertyChangedDelegate? propertyChanged = null, BindingPropertyChangingDelegate? propertyChanging = null,
        CoerceValueDelegate? coerceValue = null, CreateDefaultValueDelegate? defaultValueCreator = null) =>
        Create(propertyName, returnType, declaringType, defaultValue, defaultBindingMode, validateValue, propertyChanged, propertyChanging,
            coerceValue, defaultValueCreator);

    public static BindablePropertyKey CreateReadOnly(string propertyName, Type returnType, Type declaringType, object? defaultValue = null,
        BindingMode defaultBindingMode = BindingMode.OneWayToSource, ValidateValueDelegate? validateValue = null,
        BindingPropertyChangedDelegate? propertyChanged = null, BindingPropertyChangingDelegate? propertyChanging = null,
        CoerceValueDelegate? coerceValue = null, CreateDefaultValueDelegate? defaultValueCreator = null) =>
        new(new BindableProperty(propertyName, returnType, declaringType, defaultValue, defaultBindingMode, validateValue, propertyChanged,
            propertyChanging, coerceValue, defaultValueCreator, isReadOnly: true));

    public static BindablePropertyKey CreateAttachedReadOnly(string propertyName, Type returnType, Type declaringType, object? defaultValue = null,
        BindingMode defaultBindingMode = BindingMode.OneWayToSource, ValidateValueDelegate? validateValue = null,
        BindingPropertyChangedDelegate? propertyChanged = null, BindingPropertyChangingDelegate? propertyChanging = null,
        CoerceValueDelegate? coerceValue = null, CreateDefaultValueDelegate? defaultValueCreator = null) =>
        CreateReadOnly(propertyName, returnType, declaringType, defaultValue, defaultBindingMode, validateValue, propertyChanged,
            propertyChanging, coerceValue, defaultValueCreator);
}

public sealed class BindablePropertyKey
{
    internal BindablePropertyKey(BindableProperty property) => BindableProperty = property;

    public BindableProperty BindableProperty { get; }
}
