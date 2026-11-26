using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;

// MAUI's Core interfaces, cut to the members the ported layout managers read.
// MauiLite's controls implement them explicitly, so their public surface stays that of MAUI's controls.
namespace Microsoft.Maui
{
    public interface IView
    {
        Thickness Margin { get; }
        /// <summary>The explicit width (WidthRequest), or <see cref="Dimension.Unset"/>.</summary>
        double Width { get; }
        double Height { get; }
        double MinimumWidth { get; }
        double MinimumHeight { get; }
        double MaximumWidth { get; }
        double MaximumHeight { get; }
        LayoutAlignment HorizontalLayoutAlignment { get; }
        LayoutAlignment VerticalLayoutAlignment { get; }
        Visibility Visibility { get; }
        Size DesiredSize { get; }
        Size Measure(double widthConstraint, double heightConstraint);
        Size Arrange(Rect bounds);
    }

    public interface IPadding
    {
        Thickness Padding { get; }
    }

    public interface IContainer : IList<IView>
    {
    }

    public interface ILayout : IView, IContainer, IPadding
    {
        Size CrossPlatformMeasure(double widthConstraint, double heightConstraint);
        Size CrossPlatformArrange(Rect bounds);
    }

    public interface IStackLayout : ILayout
    {
        double Spacing { get; }
    }

    public interface IGridRowDefinition
    {
        GridLength Height { get; }
    }

    public interface IGridColumnDefinition
    {
        GridLength Width { get; }
    }

    public interface IGridLayout : ILayout
    {
        IReadOnlyList<IGridRowDefinition> RowDefinitions { get; }
        IReadOnlyList<IGridColumnDefinition> ColumnDefinitions { get; }
        double RowSpacing { get; }
        double ColumnSpacing { get; }
        int GetRow(IView view);
        int GetRowSpan(IView view);
        int GetColumn(IView view);
        int GetColumnSpan(IView view);
    }

    public interface IFlexLayout : ILayout
    {
        void Layout(double width, double height);
        Rect GetFlexFrame(IView view);
    }

    public interface IContentView : IView, IPadding
    {
        object? Content { get; }
        IView? PresentedContent { get; }
    }
}
