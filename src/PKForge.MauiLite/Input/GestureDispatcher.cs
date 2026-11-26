using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PKForge.MauiLite.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PKForge.MauiLite;

/// <summary>
/// Turns pointer events into MAUI's taps, button clicks, canvas touches and scrolling, as Android dispatches touches:
/// the topmost element that takes touch gets it; a drag past the touch slop becomes a scroll; a canvas that handles
/// its press keeps the whole gesture.
/// </summary>
public sealed class GestureDispatcher
{
    /// <summary>Android's touch slop, in units.</summary>
    const double Slop = 8;
    static readonly TimeSpan DoubleTapWindow = TimeSpan.FromMilliseconds(300);

    SKCanvasView? _canvas;
    View? _tapTarget;
    View? _pointerTarget;
    ScrollView? _scroll;
    Point _pressedAt;
    Point _scrollStart;
    bool _scrolling;
    bool _active;
    View? _lastTapTarget;
    TimeSpan _lastTapAt;
    int _tapCount;

    public void Handle(VisualElement root, PointerEvent e, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(root);
        try
        {
            switch (e.Action)
            {
                case PointerAction.Pressed:
                    Pressed(root, e);
                    break;
                case PointerAction.Moved when _active:
                    Moved(e);
                    break;
                case PointerAction.Released when _active:
                    Released(e, now);
                    break;
                case PointerAction.Cancelled when _active:
                    SendCanvas(SKTouchAction.Cancelled, e, inContact: false);
                    Reset();
                    break;
            }
        }
        catch (Exception error)
        {
            UiErrors.Report(error, $"input {e.Action}");
            if (e.Action is PointerAction.Released or PointerAction.Cancelled)
                Reset();
        }
    }

    void Pressed(VisualElement root, PointerEvent e)
    {
        Reset();
        _active = true;
        _pressedAt = e.Units;
        var hit = HitTest(root, e.Units);
        if (hit is SKCanvasView canvas)
        {
            var args = Touch(canvas, SKTouchAction.Pressed, e, inContact: true);
            canvas.SendTouch(args);
            if (args.Handled)
            {
                _canvas = canvas;
                return;
            }
        }
        for (Element? element = hit; element is not null; element = element.Parent)
        {
            if (_tapTarget is null && element is View view and (Button or InputView or { HasGestures: true }))
                _tapTarget = view;
            if (_pointerTarget is null && element is View pointed && pointed.GestureRecognizers.OfType<PointerGestureRecognizer>().Any())
                _pointerTarget = pointed;
            if (_scroll is null && element is ScrollView scroll)
                _scroll = scroll;
        }
        if (_tapTarget is Button button)
            button.SendPressed();
        SendPointer(e.Units, (recognizer, view, at) => recognizer.SendPressed(view, at));
    }

    void Moved(PointerEvent e)
    {
        if (_canvas is not null)
        {
            SendCanvas(SKTouchAction.Moved, e, inContact: true);
            return;
        }
        // The move that starts a scroll still reaches the pointer gesture, so it can see the drag (Android cancels then).
        if (!_scrolling)
            SendPointer(e.Units, (recognizer, view, at) => recognizer.SendMoved(view, at));
        var dx = e.Units.X - _pressedAt.X;
        var dy = e.Units.Y - _pressedAt.Y;
        if (!_scrolling && Math.Max(Math.Abs(dx), Math.Abs(dy)) > Slop)
        {
            CancelTap();
            if (_scroll is not null)
            {
                _scrolling = true;
                _scrollStart = new Point(_scroll.ScrollX, _scroll.ScrollY);
            }
        }
        if (_scrolling && _scroll is not null)
            _scroll.SetScroll(_scrollStart.X - dx, _scrollStart.Y - dy);
    }

    void Released(PointerEvent e, TimeSpan now)
    {
        if (_canvas is not null)
        {
            SendCanvas(SKTouchAction.Released, e, inContact: false);
            Reset();
            return;
        }
        SendPointer(e.Units, (recognizer, view, at) => recognizer.SendReleased(view, at));
        var target = _tapTarget;
        if (target is Button pressed)
            pressed.SendReleased();
        if (target is not null && target.WindowBounds.Contains(e.Units))
            Tap(target, e.Units, now);
        Reset();
    }

    void Tap(View target, Point at, TimeSpan now)
    {
        _tapCount = ReferenceEquals(target, _lastTapTarget) && now - _lastTapAt <= DoubleTapWindow ? _tapCount + 1 : 1;
        _lastTapTarget = target;
        _lastTapAt = now;
        if (target is Button button)
        {
            button.SendClicked();
            return;
        }
        // A text field gains focus, as on Android; phase 1 has no keyboard to open for it.
        if (target is InputView input)
        {
            input.Focus();
            return;
        }
        foreach (var tap in target.GestureRecognizers.OfType<TapGestureRecognizer>().ToArray())
            if (tap.NumberOfTapsRequired <= 1 || tap.NumberOfTapsRequired == _tapCount)
                tap.SendTapped(target, at);
    }

    void SendPointer(Point at, Action<PointerGestureRecognizer, View, Point> send)
    {
        if (_pointerTarget is { } view)
            foreach (var recognizer in view.GestureRecognizers.OfType<PointerGestureRecognizer>().ToArray())
                send(recognizer, view, at);
    }

    void CancelTap()
    {
        if (_tapTarget is Button button)
            button.SendReleased();
        _tapTarget = null;
    }

    void SendCanvas(SKTouchAction action, PointerEvent e, bool inContact)
    {
        if (_canvas is { } canvas)
            canvas.SendTouch(Touch(canvas, action, e, inContact));
    }

    static SKTouchEventArgs Touch(SKCanvasView canvas, SKTouchAction action, PointerEvent e, bool inContact)
    {
        // Canvas pixels, as SkiaSharp reports them on Android (units when the canvas ignores pixel scaling).
        var scale = canvas.IgnorePixelScaling ? 1f : Display.Density;
        var location = new SKPoint((float)((e.Units.X - canvas.WindowBounds.X) * scale), (float)((e.Units.Y - canvas.WindowBounds.Y) * scale));
        return new SKTouchEventArgs(e.Id, action, SKMouseButton.Left, SKTouchDeviceType.Touch, location, inContact);
    }

    void Reset()
    {
        _canvas = null;
        _tapTarget = null;
        _pointerTarget = null;
        _scroll = null;
        _scrolling = false;
        _active = false;
    }

    /// <summary>The topmost element under <paramref name="point"/> that takes touch, or null.</summary>
    internal static VisualElement? HitTest(VisualElement element, Point point)
    {
        if (!element.IsVisible)
            return null;
        var clipped = element.ClipsChildren;
        var cascades = element is not Layout { CascadeInputTransparent: false };
        if (!(element.InputTransparent && cascades) && (!clipped || element.WindowBounds.Contains(point)))
        {
            foreach (var child in element.VisualChildren.Reverse())
                if (HitTest(child, point) is { } hit)
                    return hit;
        }
        if (element.InputTransparent || !element.IsEnabled || !element.WindowBounds.Contains(point))
            return null;
        return TakesTouch(element) ? element : null;
    }

    static bool TakesTouch(VisualElement element) => element switch
    {
        SKCanvasView canvas => canvas.EnableTouchEvents,
        Button => true,
        InputView => true,
        ScrollView => true,
        View { HasGestures: true } => true,
        _ => false,
    };
}
