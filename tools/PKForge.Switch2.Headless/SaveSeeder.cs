using PKHeX.Core;

namespace PKForge.Switch2.Headless;

/// <summary>
/// Fake console storage with PKHeX's blank saves. Among the Switch games PKHeX makes a blank its own detection accepts
/// only for Brilliant Diamond and Shining Pearl, so those stand in for the console's saves on the headless host.
/// </summary>
public static class SaveSeeder
{
    public const string User = "00000000000000010000000000000001";
    public const string BrilliantDiamond = "0100000011D90000";
    public const string ShiningPearl = "010018E011D92000";

    /// <summary>Box 1, slots 1–6, all at level 50: Pikachu, Charizard, Lucario, Garchomp, Dragonite, Metagross.</summary>
    static readonly ushort[] BoxOne = [25, 6, 448, 445, 149, 376];

    public static void Seed(string savesRoot)
    {
        var diamond = Blank(GameVersion.BD, BoxOne);
        Write(savesRoot, BrilliantDiamond, "SaveData.bin", diamond);
        // A game's own backup copy, which the shelf keeps under Hidden saves.
        Write(savesRoot, BrilliantDiamond, "backup", diamond);
        Write(savesRoot, ShiningPearl, "SaveData.bin", Blank(GameVersion.SP, []));
    }

    static byte[] Blank(GameVersion version, IReadOnlyList<ushort> species)
    {
        var save = BlankSaveFile.Get(version, "PKFORGE");
        for (var slot = 0; slot < species.Count; slot++)
        {
            var pk = save.BlankPKM;
            pk.Species = species[slot];
            pk.CurrentLevel = 50;
            pk.ClearNickname();
            pk.OriginalTrainerName = save.OT;
            pk.ID32 = save.ID32;
            pk.Language = save.Language;
            pk.Version = save.Version;
            pk.RefreshChecksum();
            save.SetBoxSlotAtIndex(pk, 0, slot);
        }
        return save.Write().ToArray();
    }

    static void Write(string root, string title, string file, byte[] bytes)
    {
        var folder = Path.Combine(root, title, User);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, file), bytes);
    }
}
