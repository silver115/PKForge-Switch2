namespace Microsoft.Maui.Controls;

/// <summary>MAUI's view: a visual element with a margin and alignment options.</summary>
public abstract partial class View : VisualElement
{
    public static readonly BindableProperty MarginProperty = BindableProperty.Create(nameof(Margin), typeof(Thickness), typeof(View), Thickness.Zero).Measured();
    public static readonly BindableProperty HorizontalOptionsProperty = BindableProperty.Create(nameof(HorizontalOptions), typeof(LayoutOptions), typeof(View), LayoutOptions.Fill).Measured();
    public static readonly BindableProperty VerticalOptionsProperty = BindableProperty.Create(nameof(VerticalOptions), typeof(LayoutOptions), typeof(View), LayoutOptions.Fill).Measured();

    public Thickness Margin { get => (Thickness)GetValue(MarginProperty)!; set => SetValue(MarginProperty, value); }
    public LayoutOptions HorizontalOptions { get => (LayoutOptions)GetValue(HorizontalOptionsProperty)!; set => SetValue(HorizontalOptionsProperty, value); }
    public LayoutOptions VerticalOptions { get => (LayoutOptions)GetValue(VerticalOptionsProperty)!; set => SetValue(VerticalOptionsProperty, value); }
}
