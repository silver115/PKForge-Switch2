using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using PKForge.MauiLite;

namespace Microsoft.Maui.Controls;

public interface IValueConverter
{
    object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);
    object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture);
}

public abstract class BindingBase
{
    public BindingMode Mode { get; set; }
    public string? StringFormat { get; set; }
    public object? FallbackValue { get; set; }
    public object? TargetNullValue { get; set; }
}

/// <summary>MAUI's binding: a property path on the binding context (or <see cref="Source"/>).</summary>
public sealed class Binding : BindingBase
{
    public const string SelfPath = ".";

    public Binding()
    {
    }

    public Binding(string path, BindingMode mode = BindingMode.Default, IValueConverter? converter = null, object? converterParameter = null,
        string? stringFormat = null, object? source = null)
    {
        Path = path;
        Mode = mode;
        Converter = converter;
        ConverterParameter = converterParameter;
        StringFormat = stringFormat;
        Source = source;
    }

    public string Path { get; set; } = SelfPath;
    public IValueConverter? Converter { get; set; }
    public object? ConverterParameter { get; set; }
    public object? Source { get; set; }
}

public static class BindableObjectExtensions
{
    public static void SetBinding(this BindableObject self, BindableProperty targetProperty, string path, BindingMode mode = BindingMode.Default,
        IValueConverter? converter = null, string? stringFormat = null)
    {
        ArgumentNullException.ThrowIfNull(self);
        self.SetBinding(targetProperty, new Binding(path, mode, converter, stringFormat: stringFormat));
    }
}

/// <summary>
/// One live binding. It listens to every object along its path, through a weak proxy, so a page that is gone
/// is not kept alive by a long-lived view model. A change raised off the UI thread is applied on it.
/// </summary>
sealed class BindingExpression
{
    readonly BindableObject _target;
    readonly BindableProperty _property;
    readonly BindingBase _binding;
    readonly string[] _parts;
    readonly List<(INotifyPropertyChanged Source, PropertyChangedEventHandler Handler)> _listening = [];
    object? _owner;
    PropertyInfo? _leaf;
    bool _applying;
    bool _targetWatched;

    public BindingExpression(BindableObject target, BindableProperty property, BindingBase binding)
    {
        _target = target;
        _property = property;
        _binding = binding;
        var path = (binding as Binding)?.Path ?? Binding.SelfPath;
        _parts = path is "" or Binding.SelfPath ? [] : path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    BindingMode Mode => _binding.Mode == BindingMode.Default ? _property.DefaultBindingMode : _binding.Mode;

    public void Apply()
    {
        Unlisten();
        var mode = Mode;
        object? value = (_binding as Binding)?.Source ?? _target.BindingContext;
        var resolved = true;
        _owner = null;
        _leaf = null;
        foreach (var part in _parts)
        {
            if (value is null)
            {
                resolved = false;
                break;
            }
            if (mode != BindingMode.OneTime && value is INotifyPropertyChanged notifying)
                Listen(notifying);
            _owner = value;
            _leaf = value.GetType().GetProperty(part, BindingFlags.Public | BindingFlags.Instance);
            if (_leaf is null)
            {
                resolved = false;
                value = null;
                break;
            }
            value = _leaf.GetValue(value);
        }
        if (mode is BindingMode.TwoWay or BindingMode.OneWayToSource && !_targetWatched)
        {
            _target.PropertyChanged += OnTargetChanged;
            _targetWatched = true;
        }
        if (mode == BindingMode.OneWayToSource)
        {
            WriteBack();
            return;
        }
        var final = resolved ? ToTarget(value) : _binding.FallbackValue ?? _property.DefaultValue;
        _applying = true;
        try
        {
            _target.SetValue(_property, final);
        }
        finally
        {
            _applying = false;
        }
    }

    object? ToTarget(object? value)
    {
        if (_binding is Binding { Converter: { } converter } binding)
            value = converter.Convert(value, _property.ReturnType, binding.ConverterParameter, CultureInfo.CurrentUICulture);
        if (value is null)
            return _binding.TargetNullValue ?? _property.DefaultValue;
        if (_binding.StringFormat is { } format)
            value = string.Format(CultureInfo.CurrentCulture, format, value);
        return Coerce(value, _property.ReturnType) ?? _property.DefaultValue;
    }

    static object? Coerce(object? value, Type type)
    {
        if (value is null || type.IsInstanceOfType(value))
            return value;
        if (type == typeof(string))
            return value.ToString();
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        try
        {
            return underlying.IsEnum ? Enum.ToObject(underlying, value) : System.Convert.ChangeType(value, underlying, CultureInfo.CurrentCulture);
        }
        catch (Exception error) when (error is InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
    }

    void OnTargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_applying && e.PropertyName == _property.PropertyName)
            WriteBack();
    }

    void WriteBack()
    {
        if (_owner is null || _leaf is not { CanWrite: true } leaf)
            return;
        var value = _target.GetValue(_property);
        if (_binding is Binding { Converter: { } converter } binding)
            value = converter.ConvertBack(value, leaf.PropertyType, binding.ConverterParameter, CultureInfo.CurrentUICulture);
        leaf.SetValue(_owner, Coerce(value, leaf.PropertyType));
    }

    void SourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) && Array.IndexOf(_parts, e.PropertyName) < 0)
            return;
        var loop = UiLoop.Current;
        if (loop.IsAttached && !loop.IsUiThread)
            loop.Post(Apply);
        else
            Apply();
    }

    void Listen(INotifyPropertyChanged source)
    {
        var proxy = new WeakProxy(this);
        source.PropertyChanged += proxy.OnChanged;
        _listening.Add((source, proxy.OnChanged));
    }

    void Unlisten()
    {
        foreach (var (source, handler) in _listening)
            source.PropertyChanged -= handler;
        _listening.Clear();
    }

    public void Detach()
    {
        Unlisten();
        if (_targetWatched)
            _target.PropertyChanged -= OnTargetChanged;
        _targetWatched = false;
    }

    sealed class WeakProxy(BindingExpression owner)
    {
        readonly WeakReference<BindingExpression> _owner = new(owner);

        public void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_owner.TryGetTarget(out var expression))
                expression.SourceChanged(sender, e);
            else if (sender is INotifyPropertyChanged source)
                source.PropertyChanged -= OnChanged;
        }
    }
}
