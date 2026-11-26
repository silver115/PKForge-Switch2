using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using PKForge.Domain;
using PKForge.Infrastructure;

namespace PKForge.Switch2.Platform;

/// <summary>
/// Decides whether a console write may go ahead; null allows it, otherwise the message the user sees (Task 4's legality
/// guard). <paramref name="current"/> is the save on the console now, <paramref name="candidate"/> what would be written.
/// </summary>
public interface IConsoleWriteGuard
{
    ValueTask<string?> RefusalAsync(string documentId, ReadOnlyMemory<byte> current, ReadOnlyMemory<byte> candidate,
        CancellationToken cancellationToken);
}

/// <summary>
/// PKForge's save file access for <c>console:</c> documents. Reads any allow-listed save; writes only a writable title's
/// (<see cref="ConsoleWritableTitles"/>), through the sequence of parent design 7.6, after PKForge's SafeSaveWriter has
/// validated the candidate and made its restore point. Before the first write to a save in a session, the whole save is
/// copied to <c>backupRoot</c> and kept for good.
/// </summary>
public sealed class ConsoleSaveFileAccess(IConsoleSaveHost host, string backupRoot, Func<DateTime> utcNow, IConsoleWriteGuard? guard = null)
    : ISaveFileAccess
{
    readonly ConcurrentDictionary<string, (byte[] Sha256, int Length)> _read = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, bool> _copied = new(StringComparer.Ordinal);

    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var (titleId, user, file) = Parse(documentId);
        // Off the UI thread: reading a save file takes a few hundred milliseconds on the console.
        return new ValueTask<ReadOnlyMemory<byte>>(Task.Run(() =>
        {
            var bytes = host.ReadFile(titleId, user, file);
            _read[documentId] = (SHA256.HashData(bytes), bytes.Length);
            return (ReadOnlyMemory<byte>)bytes;
        }, cancellationToken));
    }

    public ValueTask WriteAtomicallyAsync(string documentId, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        var (titleId, user, file) = Parse(documentId);
        return new ValueTask(Task.Run(() => WriteAsync(documentId, titleId, user, file, bytes, cancellationToken), cancellationToken));
    }

    async Task WriteAsync(string documentId, ulong titleId, ConsoleUser user, string file, ReadOnlyMemory<byte> candidate,
        CancellationToken cancellationToken)
    {
        if (!ConsoleWritableTitles.Ids.Contains(titleId))
            throw new InvalidOperationException($"Writing {GameName(titleId)} saves on Switch 2 is not verified yet, so PKForge only reads them.");
        if (!_read.TryGetValue(documentId, out var opened))
            throw new InvalidOperationException("PKForge has not read this save since it started. Open it again before saving.");
        if (candidate.Length != opened.Length)
            throw new InvalidOperationException("The new save is a different size from the one on the console, so it was not written.");
        // Before the writer opens: the copy reads through the host, which an open writer holds.
        if (!_copied.ContainsKey(documentId))
        {
            if (!ConsoleSaveBackup.One(host, titleId, user, backupRoot, utcNow()).Passed)
                throw new InvalidOperationException("PKForge could not copy the original save to the SD card first, so nothing was written.");
            _copied[documentId] = true;
        }
        using (var writer = host.OpenForWrite(titleId, user))
        {
            var current = writer.ReadFile(file);
            if (!SHA256.HashData(current).AsSpan().SequenceEqual(opened.Sha256))
                throw new InvalidOperationException("The save changed on the console since PKForge opened it. Open it again before saving.");
            if (candidate.Length > writer.JournalBytes)
                throw new InvalidOperationException("The console save has no room for this write, so nothing was written.");
            if (guard is not null && await guard.RefusalAsync(documentId, current, candidate, cancellationToken) is { } refusal)
                throw new InvalidOperationException(refusal);
            try
            {
                writer.WriteFile(file, candidate.Span);
                writer.Commit();
            }
            catch (IOException error)
            {
                throw new IOException("The console did not commit the save, so nothing was written.", error);
            }
        }
        var back = host.ReadFile(titleId, user, file);
        if (!back.AsSpan().SequenceEqual(candidate.Span))
            throw new IOException("The save read back from the console differs from what was written. Restore the restore point made before this save.");
        _read[documentId] = (SHA256.HashData(back), back.Length);
    }

    static (ulong TitleId, ConsoleUser User, string File) Parse(string documentId) =>
        ConsoleDocumentId.TryParse(documentId, out var titleId, out var user, out var file)
            ? (titleId, user, file)
            : throw new ArgumentException($"'{documentId}' is not a console save.", nameof(documentId));

    static string GameName(ulong titleId) =>
        EmulatorSaveHeuristics.SwitchTitles.TryGetValue(titleId.ToString("X16", CultureInfo.InvariantCulture), out var name) ? name : $"{titleId:X16}";
}

/// <summary>
/// The console's Pokémon saves as PKForge's save shelf sees them, in place of an emulator folder scan: every allow-listed
/// save of every user. The game's own <c>backup</c> copy starts under Hidden saves, as any copy does.
/// </summary>
public sealed class ConsoleSaveScanner(IConsoleSaveHost host, ISaveEngine engine) : IEmulatorDetectionService
{
    public ValueTask<EmulatorScanResult> ScanAsync(string treeId, EmulatorKind kind, CancellationToken cancellationToken = default)
    {
        if (treeId != ConsoleWatchedRootStore.Root.TreeId)
            return ValueTask.FromResult(new EmulatorScanResult([], 0, []));
        return new ValueTask<EmulatorScanResult>(Task.Run(Scan, cancellationToken));
    }

    EmulatorScanResult Scan()
    {
        var catalog = new ConsoleSaveCatalog(host, engine);
        var saves = new List<DetectedSave>();
        var rejected = new List<string>();
        var users = host.ListUsers();
        for (var i = 0; i < users.Count; i++)
        {
            var scan = catalog.Scan(users[i]);
            foreach (var entry in scan.Saves)
            {
                var d = entry.Description;
                // shortcut: users show as "User 1", "User 2"; their names and icons come with user choice in phase 2.
                saves.Add(new DetectedSave(entry.DocumentId, entry.File, entry.GameName, EmulatorKind.Console, RequiresExtraCare: false, LastModified: null,
                    Generation: d.Generation, TrainerName: d.TrainerName, PlayTime: d.PlayTime, FolderHint: $"User {i + 1}",
                    Language: d.Language, IsCopy: entry.File == "backup", TrainerId: d.TrainerId));
            }
            rejected.AddRange(scan.Rejections.Select(r => $"{r.DocumentId}: {r.Reason}"));
        }
        return new EmulatorScanResult(saves, saves.Count + rejected.Count, rejected);
    }
}

/// <summary>The one save source on the console: its own storage. It is always there, so nothing is added or removed.</summary>
public sealed class ConsoleWatchedRootStore : IWatchedRootStore
{
    public static WatchedRoot Root { get; } = new(EmulatorKind.Console, "console:", "Switch 2");

    public IReadOnlyList<WatchedRoot> GetRoots() => [Root];

    public void AddRoot(WatchedRoot root)
    {
    }

    public void RemoveRoot(WatchedRoot root)
    {
    }
}

/// <summary>The console's implementations of PKForge's platform services, registered after the app's own so they win.</summary>
public static class ConsoleServices
{
    /// <param name="backupRoot">Where the original of each written save is kept (sdmc:/switch/PKForge/save-backups).</param>
    /// <param name="guard">The write guard, from the app's services; null for none.</param>
    public static void Register(IServiceCollection services, IConsoleSaveHost host, string backupRoot,
        Func<IServiceProvider, IConsoleWriteGuard?> guard)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(guard);
        var saves = host as SerializedConsoleSaveHost ?? new SerializedConsoleSaveHost(host);
        services.AddSingleton<IConsoleSaveHost>(saves);
        services.AddSingleton<ISaveFileAccess>(provider => new ConsoleSaveFileAccess(saves, backupRoot, () => DateTime.UtcNow, guard(provider)));
        services.AddSingleton<IEmulatorDetectionService>(provider => new ConsoleSaveScanner(saves, provider.GetRequiredService<ISaveEngine>()));
        services.AddSingleton<IWatchedRootStore, ConsoleWatchedRootStore>();
        services.AddSingleton<IDocumentPicker, UnavailableDocumentPicker>();
        services.AddSingleton<IFolderPicker, UnavailableFolderPicker>();
        services.AddSingleton<IFolderFileAccess, UnavailableFolderFileAccess>();
        services.AddSingleton<ISecondaryDisplayHost, NoSecondaryDisplay>();
        services.AddSingleton<IMusicPlayer, SilentMusicPlayer>();
    }
}
