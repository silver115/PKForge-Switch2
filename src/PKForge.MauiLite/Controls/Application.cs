using System.Collections;

namespace Microsoft.Maui.Controls;

public interface IActivationState
{
}

/// <summary>MAUI's application: resources, windows and life-cycle callbacks.</summary>
public class Application : Element, IApplication
{
    readonly List<Window> _windows = [];

    public Application() => Current = this;

    public static Application? Current { get; set; }

    public ResourceDictionary Resources { get; set; } = [];

    public IReadOnlyList<Window> Windows => _windows;

    [Obsolete("MAUI 9 replaces MainPage with CreateWindow.")]
    public Page? MainPage { get; set; }

    /// <summary>Raised when app code calls <see cref="Quit"/>; the host closes the app.</summary>
    internal event EventHandler? QuitRequested;

#pragma warning disable CS0618 // MainPage: the default window shows it, as in MAUI
    protected virtual Window CreateWindow(IActivationState? activationState) =>
        new(MainPage ?? throw new InvalidOperationException("Set MainPage or override CreateWindow."));
#pragma warning restore CS0618

    protected virtual void OnStart()
    {
    }

    protected virtual void OnSleep()
    {
    }

    protected virtual void OnResume()
    {
    }

    public void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Creates the window, shows its page and starts the app, as MAUI does at launch.</summary>
    internal Window OpenWindow()
    {
        var window = CreateWindow(null);
        _windows.Add(window);
        window.Parent = this;
        window.Open();
        OnStart();
        return window;
    }

    internal void SendSleep() => OnSleep();

    internal void SendResume() => OnResume();
}

/// <summary>MAUI's window: holds the root page. MauiLite's one window is the whole screen, in units.</summary>
public class Window : Element
{
    /// <summary>The screen in units: its pixels over the host's scale (set by the runner before the window opens).</summary>
    internal static Microsoft.Maui.Graphics.Size ScreenUnits { get; set; } = new(1280, 720);

    Page? _page;
    bool _open;

    public Window()
    {
    }

    public Window(Page page) => Page = page;

    public string? Title { get; set; }
    public double X => 0;
    public double Y => 0;
    public double Width { get; private set; } = -1;
    public double Height { get; private set; } = -1;

    public event EventHandler? SizeChanged;

    public Page? Page
    {
        get => _page;
        set
        {
            if (ReferenceEquals(_page, value))
                return;
            if (_page is not null)
            {
                if (_open)
                    _page.SendDisappearing();
                _page.Parent = null;
            }
            _page = value;
            if (value is null)
                return;
            value.Parent = this;
            if (_open)
                value.SendAppearing();
        }
    }

    internal void Open()
    {
        _open = true;
        Width = ScreenUnits.Width;
        Height = ScreenUnits.Height;
        SizeChanged?.Invoke(this, EventArgs.Empty);
        _page?.SendAppearing();
    }

    internal override IEnumerable<BindableObject> InheritingChildren => _page is null ? [] : [_page];
}

/// <summary>MAUI's style: values for the properties of one type of element.</summary>
public sealed class Style
{
    public Style(Type targetType) => TargetType = targetType;

    public Type TargetType { get; }
    public IList<Setter> Setters { get; } = new List<Setter>();
    public bool ApplyToDerivedTypes { get; set; }
    public Style? BasedOn { get; set; }

    internal void ApplyTo(BindableObject target)
    {
        BasedOn?.ApplyTo(target);
        foreach (var setter in Setters)
            if (setter.Property is { } property)
                target.SetValue(property, setter.Value);
    }
}

public sealed class Setter
{
    public BindableProperty? Property { get; set; }
    public object? Value { get; set; }
}

/// <summary>MAUI's resource dictionary. A style added without a key is the implicit style for its target type.</summary>
public class ResourceDictionary : IDictionary<string, object>
{
    readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    public ICollection<ResourceDictionary> MergedDictionaries { get; } = [];

    public void Add(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _values[style.TargetType.FullName!] = style;
    }

    public void Add(string key, object value) => _values.Add(key, value);

    public object this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value
            : MergedDictionaries.FirstOrDefault(d => d.ContainsKey(key)) is { } merged ? merged[key]
            : throw new KeyNotFoundException(key);
        set => _values[key] = value;
    }

    public bool TryGetValue(string key, out object value)
    {
        if (_values.TryGetValue(key, out value!))
            return true;
        foreach (var merged in MergedDictionaries)
            if (merged.TryGetValue(key, out value))
                return true;
        return false;
    }

    public bool ContainsKey(string key) => _values.ContainsKey(key) || MergedDictionaries.Any(d => d.ContainsKey(key));
    public bool Remove(string key) => _values.Remove(key);
    public ICollection<string> Keys => _values.Keys;
    public ICollection<object> Values => _values.Values;
    public int Count => _values.Count;
    public bool IsReadOnly => false;
    public void Add(KeyValuePair<string, object> item) => _values.Add(item.Key, item.Value);
    public void Clear() => _values.Clear();
    public bool Contains(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_values).Contains(item);
    public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, object>>)_values).CopyTo(array, arrayIndex);
    public bool Remove(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_values).Remove(item);
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Applies the implicit style for <paramref name="target"/>'s exact type, or one that applies to derived types.</summary>
    internal void ApplyImplicitStyle(BindableObject target)
    {
        for (var type = target.GetType(); type is not null && type != typeof(BindableObject); type = type.BaseType)
            if (TryGetValue(type.FullName!, out var found) && found is Style style && (type == target.GetType() || style.ApplyToDerivedTypes))
            {
                style.ApplyTo(target);
                return;
            }
    }
}
