using PKForge.Chrome;
using PKForge.Switch2.Spike;
using SkiaSharp;
using Xunit;

namespace PKForge.Switch2.Platform.Tests;

public class SpikeSceneTests
{
    [Fact]
    public void Draw_IsDeterministic()
    {
        var (art, font) = LoadRepoAssets();
        var first = Render(art, font, new SKPoint(960, 540));
        var second = Render(art, font, new SKPoint(960, 540));
        Assert.True(first.AsSpan().SequenceEqual(second), "two draws of the same scene differ");
    }

    // Fixtures/frame-console.png is the spike's first frame as the Switch 2 drew it (Task 7, 2026-11-20).
    [Fact]
    public void Draw_MatchesConsoleFrame()
    {
        var (art, font) = LoadRepoAssets();
        var pc = Render(art, font, cursor: null);
        using var console = SKBitmap.Decode(Path.Combine(AppContext.BaseDirectory, "Fixtures", "frame-console.png"));
        Assert.Equal((SpikeScene.Width, SpikeScene.Height), (console.Width, console.Height));
        using var consolePixels = console.Copy(SKColorType.Rgba8888);
        var other = consolePixels.GetPixelSpan();
        var differing = 0;
        for (var i = 0; i < pc.Length; i += 4)
            for (var c = 0; c < 4; c++)
                if (Math.Abs(pc[i + c] - other[i + c]) > 8)
                {
                    differing++;
                    break;
                }
        var share = differing / (double)(SpikeScene.Width * SpikeScene.Height);
        Assert.True(share <= 0.01, $"{share:P2} of pixels differ by more than 8 in a channel");
    }

    // The files tools/ChromePreview uses.
    internal static (PksmArt Art, SKTypeface Font) LoadRepoAssets()
    {
        var root = RepoRoot();
        return SpikeScene.LoadAssets(
            Path.Combine(root, "src/PKForge.App/Resources/UI/pksm"),
            Path.Combine(root, "external/PKHeX/PKHeX.Drawing.PokeSprite/Resources/img/Big Pokemon Sprites"),
            Path.Combine(root, "src/PKForge.App/Resources/Fonts/NDS12.ttf"));
    }

    internal static byte[] Render(PksmArt art, SKTypeface font, SKPoint? cursor)
    {
        var info = new SKImageInfo(SpikeScene.Width, SpikeScene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        SpikeScene.Draw(surface.Canvas, art, font, cursor);
        var pixels = new byte[info.BytesSize];
        using var pixmap = surface.PeekPixels();
        pixmap.GetPixelSpan().CopyTo(pixels);
        return pixels;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PKForge.sln")))
                return dir.FullName;
        throw new DirectoryNotFoundException("PKForge.sln not found above " + AppContext.BaseDirectory);
    }
}
