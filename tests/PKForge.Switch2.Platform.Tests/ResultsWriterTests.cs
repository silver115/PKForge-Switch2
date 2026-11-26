using PKForge.Switch2.Spike;
using Xunit;

namespace PKForge.Switch2.Platform.Tests;

public sealed class ResultsWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pkf-results-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void ResultsWriter_CreatesDirectory()
    {
        using var writer = new ResultsWriter(Path.Combine(_root, "x", "y"), TextWriter.Null);
        writer.Write("a", "1");
        Assert.Equal("a=1\n", ReadShared(writer.FilePath!));
    }

    [Fact]
    public void ResultsWriter_FlushesEachLine()
    {
        using var writer = new ResultsWriter(_root, TextWriter.Null);
        writer.Write("a", "1");
        writer.Write("b", "2");
        Assert.Equal("a=1\nb=2\n", ReadShared(writer.FilePath!));
    }

    [Fact]
    public void ResultsWriter_FallsBackWhenUnwritable()
    {
        Directory.CreateDirectory(_root);
        var notADirectory = Path.Combine(_root, "file");
        File.WriteAllText(notADirectory, "");
        var fallback = new StringWriter();
        using var writer = new ResultsWriter(notADirectory, fallback);
        writer.Write("a", "1");
        Assert.Null(writer.FilePath);
        Assert.Contains("a=1", fallback.ToString());
    }
}
