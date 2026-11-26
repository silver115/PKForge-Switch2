using Xunit;

namespace PKForge.Switch2.Platform.Tests;

public sealed class ConsoleWriteTests : IDisposable
{
    const ulong ZA = 0x0100F43008C44000, Sword = 0x0100ABF008968000;
    static readonly ConsoleUser User = new(1, 1);
    static readonly DateTime When = new(2026, 11, 23, 6, 15, 0, DateTimeKind.Utc);

    readonly string _backups = Directory.CreateTempSubdirectory("pkf-write-").FullName;

    public void Dispose() => Directory.Delete(_backups, recursive: true);

    static string Doc(ulong title) => ConsoleDocumentId.Format(title, User, "main");

    ConsoleSaveFileAccess Access(FakeHost host, IConsoleWriteGuard? guard = null, string? backupRoot = null) =>
        new(host, backupRoot ?? _backups, () => When, guard);

    sealed class RefusingGuard(string message) : IConsoleWriteGuard
    {
        public ValueTask<string?> RefusalAsync(string documentId, ReadOnlyMemory<byte> current, ReadOnlyMemory<byte> candidate,
            CancellationToken cancellationToken) => ValueTask.FromResult<string?>(message);
    }

    static async Task<T> Refused<T>(Func<Task> write) where T : Exception => await Assert.ThrowsAsync<T>(write);

    [Fact]
    public async Task Write_ZA_ReadThenWrite_WritesCommitsAndReadsBack()
    {
        var host = new FakeHost().Add(ZA, User, "main", [1, 2, 3]);
        var access = Access(host);

        await access.ReadAsync(Doc(ZA));
        await access.WriteAtomicallyAsync(Doc(ZA), new byte[] { 4, 5, 6 });

        Assert.Equal(1, host.Commits);
        Assert.Equal([4, 5, 6], host.ReadFile(ZA, User, "main"));
    }

    // The other games whose writes are switched on, each with the file it keeps its save in.
    [Theory]
    [InlineData(0x01008F6008C5E000UL, "main")]          // Violet
    [InlineData(0x01001F5010DFA000UL, "main")]          // Legends: Arceus
    [InlineData(0x010003F003A34000UL, "savedata.bin")]  // Let's Go Pikachu
    public async Task Write_OtherVerifiedGame_IsWritten(ulong title, string file)
    {
        var host = new FakeHost().Add(title, User, file, [1, 2, 3]);
        var access = Access(host);
        var doc = ConsoleDocumentId.Format(title, User, file);

        await access.ReadAsync(doc);
        await access.WriteAtomicallyAsync(doc, new byte[] { 4, 5, 6 });

        Assert.Equal([4, 5, 6], host.ReadFile(title, User, file));
    }

    [Fact]
    public async Task Write_OtherGame_IsRefused()
    {
        var host = new FakeHost().Add(Sword, User, "main", [1, 2, 3]);
        var access = Access(host);
        await access.ReadAsync(Doc(Sword));

        var error = await Refused<InvalidOperationException>(() => access.WriteAtomicallyAsync(Doc(Sword), new byte[] { 4, 5, 6 }).AsTask());

        Assert.Equal("Writing Sword saves on Switch 2 is not verified yet, so PKForge only reads them.", error.Message);
        Assert.Empty(host.Writes);
    }

    [Fact]
    public async Task Write_WithoutReadingFirst_IsRefused()
    {
        var host = new FakeHost().Add(ZA, User, "main", [1, 2, 3]);

        var error = await Refused<InvalidOperationException>(() => Access(host).WriteAtomicallyAsync(Doc(ZA), new byte[] { 4, 5, 6 }).AsTask());

        Assert.Equal("PKForge has not read this save since it started. Open it again before saving.", error.Message);
        Assert.Empty(host.Writes);
    }

    [Fact]
    public async Task Write_SaveChangedUnderneath_IsRefused()
    {
        var host = new FakeHost().Add(ZA, User, "main", [1, 2, 3]);
        var access = Access(host);
        await access.ReadAsync(Doc(ZA));
        host.BeforeWriteRead = () => host.Add(ZA, User, "main", [9, 9, 9]);

        var error = await Refused<InvalidOperationException>(() => access.WriteAtomicallyAsync(Doc(ZA), new byte[] { 4, 5, 6 }).AsTask());

        Assert.Equal("The save changed on the console since PKForge opened it. Open it again before saving.", error.Message);
        Assert.Empty(host.Writes);
    }

    [Fact]
    public async Task Write_OtherSize_IsRefused()
    {
        var host = new FakeHost().Add(ZA, User, "main", [1, 2, 3]);
        var access = Access(host);
        await access.ReadAsync(Doc(ZA));

        var error = await Refused<InvalidOperationException>(() => access.WriteAtomicallyAsync(Doc(ZA), new byte[] { 4, 5 }).AsTask());

        Assert.Equal("The new save is a different size from the one on the console, so it was not written.", error.Message);
        Assert.Empty(host.Writes);
    }

    [Fact]
    public async Task Write_NoRoom_IsRefused()
    {
        var host = new FakeHost { Journal = 2 }.Add(ZA, User, "main", [1, 2, 3]);
        var access = Access(host);
        await access.ReadAsync(Doc(ZA));

        var error = await Refused<InvalidOperationException>(() => access.WriteAtomicallyAsync(Doc(ZA), new byte[] { 4, 5, 6 }).AsTask());

        Assert.Equal("The console save has no room for this write, so nothing was written.", error.Message);
        Assert.Empty(host.Writes);
    }

    [Fact]
    public async Task Write_CommitFails_ThrowsAndRecordsNothing()
    {
        var host = new FakeHost { FailCommit = true }.Add(ZA, User, "main", [1, 2, 3]);
        var access = Access(host);
        await access.ReadAsync(Doc(ZA));

        var error = await Refused<IOException>(() => access.WriteAtomicallyAsync(Doc(ZA), new byte[] { 4, 5, 6 }).AsTask());

        Assert.Equal("The console did not commit the save, so nothing was written.", error.Message);
        Assert.Equal([1, 2, 3], host.ReadFile(ZA, User, "main"));
    }

    [Fact]
    public async Task Write_ReadBackDiffers_Throws()
    {
        var host = new FakeHost { CorruptReadBack = true }.Add(ZA, User, "main", [1, 2, 3]);
        var access = Access(host);
        await access.ReadAsync(Doc(ZA));

        var error = await Refused<IOException>(() => access.WriteAtomicallyAsync(Doc(ZA), new byte[] { 4, 5, 6 }).AsTask());

        Assert.Equal("The save read back from the console differs from what was written. Restore the restore point made before this save.",
            error.Message);
    }

    [Fact]
    public async Task Write_Twice_CopiesTheOriginalOnce()
    {
        var host = new FakeHost().Add(ZA, User, "main", [1, 2, 3]);
        var access = new ConsoleSaveFileAccess(host, _backups, () => DateTime.UtcNow);
        await access.ReadAsync(Doc(ZA));

        await access.WriteAtomicallyAsync(Doc(ZA), new byte[] { 4, 5, 6 });
        await Task.Delay(1100);  // a second copy would get its own dated folder
        await access.WriteAtomicallyAsync(Doc(ZA), new byte[] { 7, 8, 9 });

        var dated = Assert.Single(Directory.GetDirectories(_backups));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(dated, "0100F43008C44000 Legends Z-A", User.Hex, "main")));
        Assert.Equal([7, 8, 9], host.ReadFile(ZA, User, "main"));
    }

    [Fact]
    public async Task Write_OriginalCopyFails_IsRefusedAndNothingWritten()
    {
        var host = new FakeHost().Add(ZA, User, "main", [1, 2, 3]);
        var blocker = Path.Combine(_backups, "a file");
        File.WriteAllText(blocker, "the backup folder cannot be made under a file");
        var access = Access(host, backupRoot: Path.Combine(blocker, "save-backups"));
        await access.ReadAsync(Doc(ZA));

        var error = await Refused<InvalidOperationException>(() => access.WriteAtomicallyAsync(Doc(ZA), new byte[] { 4, 5, 6 }).AsTask());

        Assert.Equal("PKForge could not copy the original save to the SD card first, so nothing was written.", error.Message);
        Assert.Empty(host.Writes);
    }

    [Fact]
    public async Task Write_GuardRefuses_NothingWritten()
    {
        var host = new FakeHost().Add(ZA, User, "main", [1, 2, 3]);
        var access = Access(host, new RefusingGuard("Not saved: Pikachu would be illegal."));
        await access.ReadAsync(Doc(ZA));

        var error = await Refused<InvalidOperationException>(() => access.WriteAtomicallyAsync(Doc(ZA), new byte[] { 4, 5, 6 }).AsTask());

        Assert.Equal("Not saved: Pikachu would be illegal.", error.Message);
        Assert.Empty(host.Writes);
    }
}
