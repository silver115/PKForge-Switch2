using PKForge.Domain;

namespace PKForge.Switch2.Platform.Tests;

/// <summary>Console save storage in memory: saves keyed by (title, user), each a set of files.</summary>
internal sealed class FakeHost : IConsoleSaveHost
{
    private readonly List<ConsoleUser> _users = [];
    private readonly Dictionary<(ulong Title, ConsoleUser User), Dictionary<string, byte[]>> _saves = [];

    public List<(ulong Title, string Path)> Reads { get; } = [];

    /// <summary>Titles whose reads throw, as a save the console cannot mount.</summary>
    public HashSet<ulong> FailReads { get; } = [];

    public FakeHost Add(ulong title, ConsoleUser user, string file, byte[] bytes)
    {
        if (!_users.Contains(user)) _users.Add(user);
        if (!_saves.TryGetValue((title, user), out var files)) _saves[(title, user)] = files = [];
        files[file] = bytes;
        return this;
    }

    public IReadOnlyList<ConsoleUser> ListUsers() => _users;

    public IReadOnlyList<ConsoleSaveInfo> ListAccountSaves() =>
        [.. _saves.Select(s => new ConsoleSaveInfo(s.Key.Title, s.Key.User, s.Value.Values.Sum(b => (long)b.Length)))];

    public IReadOnlyList<ConsoleSaveFile> ListFiles(ulong titleId, ConsoleUser user) =>
        [.. _saves[(titleId, user)].Select(f => new ConsoleSaveFile(f.Key, f.Value.Length))];

    public byte[] ReadFile(ulong titleId, ConsoleUser user, string path)
    {
        Reads.Add((titleId, path));
        if (FailReads.Contains(titleId)) throw new IOException($"cannot mount {titleId:X16}");
        return _saves[(titleId, user)][path];
    }

    // The write side, with switches for the write sequence's refusals and failures.
    public long Journal { get; set; } = long.MaxValue;
    public bool FailCommit { get; set; }
    public bool CorruptReadBack { get; set; }
    public Action? BeforeWriteRead { get; set; }
    public List<string> Writes { get; } = [];
    public int Commits { get; set; }

    public IConsoleSaveWriter OpenForWrite(ulong titleId, ConsoleUser user) => new Writer(this, _saves[(titleId, user)]);

    sealed class Writer(FakeHost host, Dictionary<string, byte[]> files) : IConsoleSaveWriter
    {
        readonly Dictionary<string, byte[]> _pending = [];

        public long JournalBytes => host.Journal;

        public byte[] ReadFile(string path)
        {
            host.BeforeWriteRead?.Invoke();
            return files[path];
        }

        public void WriteFile(string path, ReadOnlySpan<byte> bytes)
        {
            host.Writes.Add(path);
            _pending[path] = bytes.ToArray();
        }

        public void Commit()
        {
            if (host.FailCommit) throw new IOException("the commit was refused");
            foreach (var (path, bytes) in _pending)
            {
                if (host.CorruptReadBack) bytes[0] ^= 0xFF;
                files[path] = bytes;
            }
            host.Commits++;
            _pending.Clear();
        }

        public void Dispose() => _pending.Clear();
    }
}

/// <summary>Treats bytes that start with "SAVE" as a Legends: Z-A save and rejects everything else.</summary>
internal sealed class FakeEngine : ISaveEngine
{
    public SaveDescription? TryDescribe(ReadOnlyMemory<byte> bytes, string? displayName = null) =>
        bytes.Span.StartsWith("SAVE"u8) ? new SaveDescription("Legends: Z-A", 9, "FORGE", "1:00") : null;

    public string? DescribeRejection(ReadOnlyMemory<byte> bytes) => "not a known save size";

    public SaveSnapshot Open(ReadOnlyMemory<byte> bytes, string? displayName = null) => throw new NotSupportedException();
    public ISaveEngineSession OpenSession(ReadOnlyMemory<byte> bytes, string? displayName = null) => throw new NotSupportedException();
    public ReadOnlyMemory<byte> Serialize(SaveSnapshot snapshot) => throw new NotSupportedException();
    public bool Validate(ReadOnlyMemory<byte> bytes) => throw new NotSupportedException();
    public BankEntryInfo? TryDescribeEntity(byte[] bytes, string sourceName, string? format = null) => throw new NotSupportedException();
    public ISaveEngineSession? OpenEntitySession(byte[] entityBytes, string? displayName = null, string? format = null) => throw new NotSupportedException();
    public ISaveEngineSession OpenBlankSession(int generation, string? displayName = null) => throw new NotSupportedException();
}
