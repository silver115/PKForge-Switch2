namespace PKForge.App.Views;

/// <summary>
/// The Switch 2's form of HomePage.AttachPress, which reads Android's native touch stream: a press released within the
/// touch slop is a tap, one that rests for Android's long-press timeout is a long press, and a drag (the shelf scrolling)
/// cancels it. Built on MAUI's pointer gesture, which MauiLite feeds from the touch screen.
/// </summary>
internal static class PressGesture
{
    /// <summary>Android's touch slop, in units (8 dp).</summary>
    private const double Slop = 8;

    /// <summary>Android's ViewConfiguration.getLongPressTimeout().</summary>
    private static readonly TimeSpan LongPressTimeout = TimeSpan.FromMilliseconds(400);

    public static void Attach(View view, Action onTap, Action onLongPress)
    {
        var token = 0;
        var pressing = false;
        var down = Point.Zero;
        var pointer = new PointerGestureRecognizer();
        pointer.PointerPressed += (_, e) =>
        {
            var pressed = ++token;
            pressing = true;
            down = e.GetPosition(view) ?? Point.Zero;
            view.Dispatcher.DispatchDelayed(LongPressTimeout, () =>
            {
                if (pressed != token || !pressing) return;
                pressing = false;
                onLongPress();
            });
        };
        pointer.PointerMoved += (_, e) =>
        {
            if (e.GetPosition(view) is { } at && (Math.Abs(at.X - down.X) > Slop || Math.Abs(at.Y - down.Y) > Slop))
                pressing = false;
        };
        pointer.PointerReleased += (_, _) =>
        {
            token++;
            if (pressing) onTap();
            pressing = false;
        };
        view.GestureRecognizers.Add(pointer);
    }
}
