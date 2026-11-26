using Microsoft.Maui.Graphics;
using PKForge.MauiLite;
using Xunit;

namespace PKForge.Switch2.Platform.Tests;

// The console reports one touch point per poll in libnx's 1280x720. MauiLite's window is 1920x1080 pixels at the
// Thor's 2.25 pixels per unit, 853⅓ by 480 units, so a raw point is two thirds of a unit point.
public class TouchTrackerTests
{
    [Fact]
    public void Touch_PressMoveRelease()
    {
        var touch = new TouchTracker();

        Assert.Empty(touch.Update(0, 0, 0));
        var pressed = Assert.Single(touch.Update(1, 300, 600));
        var moved = Assert.Single(touch.Update(1, 330, 600));
        var released = Assert.Single(touch.Update(0, 0, 0));

        Assert.Equal((PointerAction.Pressed, new Point(200, 400)), (pressed.Action, pressed.Units));
        Assert.Equal((PointerAction.Moved, new Point(220, 400)), (moved.Action, moved.Units));
        Assert.Equal((PointerAction.Released, new Point(220, 400)), (released.Action, released.Units));
    }

    [Fact]
    public void Touch_SamePoint_NoMoveEvent()
    {
        var touch = new TouchTracker();
        touch.Update(1, 300, 600);

        Assert.Empty(touch.Update(1, 300, 600));
    }

    [Fact]
    public void Touch_AtRawEdge_ClampedInsideWindow()
    {
        var pressed = Assert.Single(new TouchTracker().Update(1, 1280, 720));

        Assert.True(pressed.Units.X < 1920 / 2.25 && pressed.Units.X > 853, $"{pressed.Units}");
        Assert.True(pressed.Units.Y < 480 && pressed.Units.Y > 479.9, $"{pressed.Units}");
    }

    [Fact]
    public void Touch_NewPress_NewId()
    {
        var touch = new TouchTracker();
        var first = touch.Update(1, 10, 10)[0].Id;
        touch.Update(0, 0, 0);

        Assert.NotEqual(first, touch.Update(1, 10, 10)[0].Id);
    }
}
