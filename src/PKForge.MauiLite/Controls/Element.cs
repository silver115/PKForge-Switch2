namespace Microsoft.Maui.Controls;

/// <summary>MAUI's element: something with a parent in the tree.</summary>
public abstract partial class Element : BindableObject
{
    Element? _parent;

    public Element? Parent
    {
        get => _parent;
        set
        {
            if (ReferenceEquals(_parent, value))
                return;
            OnParentChanging(new ParentChangingEventArgs(_parent, value));
            _parent = value;
            InheritBindingContext(value?.BindingContext);
            UpdateLoaded();
            OnParentSet();
            ParentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ParentChanged;

    public string? AutomationId { get; set; }
    public string? ClassId { get; set; }
    public string? StyleId { get; set; }

    protected virtual void OnParentSet()
    {
    }

    protected virtual void OnParentChanging(ParentChangingEventArgs args)
    {
    }
}

public sealed class ParentChangingEventArgs(Element? oldParent, Element? newParent) : EventArgs
{
    public Element? OldParent { get; } = oldParent;
    public Element? NewParent { get; } = newParent;
}
