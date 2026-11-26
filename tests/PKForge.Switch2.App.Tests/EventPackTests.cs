using PKForge.Switch2;

namespace PKForge.Switch2.App.Tests;

// EventArchive unpacks each generation's mystery-gift pack into the cache once and keeps it. A build that ships
// newer packs must not keep offering the events an older pack unpacked.
public sealed class EventPackTests : IDisposable
{
    readonly string _cache = Directory.CreateTempSubdirectory("pkforge-cache-").FullName;

    public void Dispose() => Directory.Delete(_cache, recursive: true);

    string Unpacked => Path.Combine(_cache, "events", "g9", "1613 ZA - Kalos Shiny Xerneas.wa9");

    static Func<string, Stream> PacksOf(int size) => _ => new MemoryStream(new byte[size]);

    void Unpack()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Unpacked)!);
        File.WriteAllText(Unpacked, "gift");
    }

    [Fact]
    public void NewerPacks_ClearTheUnpackedEvents_SamePacksKeepThem()
    {
        AppHost.ClearStaleEvents(_cache, PacksOf(100));
        Unpack();

        AppHost.ClearStaleEvents(_cache, PacksOf(100));
        Assert.True(File.Exists(Unpacked));

        AppHost.ClearStaleEvents(_cache, PacksOf(120));
        Assert.False(File.Exists(Unpacked));
        // Only files go: removing the folders failed on the console's SD card ("I/O error"), and EventArchive
        // unpacks again into a folder that holds no files.
        Assert.True(Directory.Exists(Path.GetDirectoryName(Unpacked)));
    }

    // The console's first run of this check closed the app at start-up: nothing here may stop it.
    [Fact]
    public void ACacheThatCannotBeCleared_DoesNotStopTheApp()
    {
        File.WriteAllText(Path.Combine(_cache, "events"), "a file where the events folder should be");

        AppHost.ClearStaleEvents(_cache, PacksOf(100));
    }

    [Fact]
    public void MissingPacks_DoNotStopTheApp()
    {
        AppHost.ClearStaleEvents(_cache, name => throw new FileNotFoundException(name));

        Assert.True(Directory.Exists(Path.Combine(_cache, "events")));
    }
}
