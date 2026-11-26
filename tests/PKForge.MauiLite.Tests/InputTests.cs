using System.Collections.ObjectModel;
using PKForge.MauiLite;
using PKForge.MauiLite.Diagnostics;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PKForge.MauiLite.Tests;

public class InputTests
{
    static readonly Size Screen = new(1280, 720);

    // Canvas touch locations are in pixels: these tests use 1.5 pixels per unit.
    public InputTests() => PKForge.MauiLite.Rendering.Display.Density = 1.5f;

    sealed class Pointer(VisualElement root)
    {
        readonly GestureDispatcher _input = new();
        TimeSpan _now;

        public Pointer Press(double x, double y) => Send(PointerAction.Pressed, x, y);
        public Pointer Move(double x, double y) => Send(PointerAction.Moved, x, y);
        public Pointer Release(double x, double y) => Send(PointerAction.Released, x, y);
        public Pointer Tap(double x, double y) => Press(x, y).Release(x, y);

        Pointer Send(PointerAction action, double x, double y)
        {
            LayoutPass.Run(root, Screen);
            _now += TimeSpan.FromMilliseconds(500);
            _input.Handle(root, new PointerEvent(action, 1, new Point(x, y)), _now);
            return this;
        }
    }

    static T Place<T>(T view, double x, double y, double width, double height) where T : View
    {
        view.Margin = new Thickness(x, y, 0, 0);
        view.WidthRequest = width;
        view.HeightRequest = height;
        view.HorizontalOptions = LayoutOptions.Start;
        view.VerticalOptions = LayoutOptions.Start;
        return view;
    }

    static (BoxView View, List<string> Taps) Tappable(string name, List<string> taps)
    {
        var view = new BoxView();
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => taps.Add(name);
        view.GestureRecognizers.Add(tap);
        return (view, taps);
    }

    [Fact]
    public void Tap_HitsTopmostVisibleAndSkipsInputTransparent()
    {
        var taps = new List<string>();
        var bottom = Tappable("bottom", taps).View;
        var top = Tappable("top", taps).View;
        var hidden = Tappable("hidden", taps).View;
        hidden.IsVisible = false;
        var root = new Grid { bottom, top, hidden };
        var pointer = new Pointer(root);

        pointer.Tap(10, 10);
        top.InputTransparent = true;
        pointer.Tap(10, 10);

        Assert.Equal(["top", "bottom"], taps);
    }

    // The console's first run: every tap that passed a rounded border was lost (hit testing asked the border for its clip).
    [Fact]
    public void Tap_ThroughRoundedBorder_ReachesChild()
    {
        var taps = new List<string>();
        var inner = Tappable("inner", taps).View;
        var border = Place(new Border { StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 7 }, StrokeThickness = 1.5, Content = inner }, 0, 0, 100, 60);

        new Pointer(new Grid { border }).Tap(50, 30);

        Assert.Equal(["inner"], taps);
    }

    // The console's Pokédex picker closed when its search box was tapped: a text field did not take the tap, so it went
    // to the backdrop behind the panel. On Android a text field takes it and gains focus.
    [Fact]
    public void Tap_OnTextField_FocusesItAndStopsThere()
    {
        var taps = new List<string>();
        var backdrop = Tappable("backdrop", taps).View;
        var entry = Place(new Entry(), 0, 0, 100, 40);

        new Pointer(new Grid { backdrop, entry }).Tap(50, 20);

        Assert.Empty(taps);
        Assert.True(entry.IsFocused);
    }

    // The console's software keyboard opens when a text field takes focus (phase 2): what it is asked, and what it returns.
    static T WithKeyboard<T>(Func<SoftKeyboardRequest, string?> keyboard, Func<T> test)
    {
        SoftKeyboard.Show = keyboard;
        try
        {
            return test();
        }
        finally
        {
            SoftKeyboard.Show = null;
        }
    }

    [Fact]
    public void Focus_ShowsTheKeyboardAndTakesItsText()
    {
        SoftKeyboardRequest? asked = null;
        var entry = new Entry { Text = "Pika", Placeholder = "Search", MaxLength = 12 };
        var completed = 0;
        entry.Completed += (_, _) => completed++;

        WithKeyboard(request => { asked = request; return "Pikachu"; }, entry.Focus);

        Assert.Equal(new SoftKeyboardRequest("Pika", "Search", 12, Numeric: false, Multiline: false), asked);
        Assert.Equal("Pikachu", entry.Text);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Focus_KeyboardCancelled_KeepsTheText()
    {
        var entry = new Entry { Text = "Pika" };
        var completed = 0;
        entry.Completed += (_, _) => completed++;

        WithKeyboard(_ => null, entry.Focus);

        Assert.Equal("Pika", entry.Text);
        Assert.Equal(0, completed);
    }

    [Fact]
    public void Focus_NumericKeyboard_AsksForNumbers()
    {
        SoftKeyboardRequest? asked = null;
        var entry = new Entry { Keyboard = Keyboard.Numeric };

        WithKeyboard(request => { asked = request; return null; }, entry.Focus);

        Assert.True(asked?.Numeric);
        Assert.Equal("", asked?.Text);
    }

    [Fact]
    public void Focus_ReadOnlyField_ShowsNoKeyboard()
    {
        var shown = false;
        var entry = new Entry { Text = "Pika", IsReadOnly = true };

        WithKeyboard(_ => { shown = true; return "Pikachu"; }, entry.Focus);

        Assert.False(shown);
        Assert.Equal("Pika", entry.Text);
    }

    [Fact]
    public void Tap_CancelledWhenMovedPast8Units()
    {
        var taps = new List<string>();
        var root = new Grid { Tappable("box", taps).View };

        new Pointer(root).Press(10, 10).Move(19, 10).Release(19, 10);
        Assert.Empty(taps);

        new Pointer(root).Press(10, 10).Move(17, 10).Release(17, 10);
        Assert.Equal(["box"], taps);
    }

    static (SKCanvasView Canvas, List<SKTouchEventArgs> Events) TouchCanvas(bool handle)
    {
        var events = new List<SKTouchEventArgs>();
        var canvas = Place(new SKCanvasView { EnableTouchEvents = true }, 100, 50, 200, 100);
        canvas.Touch += (_, e) =>
        {
            events.Add(e);
            e.Handled = handle;
        };
        return (canvas, events);
    }

    [Fact]
    public void CanvasTouch_LocationInCanvasPixels()
    {
        var (canvas, events) = TouchCanvas(handle: true);

        new Pointer(new Grid { canvas }).Press(110, 60);

        Assert.Equal(new SKPoint(15, 15), Assert.Single(events).Location);
        Assert.Equal(SKTouchAction.Pressed, events[0].ActionType);
    }

    [Fact]
    public void CanvasTouch_UnhandledPress_GetsNoMoreEvents()
    {
        var (canvas, events) = TouchCanvas(handle: false);

        new Pointer(new Grid { canvas }).Press(110, 60).Move(120, 60).Release(120, 60);

        Assert.Single(events);
    }

    [Fact]
    public void CanvasTouch_HandledPress_CapturedOutsideBounds()
    {
        var (canvas, events) = TouchCanvas(handle: true);

        new Pointer(new Grid { canvas }).Press(110, 60).Move(500, 500).Release(500, 500);

        Assert.Equal([SKTouchAction.Pressed, SKTouchAction.Moved, SKTouchAction.Released], events.Select(e => e.ActionType));
        Assert.Equal(new SKPoint(600, 675), events[1].Location);
    }

    [Fact]
    public void ScrollView_DragScrollsAndClamps()
    {
        var content = new VerticalStackLayout { Children = { new BoxView { HeightRequest = 100 }, new BoxView { HeightRequest = 100 }, new BoxView { HeightRequest = 100 } } };
        var scroll = Place(new ScrollView { Content = content }, 0, 0, 200, 150);
        var pointer = new Pointer(new Grid { scroll });

        pointer.Press(50, 140).Move(50, 40).Release(50, 40);
        Assert.Equal(100, scroll.ScrollY);

        pointer.Press(50, 140).Move(50, -360).Release(50, -360);
        Assert.Equal(150, scroll.ScrollY);
    }

    [Fact]
    public void Button_ClickedOnReleaseInside()
    {
        var clicks = 0;
        var button = Place(new Button { Text = "Go" }, 0, 0, 100, 40);
        button.Clicked += (_, _) => clicks++;
        var pointer = new Pointer(new Grid { button });

        pointer.Tap(10, 10);
        pointer.Press(10, 10).Release(300, 300);

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Tap_HandlerThrowing_IsReported()
    {
        var reported = 0;
        void Record(Exception error, string where) => reported++;
        var box = new BoxView();
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => throw new InvalidOperationException("tap bug");
        box.GestureRecognizers.Add(tap);
        UiErrors.Reported += Record;
        try
        {
            new Pointer(new Grid { box }).Tap(10, 10);
        }
        finally
        {
            UiErrors.Reported -= Record;
        }

        Assert.Equal(1, reported);
    }

    [Fact]
    public void PointerGesture_PressMoveReleaseRelativeToView()
    {
        var events = new List<string>();
        var box = Place(new BoxView(), 100, 50, 200, 100);
        var pointer = new PointerGestureRecognizer();
        pointer.PointerPressed += (_, e) => events.Add($"pressed {e.GetPosition(box)}");
        pointer.PointerMoved += (_, e) => events.Add($"moved {e.GetPosition(box)}");
        pointer.PointerReleased += (_, e) => events.Add($"released {e.GetPosition(box)}");
        box.GestureRecognizers.Add(pointer);

        new Pointer(new Grid { box }).Press(110, 60).Move(112, 60).Release(112, 60);

        Assert.Equal([$"pressed {new Point(10, 10)}", $"moved {new Point(12, 10)}", $"released {new Point(12, 10)}"], events);
    }

    static DataTemplate TextTemplate() => new(() =>
    {
        var label = new Label();
        label.SetBinding(Label.TextProperty, Binding.SelfPath);
        return label;
    });

    [Fact]
    public void CollectionView_OneViewPerItemWithItsContext()
    {
        var list = new CollectionView { ItemsSource = new[] { "a", "b" }, ItemTemplate = TextTemplate() };

        Assert.Equal(["a", "b"], VisibleText.Of(list));
    }

    [Fact]
    public void CollectionView_FollowsCollectionChanged()
    {
        var items = new ObservableCollection<string> { "a", "b" };
        var list = new CollectionView { ItemsSource = items, ItemTemplate = TextTemplate() };

        items.Add("c");
        items.Remove("a");

        Assert.Equal(["b", "c"], VisibleText.Of(list));
    }
}
