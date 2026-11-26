using PKForge.Switch2.Platform;

namespace PKForge.Switch2.Spike;

public static class SavePicker
{
    /// <summary>The first Legends: Z-A entry in user order, else the first entry of any game, else null.</summary>
    public static ConsoleSaveEntry? Pick(IReadOnlyList<CatalogResult> perUser)
    {
        var all = perUser.SelectMany(result => result.Saves).ToList();
        return all.FirstOrDefault(entry => entry.GameName.StartsWith("Legends: Z-A", StringComparison.Ordinal)) ?? all.FirstOrDefault();
    }
}
