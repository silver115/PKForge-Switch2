using System.Globalization;
using System.Security.Cryptography;
using PKForge.Infrastructure;

namespace PKForge.Switch2.Platform;

public sealed record BackedUpFile(string RelativePath, long Size, string Sha256);

public sealed record SaveBackupResult(string Folder, IReadOnlyList<BackedUpFile> Files, IReadOnlyList<string> Failures)
{
    public bool Passed => Failures.Count == 0;
}

/// <summary>
/// Copies console saves to the SD card, as they are, before anything writes them: a dated folder per run, each copy read
/// back and compared, and a manifest of SHA-256 hashes the PC checks (switch2/toolchain/pull-backups.py). Nothing here
/// writes a console save, and nothing deletes a backup.
/// </summary>
public static class ConsoleSaveBackup
{
    /// <summary>Every allow-listed save of every user.</summary>
    public static SaveBackupResult All(IConsoleSaveHost host, string root, DateTime utcNow) =>
        Copy(host, [.. host.ListAccountSaves().Where(s => ConsoleSaveCatalog.AllowedTitleIds.Contains(s.TitleId)).Select(s => (s.TitleId, s.User))],
            root, utcNow);

    /// <summary>One save, before the first write to it in a session.</summary>
    public static SaveBackupResult One(IConsoleSaveHost host, ulong titleId, ConsoleUser user, string root, DateTime utcNow) =>
        Copy(host, [(titleId, user)], root, utcNow);

    static SaveBackupResult Copy(IConsoleSaveHost host, IReadOnlyList<(ulong TitleId, ConsoleUser User)> saves, string root, DateTime utcNow)
    {
        var folder = Path.Combine(root, utcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        var files = new List<BackedUpFile>();
        var failures = new List<string>();
        foreach (var (titleId, user) in saves)
        {
            var game = GameName(titleId);
            // The SD card's file layer takes no ':' and the PC's neither ("Legends: Z-A").
            var saveFolder = $"{titleId:X16} {game.Replace(":", "", StringComparison.Ordinal)}/{user.Hex}";
            try
            {
                foreach (var file in host.ListFiles(titleId, user))
                {
                    var bytes = host.ReadFile(titleId, user, file.Path);
                    var relative = $"{saveFolder}/{file.Path}";
                    var target = Path.Combine(folder, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllBytes(target, bytes);
                    if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
                        failures.Add($"{game}: {file.Path} read back different from the console");
                    else
                        files.Add(new BackedUpFile(relative, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{game}: {error.Message}");
            }
        }
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "manifest.txt"), files.Select(f => $"{f.Sha256}  {f.Size}  {f.RelativePath}"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            failures.Add($"manifest: {error.Message}");
        }
        return new SaveBackupResult(folder, files, failures);
    }

    static string GameName(ulong titleId) =>
        EmulatorSaveHeuristics.SwitchTitles.TryGetValue(titleId.ToString("X16", CultureInfo.InvariantCulture), out var name) ? name : $"{titleId:X16}";
}
