using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.Controls;

/// <summary>Values set on an element while something holds, and put back when it stops holding.</summary>
sealed class SetterScope
{
    readonly Dictionary<BindableProperty, (bool WasSet, object? Value)> _saved = [];

    public static SetterScope Apply(BindableObject target, IEnumerable<Setter> setters)
    {
        var scope = new SetterScope();
        foreach (var setter in setters)
            if (setter.Property is { } property)
            {
                scope._saved.TryAdd(property, (target.IsSet(property), target.GetValue(property)));
                target.SetValue(property, setter.Value);
            }
        return scope;
    }

    public void Restore(BindableObject target)
    {
        foreach (var (property, (wasSet, value)) in _saved)
            if (wasSet)
                target.SetValue(property, value);
            else
                target.ClearValue(property);
    }
}

/// <summary>MAUI's trigger base: setters applied while a condition holds.</summary>
public abstract class TriggerBase : BindableObject
{
    private protected TriggerBase(Type targetType) => TargetType = targetType;

    public Type TargetType { get; }

    public IList<Setter> Setters { get; } = new List<Setter>();

    internal abstract void Attach(VisualElement element);

    internal abstract void Detach();
}

/// <summary>MAUI's data trigger: its setters apply while its binding (on the element's context) equals <see cref="Value"/>.</summary>
public sealed class DataTrigger(Type targetType) : TriggerBase(targetType)
{
    VisualElement? _element;
    Probe? _probe;
    SetterScope? _applied;

    public BindingBase? Binding { get; set; }

    public object? Value { get; set; }

    internal override void Attach(VisualElement element)
    {
        _element = element;
        _probe = new Probe { BindingContext = element.BindingContext };
        _probe.PropertyChanged += OnProbeChanged;
        element.BindingContextChanged += OnContextChanged;
        if (Binding is { } binding)
            _probe.SetBinding(Probe.ValueProperty, binding);
        Evaluate();
    }

    internal override void Detach()
    {
        if (_element is null || _probe is null)
            return;
        _applied?.Restore(_element);
        _applied = null;
        _element.BindingContextChanged -= OnContextChanged;
        _probe.PropertyChanged -= OnProbeChanged;
        _probe.RemoveBinding(Probe.ValueProperty);
        _element = null;
        _probe = null;
    }

    void OnContextChanged(object? sender, EventArgs e)
    {
        if (_probe is not null && _element is not null)
            _probe.BindingContext = _element.BindingContext;
    }

    void OnProbeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == Probe.ValueProperty.PropertyName)
            Evaluate();
    }

    void Evaluate()
    {
        if (_element is null || _probe is null)
            return;
        var matches = Matches(_probe.GetValue(Probe.ValueProperty), Value);
        if (matches && _applied is null)
            _applied = SetterScope.Apply(_element, Setters);
        else if (!matches && _applied is not null)
        {
            _applied.Restore(_element);
            _applied = null;
        }
    }

    // As MAUI: equal values match, and so do values that read the same as text ("True" and true).
    static bool Matches(object? actual, object? expected) =>
        Equals(actual, expected)
        || actual is not null && expected is not null
           && string.Equals(Convert.ToString(actual, CultureInfo.InvariantCulture), Convert.ToString(expected, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

    sealed class Probe : BindableObject
    {
        public static readonly BindableProperty ValueProperty = BindableProperty.Create("Value", typeof(object), typeof(Probe), null);
    }
}

/// <summary>MAUI's visual states: named sets of setters, one state at a time in each group.</summary>
public static class VisualStateManager
{
    static readonly ConditionalWeakTable<VisualElement, VisualStateGroupList> Groups = new();

    public static class CommonStates
    {
        public const string Normal = "Normal";
        public const string Disabled = "Disabled";
        public const string Focused = "Focused";
        public const string Selected = "Selected";
        public const string PointerOver = "PointerOver";
    }

    public static void SetVisualStateGroups(VisualElement visualElement, VisualStateGroupList value)
    {
        ArgumentNullException.ThrowIfNull(visualElement);
        Groups.AddOrUpdate(visualElement, value);
        GoToState(visualElement, CommonStates.Normal);
    }

    public static IList<VisualStateGroup> GetVisualStateGroups(VisualElement visualElement) =>
        Groups.GetValue(visualElement, _ => []);

    public static bool HasVisualStateGroups(VisualElement element) => Groups.TryGetValue(element, out var groups) && groups.Count > 0;

    public static bool GoToState(VisualElement visualElement, string name)
    {
        if (!Groups.TryGetValue(visualElement, out var groups))
            return false;
        var found = false;
        foreach (var group in groups)
            if (group.States.FirstOrDefault(state => state.Name == name) is { } state)
            {
                found = true;
                group.GoTo(visualElement, state);
            }
        return found;
    }
}

public class VisualStateGroupList : Collection<VisualStateGroup>
{
}

public sealed class VisualStateGroup
{
    SetterScope? _applied;

    public string? Name { get; set; }

    public IList<VisualState> States { get; } = new List<VisualState>();

    public VisualState? CurrentState { get; private set; }

    internal void GoTo(VisualElement element, VisualState state)
    {
        if (ReferenceEquals(CurrentState, state))
            return;
        _applied?.Restore(element);
        _applied = SetterScope.Apply(element, state.Setters);
        CurrentState = state;
    }
}

public sealed class VisualState
{
    public string? Name { get; set; }

    public IList<Setter> Setters { get; } = new List<Setter>();

    public Type? TargetType { get; set; }
}
