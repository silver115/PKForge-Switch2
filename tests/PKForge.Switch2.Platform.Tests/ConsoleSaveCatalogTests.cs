using System.Text;
using Xunit;

namespace PKForge.Switch2.Platform.Tests;

public class ConsoleSaveCatalogTests
{
    const ulong Za = 0x0100F43008C44000, Other = 0x0100000000010000;
    static readonly ConsoleUser A = new(1, 1), B = new(2, 2);

    static byte[] SaveBytes() => Encoding.ASCII.GetBytes("SAVE-data");

    static FakeHost Host() => new FakeHost()
        .Add(Za, A, "main", SaveBytes())
        .Add(Za, B, "main", SaveBytes())
        .Add(Other, A, "data", [1, 2, 3]);

    [Fact]
    public void Scan_KeepsOnlyAllowedTitlesOfThatUser()
    {
        var entry = Assert.Single(new ConsoleSaveCatalog(Host(), new FakeEngine()).Scan(A).Saves);
        Assert.Equal((Za, A, "Legends: Z-A", "main"), (entry.TitleId, entry.User, entry.GameName, entry.File));
        Assert.Equal("console:0100F43008C44000:00000000000000010000000000000001/main", entry.DocumentId);
    }

    [Fact]
    public void Scan_ListsSkippedTitleIds()
    {
        var host = Host();
        var result = new ConsoleSaveCatalog(host, new FakeEngine()).Scan(A);
        Assert.Equal(new[] { Other }, result.SkippedTitleIds);
        Assert.DoesNotContain(host.Reads, read => read.Title == Other);
    }

    [Fact]
    public void Scan_ReportsOtherUsersWithSaves() =>
        Assert.Equal(new[] { B }, new ConsoleSaveCatalog(Host(), new FakeEngine()).Scan(A).OtherUsersWithSaves);

    [Fact]
    public void Scan_RecordsRejectionWhenNoFileDescribes()
    {
        var host = new FakeHost()
            .Add(Za, A, "main", Enumerable.Repeat((byte)'J', 100).ToArray())
            .Add(Za, A, "tiny", [(byte)'J']);
        var result = new ConsoleSaveCatalog(host, new FakeEngine()).Scan(A);
        Assert.Empty(result.Saves);
        var rejection = Assert.Single(result.Rejections);
        Assert.EndsWith("/main", rejection.DocumentId);
        Assert.Equal("not a known save size", rejection.Reason);
    }

    [Fact]
    public void AllowedTitleIds_AreTheTenSwitchTitles()
    {
        Assert.Equal(10, ConsoleSaveCatalog.AllowedTitleIds.Count);
        Assert.Contains(Za, ConsoleSaveCatalog.AllowedTitleIds);
    }

    // A real Switch 2 (2026-11-20) lists Legends: Arceus saves under this ID, as SwitchBrew does.
    [Fact]
    public void AllowedTitleIds_UseLegendsArceusConsoleId() =>
        Assert.Contains(0x01001F5010DFA000UL, ConsoleSaveCatalog.AllowedTitleIds);
}
