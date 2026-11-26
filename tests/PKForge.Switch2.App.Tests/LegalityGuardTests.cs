using System.Security.Cryptography;
using PKForge.Domain;
using PKForge.Engine;
using PKForge.Switch2.Headless;
using PKForge.Switch2.Platform;
using Xunit;

namespace PKForge.Switch2.App.Tests;

// The seeded Brilliant Diamond save's six Pokémon have no moves, so PKHeX calls every one of them illegal: the guard
// must still let the save be written unchanged, or with them moved, and refuse only a changed one.
public sealed class LegalityGuardTests : IDisposable
{
    readonly string _saves = Directory.CreateTempSubdirectory("pkf-guard-").FullName;
    readonly SaveEngine _engine = new();

    public void Dispose() => Directory.Delete(_saves, recursive: true);

    static readonly string Doc = ConsoleDocumentId.Format(0x0100000011D90000, new ConsoleUser(1, 1), "SaveData.bin");

    sealed class Backups(params BackupInfo[] points) : IBackupService
    {
        public ValueTask<BackupReceipt> CreateAsync(SaveSnapshot source, string? changeDescription = null, string? documentId = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<BackupInfo>>(points);
        public ValueTask<ReadOnlyMemory<byte>> ReadAsync(string backupId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    byte[] Seeded()
    {
        SaveSeeder.Seed(_saves);
        return File.ReadAllBytes(Path.Combine(_saves, SaveSeeder.BrilliantDiamond, SaveSeeder.User, "SaveData.bin"));
    }

    byte[] Edited(byte[] bytes, Action<ISaveEngineSession> edit)
    {
        using var session = _engine.OpenSession(bytes, "Brilliant Diamond");
        edit(session);
        return session.Serialize().ToArray();
    }

    ConsoleLegalityGuard Guard(bool hax = false, IBackupService? backups = null) =>
        new(_engine, new LegalityService(), backups ?? new Backups(), () => hax);

    [Fact]
    public async Task Guard_SameSave_IsAllowed()
    {
        var bytes = Seeded();

        Assert.Null(await Guard().RefusalAsync(Doc, bytes, bytes, default));
    }

    [Fact]
    public async Task Guard_ChangedIllegalPokemon_IsRefusedByName()
    {
        var bytes = Seeded();
        var candidate = Edited(bytes, session => session.ApplyEdit(0, 0, new EntityEdit(Nickname: "Changed")));

        var refusal = await Guard().RefusalAsync(Doc, bytes, candidate, default);

        Assert.NotNull(refusal);
        Assert.Contains("Changed", refusal);
        Assert.Contains("can get an account banned", refusal);
        Assert.StartsWith("Not saved: ", refusal);
    }

    [Fact]
    public async Task Guard_MovedIllegalPokemon_IsAllowed()
    {
        var bytes = Seeded();
        var candidate = Edited(bytes, session => session.MoveSlot(0, 0, 0, 20));

        Assert.Null(await Guard().RefusalAsync(Doc, bytes, candidate, default));
    }

    [Fact]
    public async Task Guard_HaXOn_IsAllowed()
    {
        var bytes = Seeded();
        var candidate = Edited(bytes, session => session.ApplyEdit(0, 0, new EntityEdit(Nickname: "Changed")));

        Assert.Null(await Guard(hax: true).RefusalAsync(Doc, bytes, candidate, default));
    }

    [Fact]
    public async Task Guard_RestorePoint_IsAllowed()
    {
        var bytes = Seeded();
        var candidate = Edited(bytes, session => session.ApplyEdit(0, 0, new EntityEdit(Nickname: "Changed")));
        var point = new BackupInfo("point", DateTimeOffset.UtcNow, Convert.ToHexString(SHA256.HashData(candidate)), "Brilliant Diamond",
            "SAV8BS", 8, candidate.Length, DocumentId: Doc);

        Assert.Null(await Guard(backups: new Backups(point)).RefusalAsync(Doc, bytes, candidate, default));
    }
}
