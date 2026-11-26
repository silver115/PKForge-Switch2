namespace PKForge.Switch2.Platform;

/// <summary>
/// Writes "key=value" lines to results-&lt;yyyyMMdd-HHmmss&gt;.txt, each flushed at once, so a crash keeps every
/// line written before it. When the file cannot be created, the lines go to <paramref name="fallback"/> instead.
/// </summary>
public sealed class ResultsWriter : IDisposable
{
    private readonly StreamWriter? _file;
    private readonly TextWriter _fallback;

    public ResultsWriter(string directory, TextWriter fallback)
    {
        _fallback = fallback;
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"results-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            _file = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
            FilePath = path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _fallback.WriteLine($"results file unavailable: {error.Message}");
        }
    }

    /// <summary>The results file, or null after falling back.</summary>
    public string? FilePath { get; }

    public void Write(string key, string value)
    {
        var line = $"{key}={value}\n";
        var target = (TextWriter?)_file ?? _fallback;
        target.Write(line);
        target.Flush();
    }

    public void Dispose() => _file?.Dispose();
}
