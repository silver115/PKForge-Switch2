using Xunit;

namespace PKForge.Switch2.Platform.Tests;

public class ConsoleDocumentIdTests
{
    static readonly ConsoleUser User = new(0x0123456789ABCDEF, 0xFEDCBA9876543210);

    [Fact]
    public void Format_UsesUpperHexAndSlash() =>
        Assert.Equal("console:0100F43008C44000:0123456789ABCDEFFEDCBA9876543210/main",
            ConsoleDocumentId.Format(0x0100F43008C44000, User, "main"));

    [Fact]
    public void TryParse_RoundTrips()
    {
        Assert.True(ConsoleDocumentId.TryParse(ConsoleDocumentId.Format(0x0100F43008C44000, User, "dir/main"), out var t, out var u, out var f));
        Assert.Equal((0x0100F43008C44000UL, User, "dir/main"), (t, u, f));
    }

    [Theory]
    [InlineData("console:0100F43008C4400:0123456789ABCDEFFEDCBA9876543210/main")]
    [InlineData("console:0100F43008C44000:0123/main")]
    [InlineData("console:0100F43008C44000:0123456789ABCDEFFEDCBA9876543210")]
    [InlineData("folder:/storage/main")]
    public void TryParse_RejectsMalformed(string id) => Assert.False(ConsoleDocumentId.TryParse(id, out _, out _, out _));
}
