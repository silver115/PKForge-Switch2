using PKForge.Domain;

namespace PKForge.App.Views;

public enum HomeWidgetTab { Pokepark, Walker, Partner, Egg }

/// <summary>
/// The Switch 2's form of Views/WidgetMenu.cs. Home-screen widgets are an Android feature that the port drops
/// (design section 7.8), so the menu never opens and no widget is offered.
/// </summary>
public static class WidgetMenu
{
    public static Task ShowAsync(Grid host, HomeWidgetTab start) => Task.CompletedTask;

    public static Task OfferOnceAsync(Grid host, HomeWidgetKind kind, string line) => Task.CompletedTask;
}
