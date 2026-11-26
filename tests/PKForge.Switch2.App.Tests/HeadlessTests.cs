using System.Diagnostics;
using SkiaSharp;

namespace PKForge.Switch2.App.Tests;

// The headless host boots the real app on fake console storage. Each run is its own process: MauiLite's state is
// process-wide, as MAUI's is.
public sealed class HeadlessTests : IDisposable
{
    readonly string _work = Directory.CreateTempSubdirectory("pkf-headless-").FullName;

    public void Dispose() => Directory.Delete(_work, recursive: true);

    (int Exit, string Output) Run(string script, bool seed, params string[] extra)
    {
        var scriptFile = Path.Combine(_work, "script.txt");
        File.WriteAllText(scriptFile, script);
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { Path.Combine(AppContext.BaseDirectory, "PKForge.Switch2.Headless.dll"),
                     "--saves", Path.Combine(_work, "saves"), "--data", Path.Combine(_work, "data"), "--script", scriptFile, "--out", Path.Combine(_work, "shots") })
            start.ArgumentList.Add(argument);
        if (seed)
            start.ArgumentList.Add("--seed");
        foreach (var argument in extra)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            process.Kill(entireProcessTree: true);
            return (-1, "timed out");
        }
        return (process.ExitCode, output.Result + errors.Result);
    }

    // More than one colour over at least 1 % of the frame: something was drawn.
    static void AssertDrawn(string png)
    {
        using var bitmap = SKBitmap.Decode(png);
        var first = bitmap.GetPixel(0, 0);
        var different = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
            for (var x = 0; x < bitmap.Width; x += 4)
                if (bitmap.GetPixel(x, y) != first)
                    different++;
        Assert.True(different > bitmap.Width / 4 * (bitmap.Height / 4) / 100, $"only {different} sampled pixels differ from the corner");
    }

    [Fact]
    public void Headless_BootsToHome()
    {
        var (exit, output) = Run("wait 3000\npage HomePage\nshot home\n", seed: true);

        Assert.True(exit == 0, output);
        AssertDrawn(Path.Combine(_work, "shots", "home.png"));
    }

    // The screen scripts in tools/PKForge.Switch2.Headless/scripts, on the seeded saves: every step passes and app
    // code reports no error.
    [Theory]
    [InlineData("home.txt")]
    [InlineData("saves.txt")]
    [InlineData("box.txt")]
    [InlineData("keyboard.txt")]
    [InlineData("games.txt")]
    public void Headless_ScreenScript_Passes(string script)
    {
        var path = Path.Combine(AssetListTests.RepoRoot(), "tools", "PKForge.Switch2.Headless", "scripts", script);

        var (exit, output) = Run(File.ReadAllText(path), seed: true);

        Assert.True(exit == 0, output);
    }

    static Dictionary<string, string> Hashes(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
        .ToDictionary(file => Path.GetRelativePath(folder, file), file => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))));

    // Review Focus 3: saving an edit to the seeded Brilliant Diamond save, which stays read-only, shows the "not verified yet"
    // message, writes no byte, and the app goes on.
    // scripts/editor.txt opens the summary and the editor, flips Shiny, then saves; it checks the message is shown.
    [Fact]
    public void Headless_EditorSave_LeavesSaveUnchanged()
    {
        var (seeded, seedOutput) = Run("wait 1000\n", seed: true);
        Assert.True(seeded == 0, seedOutput);
        var saves = Path.Combine(_work, "saves");
        var before = Hashes(saves);
        var script = Path.Combine(AssetListTests.RepoRoot(), "tools", "PKForge.Switch2.Headless", "scripts", "editor.txt");

        var (exit, output) = Run(File.ReadAllText(script), seed: false);

        Assert.True(exit == 0, output);
        Assert.NotEmpty(before);
        Assert.Equal(before, Hashes(saves));
    }

    // Review Focus 3: restoring a restore point of the read-only Brilliant Diamond save shows the "not verified yet" message
    // and writes no byte.
    // The points are copies of the saves, so the save then changes (its money) for a restore to have something to
    // write; scripts/restore.txt opens Restore points from the box browser's Save data and restores the pinned point.
    [Fact]
    public void Headless_Restore_LeavesSaveUnchanged()
    {
        var (seeded, seedOutput) = Run("wait 4000\n", seed: true, "--seed-restore-points");
        Assert.True(seeded == 0, seedOutput);
        var saves = Path.Combine(_work, "saves");
        var diamond = Directory.GetFiles(saves, "SaveData.bin", SearchOption.AllDirectories).Single(file => file.Contains("0100000011D90000", StringComparison.Ordinal));
        var save = PKHeX.Core.SaveUtil.GetSaveFile(File.ReadAllBytes(diamond))!;
        save.Money = 1234;
        File.WriteAllBytes(diamond, save.Write().ToArray());
        var before = Hashes(saves);
        var script = Path.Combine(AssetListTests.RepoRoot(), "tools", "PKForge.Switch2.Headless", "scripts", "restore.txt");

        var (exit, output) = Run(File.ReadAllText(script), seed: false);

        Assert.True(exit == 0, output);
        Assert.NotEmpty(before);
        Assert.Equal(before, Hashes(saves));
    }

    [Fact]
    public void Headless_EmptyConsole_ShowsHome()
    {
        Directory.CreateDirectory(Path.Combine(_work, "saves"));

        var (exit, output) = Run("wait 3000\npage HomePage\nshot empty\n", seed: false);

        Assert.True(exit == 0, output);
        AssertDrawn(Path.Combine(_work, "shots", "empty.png"));
    }
}
