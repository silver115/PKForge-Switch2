using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace PKForge.MauiLite.Diagnostics;

/// <summary>The text a person would see in a tree, in drawing order: for scripted checks and tests.</summary>
public static class VisibleText
{
    public static IEnumerable<string> Of(Element root) => Elements(root).Select(shown => shown.Text);

    /// <summary>Each shown label and button with its text and its place in the window (units), in drawing order.</summary>
    public static IEnumerable<(VisualElement Element, string Text, Rect Window)> Elements(Element root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root is not VisualElement visual || !visual.IsVisible)
            yield break;
        var text = root switch
        {
            Label { FormattedText: { } formatted } => formatted.ToString(),
            Label label => label.Text,
            Button button => button.Text,
            _ => null,
        };
        if (!string.IsNullOrEmpty(text))
            yield return (visual, text, visual.WindowBounds);
        foreach (var child in visual.VisualChildren)
            foreach (var shown in Elements(child))
                yield return shown;
    }
}
