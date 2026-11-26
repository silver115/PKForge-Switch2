using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Microsoft.Maui.Controls;

public interface INavigation
{
    IReadOnlyList<Page> ModalStack { get; }
    IReadOnlyList<Page> NavigationStack { get; }
    void InsertPageBefore(Page page, Page before);
    Task<Page?> PopAsync();
    Task<Page?> PopAsync(bool animated);
    Task<Page?> PopModalAsync();
    Task<Page?> PopModalAsync(bool animated);
    Task PopToRootAsync();
    Task PopToRootAsync(bool animated);
    Task PushAsync(Page page);
    Task PushAsync(Page page, bool animated);
    Task PushModalAsync(Page page);
    Task PushModalAsync(Page page, bool animated);
    void RemovePage(Page page);
}

/// <summary>MAUI's page: a screen with appearing and disappearing callbacks.</summary>
public partial class Page : VisualElement
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(Page), null);
    public static readonly BindableProperty PaddingProperty = BindableProperty.Create(nameof(Padding), typeof(Thickness), typeof(Page), Thickness.Zero).Measured();
    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(nameof(IsBusy), typeof(bool), typeof(Page), false);

    bool _appeared;

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty)!; set => SetValue(PaddingProperty, value); }
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty)!; set => SetValue(IsBusyProperty, value); }

    public event EventHandler? Appearing;
    public event EventHandler? Disappearing;

    /// <summary>The navigation of the nearest navigation page holding this page.</summary>
    public INavigation Navigation
    {
        get
        {
            for (Element? e = this; e is not null; e = e.Parent)
                if (e is NavigationPage navigation)
                    return navigation;
            return NoNavigation.Instance;
        }
    }

    protected virtual void OnAppearing()
    {
    }

    protected virtual void OnDisappearing()
    {
    }

    protected virtual bool OnBackButtonPressed() => false;

    public bool SendBackButtonPressed() => OnBackButtonPressed();

    internal virtual void SendAppearing()
    {
        if (_appeared)
            return;
        _appeared = true;
        OnAppearing();
        Appearing?.Invoke(this, EventArgs.Empty);
    }

    internal virtual void SendDisappearing()
    {
        if (!_appeared)
            return;
        _appeared = false;
        OnDisappearing();
        Disappearing?.Invoke(this, EventArgs.Empty);
    }

    internal bool HasAppeared => _appeared;

    sealed class NoNavigation : INavigation
    {
        public static readonly NoNavigation Instance = new();

        static InvalidOperationException NotInNavigationPage() => new("This page is not inside a NavigationPage.");

        public IReadOnlyList<Page> ModalStack => [];
        public IReadOnlyList<Page> NavigationStack => [];
        public void InsertPageBefore(Page page, Page before) => throw NotInNavigationPage();
        public Task<Page?> PopAsync() => Task.FromResult<Page?>(null);
        public Task<Page?> PopAsync(bool animated) => PopAsync();
        public Task<Page?> PopModalAsync() => Task.FromResult<Page?>(null);
        public Task<Page?> PopModalAsync(bool animated) => PopModalAsync();
        public Task PopToRootAsync() => Task.CompletedTask;
        public Task PopToRootAsync(bool animated) => Task.CompletedTask;
        public Task PushAsync(Page page) => throw NotInNavigationPage();
        public Task PushAsync(Page page, bool animated) => throw NotInNavigationPage();
        public Task PushModalAsync(Page page) => throw NotInNavigationPage();
        public Task PushModalAsync(Page page, bool animated) => throw NotInNavigationPage();
        public void RemovePage(Page page) => throw NotInNavigationPage();
    }
}

/// <summary>MAUI's content page: one view, inside the page's padding.</summary>
public class ContentPage : Page, IContentView
{
    View? _content;

    public View? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value))
                return;
            if (_content is not null && ReferenceEquals(_content.Parent, this))
                _content.Parent = null;
            _content = value;
            if (value is not null)
                value.Parent = this;
            OnPropertyChanged();
            InvalidateLayout();
        }
    }

    object? IContentView.Content => _content;
    IView? IContentView.PresentedContent => _content;
    Thickness IPadding.Padding => Padding;

    internal override Size MeasureNative(double widthConstraint, double heightConstraint) =>
        this.MeasureContent(Padding, widthConstraint, heightConstraint);

    internal override void ArrangeContent(Rect bounds) => ((IContentView)this).ArrangeContent(bounds);

    internal override IEnumerable<VisualElement> VisualChildren => _content is null ? [] : [_content];
}

/// <summary>MAUI's navigation page: a stack of pages, of which the top one shows.</summary>
public class NavigationPage : Page, INavigation
{
    public static readonly BindableProperty HasNavigationBarProperty = BindableProperty.CreateAttached("HasNavigationBar", typeof(bool), typeof(NavigationPage), true);
    public static readonly BindableProperty HasBackButtonProperty = BindableProperty.CreateAttached("HasBackButton", typeof(bool), typeof(NavigationPage), true);
    public static readonly BindableProperty BarBackgroundColorProperty = BindableProperty.Create(nameof(BarBackgroundColor), typeof(Color), typeof(NavigationPage), null);
    public static readonly BindableProperty BarTextColorProperty = BindableProperty.Create(nameof(BarTextColor), typeof(Color), typeof(NavigationPage), null);

    readonly List<Page> _stack = [];
    readonly List<Page> _modal = [];

    public NavigationPage()
    {
    }

    public NavigationPage(Page root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _stack.Add(root);
        root.Parent = this;
    }

    public static bool GetHasNavigationBar(BindableObject page) => (bool)page.GetValue(HasNavigationBarProperty)!;
    public static void SetHasNavigationBar(BindableObject page, bool value) => page.SetValue(HasNavigationBarProperty, value);
    public static bool GetHasBackButton(BindableObject page) => (bool)page.GetValue(HasBackButtonProperty)!;
    public static void SetHasBackButton(BindableObject page, bool value) => page.SetValue(HasBackButtonProperty, value);

    public Color? BarBackgroundColor { get => (Color?)GetValue(BarBackgroundColorProperty); set => SetValue(BarBackgroundColorProperty, value); }
    public Color? BarTextColor { get => (Color?)GetValue(BarTextColorProperty); set => SetValue(BarTextColorProperty, value); }

    public Page CurrentPage => _modal.Count > 0 ? _modal[^1] : _stack[^1];
    public Page RootPage => _stack[0];

    public IReadOnlyList<Page> NavigationStack => _stack;
    public IReadOnlyList<Page> ModalStack => _modal;

    public event EventHandler<NavigationEventArgs>? Pushed;
    public event EventHandler<NavigationEventArgs>? Popped;

    public Task PushAsync(Page page) => PushAsync(page, true);

    public Task PushAsync(Page page, bool animated)
    {
        Show(page, _stack);
        Pushed?.Invoke(this, new NavigationEventArgs(page));
        return Task.CompletedTask;
    }

    public Task PushModalAsync(Page page) => PushModalAsync(page, true);

    public Task PushModalAsync(Page page, bool animated)
    {
        Show(page, _modal);
        return Task.CompletedTask;
    }

    public Task<Page?> PopAsync() => PopAsync(true);

    public Task<Page?> PopAsync(bool animated)
    {
        if (_modal.Count > 0 || _stack.Count <= 1)
            return Task.FromResult<Page?>(null);
        var page = Hide(_stack);
        Popped?.Invoke(this, new NavigationEventArgs(page));
        return Task.FromResult<Page?>(page);
    }

    public Task<Page?> PopModalAsync() => PopModalAsync(true);

    public Task<Page?> PopModalAsync(bool animated) => Task.FromResult(_modal.Count == 0 ? null : (Page?)Hide(_modal));

    public Task PopToRootAsync() => PopToRootAsync(true);

    public async Task PopToRootAsync(bool animated)
    {
        while (_stack.Count > 1)
            await PopAsync(animated);
    }

    public void InsertPageBefore(Page page, Page before)
    {
        var index = _stack.IndexOf(before);
        if (index < 0)
            throw new ArgumentException("The page to insert before is not on the navigation stack.", nameof(before));
        _stack.Insert(index, page);
        page.Parent = this;
    }

    public void RemovePage(Page page)
    {
        if (ReferenceEquals(page, CurrentPage))
        {
            _ = PopAsync();
            return;
        }
        if (_stack.Remove(page))
            page.Parent = null;
    }

    // MAUI's order: navigating-from, disappearing, appearing, navigated-from, navigated-to.
    void Show(Page page, List<Page> stack)
    {
        ArgumentNullException.ThrowIfNull(page);
        var previous = CurrentPage;
        previous.SendNavigatingFrom(new NavigatingFromEventArgs(page, NavigationType.Push));
        previous.SendDisappearing();
        stack.Add(page);
        page.Parent = this;
        if (HasAppeared)
            page.SendAppearing();
        previous.SendNavigatedFrom(new NavigatedFromEventArgs(page, NavigationType.Push));
        page.SendNavigatedTo(new NavigatedToEventArgs(previous, NavigationType.Push));
        InvalidateLayout();
    }

    Page Hide(List<Page> stack)
    {
        var page = stack[^1];
        var below = stack.Count > 1 ? stack[^2] : _stack[^1];
        page.SendNavigatingFrom(new NavigatingFromEventArgs(below, NavigationType.Pop));
        page.SendDisappearing();
        stack.RemoveAt(stack.Count - 1);
        page.Parent = null;
        if (HasAppeared)
            CurrentPage.SendAppearing();
        page.SendNavigatedFrom(new NavigatedFromEventArgs(CurrentPage, NavigationType.Pop));
        CurrentPage.SendNavigatedTo(new NavigatedToEventArgs(page, NavigationType.Pop));
        InvalidateLayout();
        return page;
    }

    protected override bool OnBackButtonPressed()
    {
        if (CurrentPage.SendBackButtonPressed())
            return true;
        if (_modal.Count > 0)
        {
            Hide(_modal);
            return true;
        }
        if (_stack.Count > 1)
        {
            _ = PopAsync();
            return true;
        }
        return false;
    }

    internal override void SendAppearing()
    {
        base.SendAppearing();
        CurrentPage.SendAppearing();
    }

    internal override void SendDisappearing()
    {
        CurrentPage.SendDisappearing();
        base.SendDisappearing();
    }

    // shortcut: no navigation bar is drawn; every PKForge page in phase 1 hides it. Draw one when a shown page keeps it.
    internal override Size MeasureNative(double widthConstraint, double heightConstraint)
    {
        CurrentPage.Measure(widthConstraint, heightConstraint);
        return new Size(double.IsInfinity(widthConstraint) ? CurrentPage.DesiredSize.Width : widthConstraint,
            double.IsInfinity(heightConstraint) ? CurrentPage.DesiredSize.Height : heightConstraint);
    }

    internal override void ArrangeContent(Rect bounds) => CurrentPage.Arrange(bounds);

    internal override IEnumerable<VisualElement> VisualChildren => [CurrentPage];

    internal override IEnumerable<BindableObject> InheritingChildren => [.. _stack, .. _modal];
}

public class NavigationEventArgs(Page page) : EventArgs
{
    public Page Page { get; } = page;
}
