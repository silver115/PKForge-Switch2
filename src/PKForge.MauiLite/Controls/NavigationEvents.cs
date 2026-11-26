namespace Microsoft.Maui.Controls;

public enum NavigationType { Push, Pop, PopToRoot, Insert, Remove, Replace }

public sealed class NavigatedToEventArgs(Page? previousPage, NavigationType navigationType) : EventArgs
{
    public Page? PreviousPage { get; } = previousPage;
    public NavigationType NavigationType { get; } = navigationType;
}

public sealed class NavigatingFromEventArgs(Page? destinationPage, NavigationType navigationType) : EventArgs
{
    public Page? DestinationPage { get; } = destinationPage;
    public NavigationType NavigationType { get; } = navigationType;
}

public sealed class NavigatedFromEventArgs(Page? destinationPage, NavigationType navigationType) : EventArgs
{
    public Page? DestinationPage { get; } = destinationPage;
    public NavigationType NavigationType { get; } = navigationType;
}

public partial class Page
{
    public event EventHandler<NavigatedToEventArgs>? NavigatedTo;
    public event EventHandler<NavigatingFromEventArgs>? NavigatingFrom;
    public event EventHandler<NavigatedFromEventArgs>? NavigatedFrom;

    protected virtual void OnNavigatedTo(NavigatedToEventArgs args)
    {
    }

    protected virtual void OnNavigatingFrom(NavigatingFromEventArgs args)
    {
    }

    protected virtual void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
    }

    internal void SendNavigatedTo(NavigatedToEventArgs args)
    {
        OnNavigatedTo(args);
        NavigatedTo?.Invoke(this, args);
    }

    internal void SendNavigatingFrom(NavigatingFromEventArgs args)
    {
        OnNavigatingFrom(args);
        NavigatingFrom?.Invoke(this, args);
    }

    internal void SendNavigatedFrom(NavigatedFromEventArgs args)
    {
        OnNavigatedFrom(args);
        NavigatedFrom?.Invoke(this, args);
    }
}
