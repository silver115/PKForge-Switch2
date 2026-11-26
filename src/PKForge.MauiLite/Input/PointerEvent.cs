using Microsoft.Maui.Graphics;
using SkiaSharp;

namespace PKForge.MauiLite;

public enum PointerAction { Pressed, Moved, Released, Cancelled }

/// <summary>One touch event, at a point in window units.</summary>
public readonly record struct PointerEvent(PointerAction Action, long Id, Point Units);

/// <summary>The controller this frame: held buttons (libnx's npad bits) and the two sticks, each axis −1 to 1.</summary>
public readonly record struct PadState(ulong Held, float LeftX, float LeftY, float RightX, float RightY);

/// <summary>What MauiLite needs from the screen it runs on: the console, or the headless host.</summary>
public interface IHostBackend
{
    /// <summary>The frame size in pixels (1920×1080).</summary>
    SKSizeI PixelSize { get; }

    /// <summary>Pixels per unit: Android's density on the AYN Thor (2.25), whose screen is the Switch 2's 1920×1080.</summary>
    float Scale { get; }

    TimeSpan Now { get; }

    bool QuitRequested { get; }

    IReadOnlyList<PointerEvent> PollPointers();

    PadState PollPad();

    /// <summary>Shows a frame. Called only after a frame drew something.</summary>
    void Present(SKPixmap frame);

    /// <summary>Paces a frame that drew nothing to the display rate.</summary>
    void WaitForNextFrame();
}
