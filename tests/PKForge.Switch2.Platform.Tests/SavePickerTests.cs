using PKForge.Domain;
using PKForge.Switch2.Spike;
using Xunit;

namespace PKForge.Switch2.Platform.Tests;

public class SavePickerTests
{
    static readonly ConsoleUser A = new(1, 1), B = new(2, 2);

    static ConsoleSaveEntry Entry(ulong title, ConsoleUser user, string game) =>
        new(ConsoleDocumentId.Format(title, user, "main"), title, user, game, "main", new SaveDescription(game, 9, "FORGE", "1:00"));

    static CatalogResult Result(params ConsoleSaveEntry[] saves) => new(saves, [], [], []);

    [Fact]
    public void Pick_PrefersZaOverOtherGames()
    {
        var za = Entry(0x0100F43008C44000, B, "Legends: Z-A");
        Assert.Same(za, SavePicker.Pick([Result(Entry(0x0100ABF008968000, A, "Sword")), Result(za)]));
    }

    [Fact]
    public void Pick_ReturnsNullWhenNoEntries() => Assert.Null(SavePicker.Pick([Result(), Result()]));
}
