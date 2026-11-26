using Xunit;

namespace PKForge.Switch2.Platform.Tests;

public class NativeConsoleSaveHostTests
{
    [Fact]
    public void ParseFileList_ReadsTabSeparatedLines() =>
        Assert.Equal(new[] { new ConsoleSaveFile("main", 3145728), new ConsoleSaveFile("backup/main", 12) },
            NativeConsoleSaveHost.ParseFileList("main\t3145728\nbackup/main\t12\n"));

    [Fact]
    public void ParseFileList_RejectsLineWithoutTab() =>
        Assert.Throws<FormatException>(() => NativeConsoleSaveHost.ParseFileList("main3145728\n"));
}
