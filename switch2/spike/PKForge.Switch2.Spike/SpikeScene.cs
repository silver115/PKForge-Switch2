using PKForge.Chrome;
using SkiaSharp;

namespace PKForge.Switch2.Spike;

/// <summary>
/// The phase 0 scene: tools/ChromePreview's storage screen (RenderStorage and its status strip), drawn in its
/// 1280x675 coordinates at 1.5x on a 1920x1080 frame, plus a 40 px white cursor ring where the screen was last touched.
/// </summary>
public static class SpikeScene
{
    public const int Width = 1920, Height = 1080;
    private const float Scale = 1.5f;

    // The sprites RenderStorage shows, in the order of its filled slots.
    private static readonly string[] Sprites = ["b_645.png", "b_25.png", "b_149.png", "b_143.png", "b_9.png", "b_658.png", "b_445.png", "b_376.png"];
    private static readonly int[] FilledSlots = [1, 2, 7, 9, 14, 16, 21, 28];

    /// <summary>The PKSM chrome art from <paramref name="artDir"/>, the scene's sprites from <paramref name="spriteDir"/>
    /// (as mon_&lt;file&gt;), and the font.</summary>
    public static (PksmArt Art, SKTypeface Font) LoadAssets(string artDir, string spriteDir, string fontPath)
    {
        var art = new PksmArt();
        foreach (var file in Directory.GetFiles(artDir, "*.png"))
            art.Supply(Path.GetFileName(file), File.ReadAllBytes(file));
        foreach (var id in Sprites)
            art.Supply("mon_" + id, File.ReadAllBytes(Path.Combine(spriteDir, id)));
        using var fontData = SKData.CreateCopy(File.ReadAllBytes(fontPath));
        var font = SKTypeface.FromData(fontData) ?? throw new InvalidDataException("Not a font: " + fontPath);
        return (art, font);
    }

    public static void Draw(SKCanvas canvas, PksmArt art, SKTypeface font, SKPoint? cursor)
    {
        canvas.Clear(SKColors.Black);
        canvas.Save();
        canvas.Scale(Scale);
        DrawStorage(canvas, art, font);
        canvas.Restore();
        DrawCursor(canvas, cursor);
    }

    /// <summary>The 40 px white ring where the screen was last touched; nothing before the first touch.</summary>
    public static void DrawCursor(SKCanvas canvas, SKPoint? cursor)
    {
        if (cursor is not { } point) return;
        using var ring = new SKPaint { Color = SKColors.White, IsStroke = true, StrokeWidth = 4, IsAntialias = true };
        canvas.DrawCircle(point, 20, ring);
    }

    // tools/ChromePreview/Program.cs RenderStorage, unchanged but for disposing what it allocates.
    private static void DrawStorage(SKCanvas c, PksmArt art, SKTypeface typeface)
    {
        SKFont Font(float size) => new(typeface, size);

        PksmPaint.LogoGrid(c, new SKRect(0, 0, 1280, 675));
        StatusStrip(c, new SKRect(0, 0, 1280, 34), Font);

        // the box world sits on the dark chrome like a game screen
        var world = new SKRect(40, 60, 960, 600);
        var green = Pksm.BoxWallpapers[0];
        PksmPaint.Wallpaper(c, world, green);
        PksmPaint.FrameBrackets(c, world);

        using var spritePaint = new SKPaint();
        for (var i = 0; i < 30; i++)
        {
            var col = i % 6; var row = i / 6;
            var slot = new SKRect(world.Left + 28 + col * 146, world.Top + 40 + row * 82, world.Left + 28 + col * 146 + 128, world.Top + 40 + row * 82 + 72);
            var hasMon = Array.IndexOf(FilledSlots, i) >= 0;
            PksmPaint.Slot(c, slot, green, !hasMon);
            if (hasMon && art.Get("mon_" + Sprites[Array.IndexOf(FilledSlots, i)]) is { } b)
            {
                var scale = Math.Min(118f / b.Width, 66f / b.Height);
                var w = b.Width * scale; var h = b.Height * scale;
                c.DrawBitmap(b, new SKRect(slot.MidX - w / 2, slot.Bottom - h, slot.MidX + w / 2, slot.Bottom), spritePaint);
            }
            if (i == 9) PksmPaint.Selection(c, slot);
        }

        // dark box banner with white chevrons + red triangle
        using (var font = Font(22))
            PksmPaint.BoxNameBar(c, new SKRect(340, 66, 660, 104), "BOX 1", font, true, true);
        PksmPaint.Pointer(c, new SKPoint(310, 76), 16);

        // vertical stack in recessed blue
        string[] ops = ["VIEW", "CLEAR", "RELEASE", "TOOLS", "SAVE"];
        using (var font = Font(18))
            for (var i = 0; i < ops.Length; i++)
            {
                var b = new SKRect(world.Right - 72, 120 + i * 84, world.Right - 4, 180 + i * 84);
                PksmPaint.StackButton(c, b, i == 0);
                PksmPaint.CenterText(c, ops[i], b.MidX, b.MidY, font, SKColors.White, SKColors.Black.WithAlpha(0x50), SKTextAlign.Center);
            }

        // white info card on the right
        var info = new SKRect(990, 60, 1240, 600);
        PksmPaint.Panel(c, info);
        using (var font = Font(22))
            PksmPaint.HeaderStrip(c, new SKRect(info.Left + 10, info.Top + 10, info.Right - 10, info.Top + 54), "LANDORUS", font);
        string[] facts = ["#645", "Lv.55", "GROUND", "FLYING", "OT Bernardo", "ID 35053", "Adamant", "IV 31/29/31", "20/28/31"];
        using (var font = Font(24))
        {
            for (var i = 0; i < facts.Length; i++)
                PksmPaint.CenterText(c, facts[i], info.Left + 20, info.Top + 92 + i * 46, font, Pksm.Ink, SKColors.White.WithAlpha(0x60));
            PksmPaint.HintBar(c, new SKRect(0, 625, 1280, 675), [("LR", "Box"), ("A", "Select"), ("B", "Back"), ("X", "Tools")], font);
        }
    }

    // tools/ChromePreview/Program.cs StatusStrip.
    private static void StatusStrip(SKCanvas c, SKRect bar, Func<float, SKFont> font)
    {
        using var p = new SKPaint { Color = Pksm.Paper };
        c.DrawRect(bar, p);
        using var edge = new SKPaint { Color = Pksm.PaperEdge };
        c.DrawRect(new SKRect(bar.Left, bar.Bottom - 2, bar.Right, bar.Bottom), edge);
        using (var f = font(22))
            PksmPaint.CenterText(c, "PKForge", bar.Left + 24, bar.MidY, f, Pksm.Ink, SKColors.White.WithAlpha(0x60));
        using (var f = font(18))
            PksmPaint.CenterText(c, "OFFLINE", bar.Right - 130, bar.MidY, f, Pksm.InkSoft, SKColors.White.WithAlpha(0x50));
        using var dot = new SKPaint { Color = new SKColor(0x5F, 0xD0, 0x6A) };
        c.DrawRect(new SKRect(bar.Right - 80, bar.MidY - 6, bar.Right - 56, bar.MidY + 6), dot);
    }
}
