using System.Globalization;
using PKForge.Domain;
using PKForge.Infrastructure;

namespace PKForge.Switch2.Platform;

public sealed record ConsoleSaveEntry(string DocumentId, ulong TitleId, ConsoleUser User, string GameName, string File, SaveDescription Description);

public sealed record ConsoleRejection(string DocumentId, string Reason);

public sealed record CatalogResult(IReadOnlyList<ConsoleSaveEntry> Saves, IReadOnlyList<ulong> SkippedTitleIds,
    IReadOnlyList<ConsoleRejection> Rejections, IReadOnlyList<ConsoleUser> OtherUsersWithSaves);

/// <summary>
/// The titles PKForge may write on the console: a game is added once a console test shows it loads a save PKForge wrote
/// (parent design 7.6). The native host gets this list too and refuses the rest.
/// </summary>
public static class ConsoleWritableTitles
{
    public static IReadOnlyCollection<ulong> Ids { get; } =
    [
        0x0100F43008C44000,  // Legends: Z-A
        0x01008F6008C5E000,  // Violet
        0x01001F5010DFA000,  // Legends: Arceus
        0x010003F003A34000,  // Let's Go Pikachu
    ];
}

/// <summary>Finds a user's Pokémon saves in the console's save storage. Saves of other titles are never read.</summary>
public sealed class ConsoleSaveCatalog(IConsoleSaveHost host, ISaveEngine engine)
{
    private static readonly IReadOnlyDictionary<ulong, string> Games = EmulatorSaveHeuristics.SwitchTitles.ToDictionary(
        title => ulong.Parse(title.Key, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture), title => title.Value);

    public static IReadOnlyCollection<ulong> AllowedTitleIds { get; } = [.. Games.Keys];

    public CatalogResult Scan(ConsoleUser user)
    {
        var saves = host.ListAccountSaves();
        var entries = new List<ConsoleSaveEntry>();
        var rejections = new List<ConsoleRejection>();
        foreach (var save in saves.Where(s => s.User == user && Games.ContainsKey(s.TitleId)))
        {
            var game = Games[save.TitleId];
            (ConsoleSaveFile File, byte[] Bytes)? largest = null;
            var described = false;
            foreach (var file in host.ListFiles(save.TitleId, user))
            {
                var bytes = host.ReadFile(save.TitleId, user, file.Path);
                if (engine.TryDescribe(bytes, game) is { } description)
                {
                    entries.Add(new ConsoleSaveEntry(ConsoleDocumentId.Format(save.TitleId, user, file.Path), save.TitleId, user, game, file.Path, description));
                    described = true;
                }
                if (largest is null || file.Size > largest.Value.File.Size) largest = (file, bytes);
            }
            if (described) continue;
            rejections.Add(largest is { } big
                ? new ConsoleRejection(ConsoleDocumentId.Format(save.TitleId, user, big.File.Path),
                    engine.DescribeRejection(big.Bytes) ?? "no file was recognised as a save")
                : new ConsoleRejection(ConsoleDocumentId.Format(save.TitleId, user, ""), "the save holds no files"));
        }
        var skipped = saves.Where(s => s.User == user && !Games.ContainsKey(s.TitleId)).Select(s => s.TitleId).Distinct().Order().ToList();
        var others = host.ListUsers().Where(u => u != user && saves.Any(s => s.User == u && Games.ContainsKey(s.TitleId))).ToList();
        return new CatalogResult(entries, skipped, rejections, others);
    }
}
