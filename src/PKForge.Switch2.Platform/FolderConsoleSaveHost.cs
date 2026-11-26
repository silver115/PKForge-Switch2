using System.Globalization;

namespace PKForge.Switch2.Platform;

/// <summary>
/// Console save storage laid out in a folder, for the headless host and tests: <c>&lt;root&gt;/&lt;titleId, 16 hex&gt;/&lt;uid, 32 hex&gt;/&lt;file&gt;</c>.
/// Users are the uid folders found, sorted.
/// </summary>
public sealed class FolderConsoleSaveHost(string root) : IConsoleSaveHost
{
    public IReadOnlyList<ConsoleUser> ListUsers() =>
        [.. ListAccountSaves().Select(save => save.User).Distinct().OrderBy(user => user.Hex, StringComparer.Ordinal)];

    public IReadOnlyList<ConsoleSaveInfo> ListAccountSaves()
    {
        if (!Directory.Exists(root))
            return [];
        var saves = new List<ConsoleSaveInfo>();
        foreach (var titleFolder in Directory.EnumerateDirectories(root))
        {
            if (!ulong.TryParse(Path.GetFileName(titleFolder), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var title))
                continue;
            foreach (var userFolder in Directory.EnumerateDirectories(titleFolder))
                if (ParseUser(Path.GetFileName(userFolder)) is { } user)
                    saves.Add(new ConsoleSaveInfo(title, user, Files(userFolder).Sum(file => new FileInfo(file).Length)));
        }
        return saves;
    }

    public IReadOnlyList<ConsoleSaveFile> ListFiles(ulong titleId, ConsoleUser user)
    {
        var folder = SaveFolder(titleId, user);
        return [.. Files(folder).Select(file => new ConsoleSaveFile(Path.GetRelativePath(folder, file).Replace('\\', '/'), new FileInfo(file).Length))];
    }

    public byte[] ReadFile(ulong titleId, ConsoleUser user, string path) => File.ReadAllBytes(FileIn(SaveFolder(titleId, user), path));

    /// <summary>Writes are held until <see cref="IConsoleSaveWriter.Commit"/>, as the console's journaled save holds them.</summary>
    public IConsoleSaveWriter OpenForWrite(ulong titleId, ConsoleUser user) => new FolderWriter(SaveFolder(titleId, user));

    sealed class FolderWriter(string folder) : IConsoleSaveWriter
    {
        readonly Dictionary<string, byte[]> _pending = [];

        public long JournalBytes => long.MaxValue;

        public byte[] ReadFile(string path) => File.ReadAllBytes(FileIn(folder, path));

        public void WriteFile(string path, ReadOnlySpan<byte> bytes) => _pending[FileIn(folder, path)] = bytes.ToArray();

        public void Commit()
        {
            foreach (var (file, bytes) in _pending)
            {
                File.WriteAllBytes(file + ".commit", bytes);
                File.Move(file + ".commit", file, overwrite: true);
            }
            _pending.Clear();
        }

        public void Dispose() => _pending.Clear();
    }

    // A path may only name a file inside this one save, as on the console.
    static string FileIn(string saveFolder, string path)
    {
        var folder = Path.GetFullPath(saveFolder);
        var file = Path.GetFullPath(Path.Combine(folder, path));
        if (Path.IsPathRooted(path) || !file.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException($"'{path}' is not a file inside the save.", nameof(path));
        return file;
    }

    string SaveFolder(ulong titleId, ConsoleUser user) => Path.Combine(root, titleId.ToString("X16", CultureInfo.InvariantCulture), user.Hex);

    static IEnumerable<string> Files(string folder) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories) : [];

    static ConsoleUser? ParseUser(string name) =>
        name.Length == 32
        && ulong.TryParse(name[..16], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hi)
        && ulong.TryParse(name[16..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var lo)
            ? new ConsoleUser(hi, lo)
            : null;
}

/// <summary>
/// One call at a time: the console host mounts every save under one fixed name, so reads must not overlap, and an open
/// writer holds the gate until it is disposed. A semaphore, not a lock: a write sequence may await between its steps.
/// </summary>
public sealed class SerializedConsoleSaveHost(IConsoleSaveHost inner) : IConsoleSaveHost
{
    readonly SemaphoreSlim _gate = new(1, 1);

    T One<T>(Func<T> call)
    {
        _gate.Wait();
        try
        {
            return call();
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<ConsoleUser> ListUsers() => One(inner.ListUsers);

    public IReadOnlyList<ConsoleSaveInfo> ListAccountSaves() => One(inner.ListAccountSaves);

    public IReadOnlyList<ConsoleSaveFile> ListFiles(ulong titleId, ConsoleUser user) => One(() => inner.ListFiles(titleId, user));

    public byte[] ReadFile(ulong titleId, ConsoleUser user, string path) => One(() => inner.ReadFile(titleId, user, path));

    public IConsoleSaveWriter OpenForWrite(ulong titleId, ConsoleUser user)
    {
        _gate.Wait();
        try
        {
            return new GatedWriter(inner.OpenForWrite(titleId, user), _gate);
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    sealed class GatedWriter(IConsoleSaveWriter inner, SemaphoreSlim gate) : IConsoleSaveWriter
    {
        bool _disposed;

        public long JournalBytes => inner.JournalBytes;
        public byte[] ReadFile(string path) => inner.ReadFile(path);
        public void WriteFile(string path, ReadOnlySpan<byte> bytes) => inner.WriteFile(path, bytes);
        public void Commit() => inner.Commit();

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            inner.Dispose();
            gate.Release();
        }
    }
}
