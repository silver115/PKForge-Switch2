using Microsoft.Maui.Layouts;

namespace Microsoft.Maui.Controls;

public abstract class StackBase : Layout, IStackLayout
{
    public static readonly BindableProperty SpacingProperty = BindableProperty.Create(nameof(Spacing), typeof(double), typeof(StackBase), 0d).Measured();

    public double Spacing { get => (double)GetValue(SpacingProperty)!; set => SetValue(SpacingProperty, value); }
}

public class VerticalStackLayout : StackBase
{
    protected override ILayoutManager CreateLayoutManager() => new VerticalStackLayoutManager(this);
}

public class HorizontalStackLayout : StackBase
{
    protected override ILayoutManager CreateLayoutManager() => new HorizontalStackLayoutManager(this);
}
