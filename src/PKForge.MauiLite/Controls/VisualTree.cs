namespace Microsoft.Maui
{
    public interface IVisualTreeElement
    {
        IReadOnlyList<IVisualTreeElement> GetVisualChildren();
        IVisualTreeElement? GetVisualParent();
    }

    public static class VisualTreeElementExtensions
    {
        /// <summary>The element and everything under it, depth first, as MAUI returns them.</summary>
        public static IReadOnlyCollection<IVisualTreeElement> GetVisualTreeDescendants(this IVisualTreeElement visualTreeElement)
        {
            ArgumentNullException.ThrowIfNull(visualTreeElement);
            var found = new List<IVisualTreeElement>();
            void Walk(IVisualTreeElement element)
            {
                found.Add(element);
                foreach (var child in element.GetVisualChildren())
                    Walk(child);
            }
            Walk(visualTreeElement);
            return found;
        }
    }
}

namespace Microsoft.Maui.Controls
{
    public abstract partial class VisualElement : IVisualTreeElement, IAnimatable
    {
        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => [.. VisualChildren];

        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Parent as IVisualTreeElement;

        public void BatchBegin()
        {
        }

        public void BatchCommit()
        {
        }
    }
}
