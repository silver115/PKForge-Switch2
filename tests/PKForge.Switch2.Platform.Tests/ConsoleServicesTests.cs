using Microsoft.Extensions.DependencyInjection;
using PKForge.Domain;
using PKForge.Engine;
using PKForge.Infrastructure;
using PKHeX.Core;
using Xunit;

namespace PKForge.Switch2.Platform.Tests;

public sealed class ConsoleServicesTests : IDisposable
{
    const ulong ZA = 0x0100F43008C44000;
    const ulong BD = 0x0100000011D90000;
    const ulong Violet = 0x01008F6008C5E000;
    static readonly ConsoleUser User = new(0x0000000000000001, 0x0000000000000001);

    readonly string _root = Directory.CreateTempSubdirectory("pkf-saves-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    void Put(ulong title, ConsoleUser user, string file, byte[] bytes)
    {
        var folder = Path.Combine(_root, title.ToString("X16"), user.Hex);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, file), bytes);
    }

    // PKHeX can make a blank save its own detection accepts only for Brilliant Diamond and Shining Pearl among the
    // Switch games (the blank Z-A, Scarlet/Violet, Sword/Shield, Arceus and Let's Go saves have other sizes than real ones).
    static byte[] BlankBd() => BlankSaveFile.Get(GameVersion.BD, "PKFORGE").Write().ToArray();

    static string BdName => EmulatorSaveHeuristics.SwitchTitles["0100000011D90000"];

    [Fact]
    public void FolderHost_ListsUsersSavesAndFiles()
    {
        Put(ZA, User, "main", [1, 2, 3]);
        Put(ZA, User, "backup", [4, 5]);
        var host = new FolderConsoleSaveHost(_root);

        Assert.Equal([User], host.ListUsers());
        Assert.Equal(new ConsoleSaveInfo(ZA, User, 5), Assert.Single(host.ListAccountSaves()));
        Assert.Equal(["backup", "main"], host.ListFiles(ZA, User).Select(f => f.Path).Order());
        Assert.Equal([1, 2, 3], host.ReadFile(ZA, User, "main"));
    }

    [Fact]
    public void FolderHost_RejectsPathsOutsideTheSave()
    {
        Put(ZA, User, "main", [1]);
        var host = new FolderConsoleSaveHost(_root);

        Assert.Throws<ArgumentException>(() => host.ReadFile(ZA, User, "../x"));
    }

    sealed class SlowHost : IConsoleSaveHost
    {
        int _inside;
        public int MostAtOnce;

        public IReadOnlyList<ConsoleUser> ListUsers() => [];
        public IReadOnlyList<ConsoleSaveInfo> ListAccountSaves() => [];
        public IReadOnlyList<ConsoleSaveFile> ListFiles(ulong titleId, ConsoleUser user) => [];
        public IConsoleSaveWriter OpenForWrite(ulong titleId, ConsoleUser user) => throw new NotSupportedException();

        public byte[] ReadFile(ulong titleId, ConsoleUser user, string path)
        {
            var now = Interlocked.Increment(ref _inside);
            MostAtOnce = Math.Max(MostAtOnce, now);
            Thread.Sleep(5);
            Interlocked.Decrement(ref _inside);
            return [];
        }
    }

    [Fact]
    public void FolderHost_WriteThenCommit_ReplacesTheFile()
    {
        Put(ZA, User, "main", [1, 2, 3]);
        var host = new FolderConsoleSaveHost(_root);

        using (var writer = host.OpenForWrite(ZA, User))
        {
            Assert.Equal([1, 2, 3], writer.ReadFile("main"));
            writer.WriteFile("main", [4, 5, 6]);
            writer.Commit();
        }

        Assert.Equal([4, 5, 6], host.ReadFile(ZA, User, "main"));
    }

    [Fact]
    public void FolderHost_WriteWithoutCommit_LeavesTheFile()
    {
        Put(ZA, User, "main", [1, 2, 3]);
        var host = new FolderConsoleSaveHost(_root);

        using (var writer = host.OpenForWrite(ZA, User))
            writer.WriteFile("main", [4, 5, 6]);

        Assert.Equal([1, 2, 3], host.ReadFile(ZA, User, "main"));
    }

    [Fact]
    public async Task SerializedHost_OpenForWrite_HoldsTheLockUntilDisposed()
    {
        Put(ZA, User, "main", [1, 2, 3]);
        var host = new SerializedConsoleSaveHost(new FolderConsoleSaveHost(_root));

        var writer = host.OpenForWrite(ZA, User);
        var read = Task.Run(() => host.ReadFile(ZA, User, "main"));

        Assert.NotSame(read, await Task.WhenAny(read, Task.Delay(300)));
        writer.Dispose();
        Assert.Equal([1, 2, 3], await read.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void SerializedHost_NeverOverlapsCalls()
    {
        var slow = new SlowHost();
        var host = new SerializedConsoleSaveHost(slow);

        Parallel.For(0, 8, _ => host.ReadFile(ZA, User, "main"));

        Assert.Equal(1, slow.MostAtOnce);
    }

    [Fact]
    public async Task FileAccess_ReadsConsoleDocument()
    {
        Put(ZA, User, "main", [7, 8, 9]);
        var access = new ConsoleSaveFileAccess(new FolderConsoleSaveHost(_root), _root + "-backups", () => DateTime.UtcNow);

        var bytes = await access.ReadAsync(ConsoleDocumentId.Format(ZA, User, "main"));

        Assert.Equal([7, 8, 9], bytes.ToArray());
    }

    [Fact]
    public async Task FileAccess_NonConsoleId_Throws()
    {
        var access = new ConsoleSaveFileAccess(new FolderConsoleSaveHost(_root), _root + "-backups", () => DateTime.UtcNow);

        await Assert.ThrowsAsync<ArgumentException>(() => access.ReadAsync("content://somewhere/main").AsTask());
    }

    [Fact]
    public async Task Scanner_ListsBlankSave()
    {
        Put(BD, User, "main", BlankBd());
        var scanner = new ConsoleSaveScanner(new FolderConsoleSaveHost(_root), new SaveEngine());

        var result = await scanner.ScanAsync(ConsoleWatchedRootStore.Root.TreeId, EmulatorKind.Console);

        var save = Assert.Single(result.Saves);
        Assert.Equal(EmulatorKind.Console, save.Emulator);
        Assert.Equal(BdName, save.GameLabel);
        Assert.Equal("PKFORGE", save.TrainerName);
        Assert.Equal("User 1", save.FolderHint);
        Assert.Equal(ConsoleDocumentId.Format(BD, User, "main"), save.DocumentId);
        Assert.False(save.IsCopy);
    }

    [Fact]
    public async Task Scanner_BackupFile_IsCopy()
    {
        var bytes = BlankBd();
        Put(BD, User, "main", bytes);
        Put(BD, User, "backup", bytes);
        var scanner = new ConsoleSaveScanner(new FolderConsoleSaveHost(_root), new SaveEngine());

        var saves = (await scanner.ScanAsync(ConsoleWatchedRootStore.Root.TreeId, EmulatorKind.Console)).Saves;

        Assert.True(saves.Single(s => s.FileName == "backup").IsCopy);
        Assert.False(saves.Single(s => s.FileName == "main").IsCopy);
    }

    [Fact]
    public async Task Scanner_JunkFile_IsRejectedOthersListed()
    {
        Put(BD, User, "main", BlankBd());
        Put(Violet, User, "main", [.. Enumerable.Range(0, 64).Select(i => (byte)(i * 37))]);
        var scanner = new ConsoleSaveScanner(new FolderConsoleSaveHost(_root), new SaveEngine());

        var result = await scanner.ScanAsync(ConsoleWatchedRootStore.Root.TreeId, EmulatorKind.Console);

        Assert.Equal(BdName, Assert.Single(result.Saves).GameLabel);
        Assert.StartsWith(ConsoleDocumentId.Format(Violet, User, "main") + ": ", Assert.Single(result.RejectedCandidates));
    }

    [Fact]
    public async Task Scanner_OtherTreeId_ReturnsNothing()
    {
        Put(BD, User, "main", BlankBd());
        var scanner = new ConsoleSaveScanner(new FolderConsoleSaveHost(_root), new SaveEngine());

        var result = await scanner.ScanAsync("content://tree/primary", EmulatorKind.Console);

        Assert.Empty(result.Saves);
    }

    [Fact]
    public void Register_ResolvesConsoleServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISaveEngine, SaveEngine>();
        ConsoleServices.Register(services, new FolderConsoleSaveHost(_root), _root + "-backups", _ => null);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ConsoleSaveFileAccess>(provider.GetRequiredService<ISaveFileAccess>());
        Assert.IsType<ConsoleSaveScanner>(provider.GetRequiredService<IEmulatorDetectionService>());
        Assert.IsType<ConsoleWatchedRootStore>(provider.GetRequiredService<IWatchedRootStore>());
        Assert.False(provider.GetRequiredService<ISecondaryDisplayHost>().IsAvailable);
        Assert.Empty(provider.GetRequiredService<IMusicPlayer>().Library);
    }
}
