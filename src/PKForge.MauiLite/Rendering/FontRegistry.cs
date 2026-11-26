using Microsoft.Maui.Controls;
using SkiaSharp;

namespace PKForge.MauiLite;

/// <summary>
/// The app's fonts by alias, as MAUI's <c>ConfigureFonts</c> registers them. A character the requested face lacks
/// comes from "Rounded" (M PLUS Rounded, which covers Japanese and most symbols), then from Skia's default.
/// </summary>
public static class FontRegistry
{
    const string FallbackAlias = "Rounded";

    static readonly Dictionary<string, (string File, Func<string, Stream> Open)> Sources = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, SKTypeface> Loaded = new(StringComparer.OrdinalIgnoreCase);
    static readonly Lock Gate = new();

    public static void Register(string file, string alias, Func<string, Stream> open)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(open);
        lock (Gate)
        {
            // MAUI resolves a font by its alias or by its file name, with or without the extension.
            foreach (var name in new[] { alias, file, Path.GetFileNameWithoutExtension(file) })
                if (!string.IsNullOrEmpty(name))
                {
                    Sources[name] = (file, open);
                    Loaded.Remove(name);
                }
        }
    }

    /// <summary>The face for <paramref name="family"/>, or Skia's default for an unknown or empty family.</summary>
    public static SKTypeface Face(string? family)
    {
        if (string.IsNullOrEmpty(family))
            return SKTypeface.Default;
        lock (Gate)
        {
            if (Loaded.TryGetValue(family, out var face))
                return face;
            if (!Sources.TryGetValue(family, out var source))
                return SKTypeface.Default;
            // One typeface per file, shared by every name the file goes by.
            face = Sources.Where(entry => entry.Value.File == source.File).Select(entry => Loaded.GetValueOrDefault(entry.Key)).FirstOrDefault(f => f is not null)
                ?? Load(source.File, source.Open);
            Loaded[family] = face;
            return face;
        }
    }

    /// <summary>The face that draws <paramref name="codepoint"/> in <paramref name="family"/>, with the fallback chain.</summary>
    public static SKTypeface FaceFor(string? family, FontAttributes attributes, int codepoint)
    {
        var face = Face(family);
        if (face.ContainsGlyph(codepoint))
            return face;
        var fallback = Face(FallbackAlias);
        if (fallback.ContainsGlyph(codepoint))
            return fallback;
        return SKFontManager.Default.MatchCharacter(codepoint) ?? face;
    }

    static SKTypeface Load(string file, Func<string, Stream> open)
    {
        using var stream = open(file);
        using var data = SKData.Create(stream);
        return SKTypeface.FromData(data) ?? SKTypeface.Default;
    }
}
