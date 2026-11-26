using System.Security.Cryptography;
using Xunit;

namespace PKForge.Switch2.Platform.Tests;

public sealed class ConsoleSaveBackupTests : IDisposable
{
    const ulong ZA = 0x0100F43008C44000, Violet = 0x01008F6008C5E000, NotPokemon = 0x0100000000010000;
    static readonly ConsoleUser First = new(1, 1), Second = new(2, 2);
    static readonly DateTime When = new(2026, 11, 23, 6, 15, 0, DateTimeKind.Utc);

    readonly string _root = Directory.CreateTempSubdirectory("pkf-backup-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    string Dated => Path.Combine(_root, "20261123-061500");

    static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    [Fact]
    public void All_CopiesEveryFileOfEveryAllowedSave()
    {
        var host = new FakeHost()
            .Add(ZA, First, "main", Bytes("za main"))
            .Add(ZA, First, "backup", Bytes("za backup"))
            .Add(Violet, Second, "main", Bytes("violet main"));

        var result = ConsoleSaveBackup.All(host, _root, When);

        Assert.True(result.Passed);
        Assert.Equal(Dated, result.Folder);
        Assert.Equal(3, result.Files.Count);
        Assert.Equal(Bytes("za main"), File.ReadAllBytes(Path.Combine(Dated, "0100F43008C44000 Legends Z-A", First.Hex, "main")));
        Assert.Equal(Bytes("violet main"), File.ReadAllBytes(Path.Combine(Dated, "01008F6008C5E000 Violet", Second.Hex, "main")));
        var manifest = File.ReadAllLines(Path.Combine(Dated, "manifest.txt"));
        Assert.Contains($"{Convert.ToHexStringLower(SHA256.HashData(Bytes("za backup")))}  9  0100F43008C44000 Legends Z-A/{First.Hex}/backup", manifest);
        Assert.Equal(3, manifest.Length);
    }

    [Fact]
    public void All_SkipsTitlesNotAllowed()
    {
        var host = new FakeHost().Add(ZA, First, "main", Bytes("za")).Add(NotPokemon, First, "save", Bytes("other game"));

        var result = ConsoleSaveBackup.All(host, _root, When);

        Assert.Single(result.Files);
        Assert.DoesNotContain(host.Reads, read => read.Title == NotPokemon);
    }

    [Fact]
    public void All_RecordsAFailureAndGoesOn()
    {
        var host = new FakeHost().Add(Violet, First, "main", Bytes("violet")).Add(ZA, First, "main", Bytes("za"));
        host.FailReads.Add(Violet);

        var result = ConsoleSaveBackup.All(host, _root, When);

        Assert.False(result.Passed);
        Assert.Contains("Violet", Assert.Single(result.Failures));
        Assert.Equal(Bytes("za"), File.ReadAllBytes(Path.Combine(Dated, "0100F43008C44000 Legends Z-A", First.Hex, "main")));
    }

    [Fact]
    public void One_CopiesOnlyThatSave()
    {
        var host = new FakeHost().Add(ZA, First, "main", Bytes("za")).Add(Violet, First, "main", Bytes("violet"));

        var result = ConsoleSaveBackup.One(host, ZA, First, _root, When);

        Assert.True(result.Passed);
        Assert.Equal($"0100F43008C44000 Legends Z-A/{First.Hex}/main", Assert.Single(result.Files).RelativePath);
    }

    [Fact]
    public void Folder_HasNoColon()
    {
        var host = new FakeHost().Add(ZA, First, "main", Bytes("za"));

        ConsoleSaveBackup.All(host, _root, When);

        Assert.True(Directory.Exists(Path.Combine(Dated, "0100F43008C44000 Legends Z-A")));
    }
}
