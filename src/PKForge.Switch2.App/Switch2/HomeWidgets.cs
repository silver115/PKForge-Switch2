using PKForge.Domain;

namespace PKForge.App;

// The Switch 2's form of Platforms/Android/HomeWidgets.cs and PokeparkWidget.cs. Home-screen widgets are an Android
// feature that the port drops (design section 7.8): no widget exists, so none is offered, refreshed or pinned.

public sealed record LastSave(string DocumentId, string Name);

public static class HomeWidgetStore
{
    public static HomeWidgetSettings Settings => new();

    public static LastSave? Last => null;
}

public static class HomeWidgets
{
    public static bool HasAny(HomeWidgetKind kind) => false;

    public static PartnerChoice? PartnerFrom(byte[] data, string? format, string origin, Guid? bankId, string? documentId) => null;

    public static Task SetPartnerAsync(PartnerChoice? partner) => Task.CompletedTask;

    public static Task CaptureOpenSaveAsync() => Task.CompletedTask;
}

public static class PokeparkWidgetPublisher
{
    public static bool HasActiveWidgets() => false;

    public static Task RefreshAsync(TimeSpan spriteWait, bool always = false) => Task.CompletedTask;
}
