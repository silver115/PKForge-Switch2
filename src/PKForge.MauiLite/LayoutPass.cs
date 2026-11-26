using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace PKForge.MauiLite;

/// <summary>Measures and arranges a tree when something in it changed, then records where each element sits in the window.</summary>
public static class LayoutPass
{
    public static void Run(VisualElement root, Size window)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.NeedsLayout || root.LayoutWindow != window)
        {
            root.Measure(window.Width, window.Height);
            root.Arrange(new Rect(0, 0, window.Width, window.Height));
            root.LayoutWindow = window;
        }
        Place(root, 0, 0);
    }

    static void Place(VisualElement element, double originX, double originY)
    {
        var bounds = element.Bounds;
        element.WindowBounds = new Rect(originX + bounds.X, originY + bounds.Y, bounds.Width, bounds.Height);
        element.NeedsLayout = false;
        var offset = element.ChildOffset;
        foreach (var child in element.VisualChildren)
            Place(child, element.WindowBounds.X + offset.X, element.WindowBounds.Y + offset.Y);
    }
}
