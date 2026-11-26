namespace PKForge.Switch2.Platform;

/// <summary>A console user account, as the two halves of its 128-bit AccountUid.</summary>
public readonly record struct ConsoleUser(ulong Hi, ulong Lo)
{
    public string Hex => $"{Hi:X16}{Lo:X16}";
}

public sealed record ConsoleSaveInfo(ulong TitleId, ConsoleUser User, long Size);

public sealed record ConsoleSaveFile(string Path, long Size);

/// <summary>The console's save storage: users, account saves, the files inside a save, and writing a writable title's save.</summary>
public interface IConsoleSaveHost
{
    IReadOnlyList<ConsoleUser> ListUsers();
    IReadOnlyList<ConsoleSaveInfo> ListAccountSaves();
    IReadOnlyList<ConsoleSaveFile> ListFiles(ulong titleId, ConsoleUser user);
    byte[] ReadFile(ulong titleId, ConsoleUser user, string path);

    /// <summary>Opens a writable title's save for writing; <see cref="ConsoleSaveFileAccess"/> runs the write sequence.</summary>
    IConsoleSaveWriter OpenForWrite(ulong titleId, ConsoleUser user);
}

/// <summary>One save opened for writing (parent design 7.6). Dispose closes it; nothing is kept that was not committed.</summary>
public interface IConsoleSaveWriter : IDisposable
{
    /// <summary>How much one commit can hold (the save's journal).</summary>
    long JournalBytes { get; }

    byte[] ReadFile(string path);

    /// <summary>Writes a file in place; it keeps its size.</summary>
    void WriteFile(string path, ReadOnlySpan<byte> bytes);

    void Commit();
}
