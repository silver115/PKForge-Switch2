using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace PKForge.Switch2.Platform;

/// <summary>The console's save storage through the native host's <c>pkf_*</c> functions (switch2/pkfhost/pkf_host.h).</summary>
public sealed unsafe class NativeConsoleSaveHost : IConsoleSaveHost
{
    private const string Lib = "pkfhost";

    [StructLayout(LayoutKind.Sequential)]
    private struct PkfUid { public ulong Hi, Lo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PkfSaveInfo { public ulong TitleId; public PkfUid Uid; public long Size; }

    [DllImport(Lib)] private static extern long pkf_set_allowed_titles(ulong* ids, int count);
    [DllImport(Lib)] private static extern long pkf_list_users(PkfUid* output, int max);
    [DllImport(Lib)] private static extern long pkf_list_account_saves(PkfSaveInfo* output, int max);
    [DllImport(Lib)] private static extern long pkf_save_list_files(ulong titleId, PkfUid uid, byte* buffer, int length);
    [DllImport(Lib)] private static extern long pkf_save_read_file(ulong titleId, PkfUid uid, byte* path, byte* buffer, long length);
    [DllImport(Lib)] private static extern long pkf_set_writable_titles(ulong* ids, int count);
    [DllImport(Lib)] private static extern long pkf_save_open_write(ulong titleId, PkfUid uid, long* journalBytes);
    [DllImport(Lib)] private static extern long pkf_save_write_read(byte* path, byte* buffer, long length);
    [DllImport(Lib)] private static extern long pkf_save_write_file(byte* path, byte* buffer, long length);
    [DllImport(Lib)] private static extern long pkf_save_commit();
    [DllImport(Lib)] private static extern void pkf_save_close_write();

    // Sizes from the last listing of each save, so a read does not walk the save again.
    private readonly Dictionary<(ulong, ConsoleUser, string), long> _sizes = [];

    public NativeConsoleSaveHost()
    {
        var ids = ConsoleSaveCatalog.AllowedTitleIds.ToArray();
        fixed (ulong* p = ids) Check(pkf_set_allowed_titles(p, ids.Length), "set the title allow-list");
        var writable = ConsoleWritableTitles.Ids.ToArray();
        fixed (ulong* p = writable) Check(pkf_set_writable_titles(p, writable.Length), "set the writable titles");
    }

    public IReadOnlyList<ConsoleUser> ListUsers()
    {
        var buffer = stackalloc PkfUid[8];
        var count = Check(pkf_list_users(buffer, 8), "list the users");
        var users = new List<ConsoleUser>((int)count);
        for (var i = 0; i < count; i++) users.Add(new ConsoleUser(buffer[i].Hi, buffer[i].Lo));
        return users;
    }

    public IReadOnlyList<ConsoleSaveInfo> ListAccountSaves()
    {
        var buffer = new PkfSaveInfo[1024];
        long count;
        fixed (PkfSaveInfo* p = buffer) count = Check(pkf_list_account_saves(p, buffer.Length), "list the saves");
        return [.. buffer.Take((int)count).Select(s => new ConsoleSaveInfo(s.TitleId, new ConsoleUser(s.Uid.Hi, s.Uid.Lo), s.Size))];
    }

    public IReadOnlyList<ConsoleSaveFile> ListFiles(ulong titleId, ConsoleUser user)
    {
        var buffer = new byte[64 * 1024];
        long length;
        fixed (byte* p = buffer) length = Check(pkf_save_list_files(titleId, Uid(user), p, buffer.Length), "list the save's files");
        var files = ParseFileList(Encoding.UTF8.GetString(buffer, 0, (int)length));
        foreach (var file in files) _sizes[(titleId, user, file.Path)] = file.Size;
        return files;
    }

    public byte[] ReadFile(ulong titleId, ConsoleUser user, string path)
    {
        if (!_sizes.TryGetValue((titleId, user, path), out var size))
            size = ListFiles(titleId, user).FirstOrDefault(f => f.Path == path)?.Size ?? throw new FileNotFoundException(path);
        var bytes = new byte[size];
        var name = Encoding.UTF8.GetBytes(path + "\0");
        long read;
        fixed (byte* pName = name)
        fixed (byte* pBytes = bytes)
            read = Check(pkf_save_read_file(titleId, Uid(user), pName, pBytes, size), "read " + path);
        return read == size ? bytes : throw new IOException($"Read {read} of {size} bytes of {path}");
    }

    public IConsoleSaveWriter OpenForWrite(ulong titleId, ConsoleUser user)
    {
        long journal;
        Check(pkf_save_open_write(titleId, Uid(user), &journal), "open the save for writing");
        return new NativeWriter(this, titleId, user, journal);
    }

    sealed class NativeWriter(NativeConsoleSaveHost host, ulong titleId, ConsoleUser user, long journal) : IConsoleSaveWriter
    {
        bool _closed;

        public long JournalBytes => journal;

        // The size comes from the listing made when the save was read: the write mount is not listed again.
        public byte[] ReadFile(string path)
        {
            if (!host._sizes.TryGetValue((titleId, user, path), out var size))
                throw new FileNotFoundException(path);
            var bytes = new byte[size];
            var name = Encoding.UTF8.GetBytes(path + "\0");
            long read;
            fixed (byte* pName = name)
            fixed (byte* pBytes = bytes)
                read = Check(pkf_save_write_read(pName, pBytes, size), "read " + path);
            return read == size ? bytes : throw new IOException($"Read {read} of {size} bytes of {path}");
        }

        public void WriteFile(string path, ReadOnlySpan<byte> bytes)
        {
            var name = Encoding.UTF8.GetBytes(path + "\0");
            fixed (byte* pName = name)
            fixed (byte* pBytes = bytes)
                Check(pkf_save_write_file(pName, pBytes, bytes.Length), "write " + path);
        }

        public void Commit() => Check(pkf_save_commit(), "commit the save");

        public void Dispose()
        {
            if (_closed)
                return;
            _closed = true;
            pkf_save_close_write();
        }
    }

    internal static IReadOnlyList<ConsoleSaveFile> ParseFileList(string text)
    {
        var files = new List<ConsoleSaveFile>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t');
            if (tab < 0) throw new FormatException($"A file list line has no tab: \"{line}\"");
            files.Add(new ConsoleSaveFile(line[..tab], long.Parse(line.AsSpan(tab + 1), NumberStyles.None, CultureInfo.InvariantCulture)));
        }
        return files;
    }

    private static PkfUid Uid(ConsoleUser user) => new() { Hi = user.Hi, Lo = user.Lo };

    private static long Check(long result, string what) =>
        result >= 0 ? result : throw new IOException($"Could not {what}: {Describe(result)}");

    private static string Describe(long code) => code switch
    {
        -0x100000000L => "the title is not allowed",
        -0x100000001L => "the console's file system refused it",
        -0x100000002L => "the listing buffer is too small",
        _ => $"result 0x{-code:X}",
    };
}
