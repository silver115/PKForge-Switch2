namespace Microsoft.Maui.Controls;

// MAUI's reasons for a new measure (Controls.Internals). MauiLite treats them all alike.
enum InvalidationTrigger
{
    Undefined = 0,
    MeasureChanged = 1 << 0,
    HorizontalOptionsChanged = 1 << 1,
    VerticalOptionsChanged = 1 << 2,
    SizeRequestChanged = 1 << 3,
    RendererReady = 1 << 4,
    MarginChanged = 1 << 5,
}
