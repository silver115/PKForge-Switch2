using Microsoft.Maui.Layouts;
using PKForge.MauiLite;

namespace PKForge.MauiLite.Tests;

// The expected rectangles follow MAUI's own layout rules (its layout managers are ported unchanged).
public class LayoutTests
{
    static void Lay(View root, double width, double height) => LayoutPass.Run(root, new Size(width, height));

    [Fact]
    public void Grid_SplitsColumnsAutoAbsoluteStar()
    {
        var auto = new BoxView { WidthRequest = 40 };
        var a = new BoxView();
        var b = new BoxView();
        var grid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = { new(GridLength.Auto), new(100), new(GridLength.Star), new(new GridLength(2, GridUnitType.Star)) },
        };
        grid.Add(auto, 0, 0);
        grid.Add(a, 2, 0);
        grid.Add(b, 3, 0);

        Lay(grid, 400, 100);

        Assert.Equal(new Rect(160, 0, 230 / 3.0, 100), a.Bounds);
        Assert.Equal(new Rect(170 + 230 / 3.0, 0, 460 / 3.0, 100), b.Bounds);
    }

    [Fact]
    public void Grid_ColumnSpanCoversSpacing()
    {
        var wide = new BoxView();
        var grid = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
        grid.Add(wide, 0, 0);
        Grid.SetColumnSpan(wide, 2);

        Lay(grid, 210, 50);

        Assert.Equal(new Rect(0, 0, 210, 50), wide.Bounds);
    }

    [Fact]
    public void Grid_ChildMarginInsetsFrame()
    {
        var child = new BoxView { Margin = 5 };
        var grid = new Grid { child };

        Lay(grid, 100, 100);

        Assert.Equal(new Rect(5, 5, 90, 90), child.Bounds);
    }

    [Fact]
    public void Grid_CenteredChildKeepsRequest()
    {
        var child = new BoxView { WidthRequest = 20, HeightRequest = 10, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        var grid = new Grid { child };

        Lay(grid, 100, 100);

        Assert.Equal(new Rect(40, 45, 20, 10), child.Bounds);
    }

    [Fact]
    public void VerticalStack_SpacingPaddingSkipHidden()
    {
        var first = new BoxView { HeightRequest = 20 };
        var hidden = new BoxView { HeightRequest = 50, IsVisible = false };
        var last = new BoxView { HeightRequest = 30 };
        var stack = new VerticalStackLayout { Padding = 10, Spacing = 5, Children = { first, hidden, last } };

        Assert.Equal(75, stack.Measure(200, double.PositiveInfinity).Height);
        Lay(stack, 200, 500);

        Assert.Equal(new Rect(10, 10, 180, 20), first.Bounds);
        Assert.Equal(35, last.Bounds.Y);
    }

    [Fact]
    public void HorizontalStack_LaysOutLeftToRight()
    {
        var first = new BoxView { WidthRequest = 30 };
        var second = new BoxView { WidthRequest = 40 };
        var stack = new HorizontalStackLayout { Spacing = 6, Children = { first, second } };

        Lay(stack, 300, 50);

        Assert.Equal(36, second.Bounds.X);
    }

    [Fact]
    public void ScrollView_ContentMeasuredUnboundedVertically()
    {
        var content = new VerticalStackLayout
        {
            Children = { new BoxView { HeightRequest = 100 }, new BoxView { HeightRequest = 100 }, new BoxView { HeightRequest = 100 } },
        };
        var scroll = new ScrollView { Content = content };

        Lay(scroll, 200, 150);

        Assert.Equal(300, content.Bounds.Height);
    }

    [Fact]
    public void FlexLayout_WrapMovesThirdChildToNextLine()
    {
        View Box() => new BoxView { WidthRequest = 60, HeightRequest = 20 };
        var third = Box();
        // AlignContent Start: MAUI's default (Stretch) would spread the two lines over the whole height.
        var flex = new FlexLayout { Wrap = FlexWrap.Wrap, AlignContent = FlexAlignContent.Start, Children = { Box(), Box(), third } };
        // MAUI's FlexLayout builds its flex tree once it has a parent, as it always does inside a page.
        var page = new Grid { flex };

        Lay(page, 150, 100);

        Assert.Equal(new Rect(0, 20, 60, 20), third.Bounds);
    }

    [Fact]
    public void ChangingWidthRequest_MarksRootForLayout()
    {
        var child = new BoxView();
        var root = new Grid { new VerticalStackLayout { Children = { child } } };
        Lay(root, 100, 100);
        Assert.False(root.NeedsLayout);

        child.WidthRequest = 30;

        Assert.True(root.NeedsLayout);
    }
}
