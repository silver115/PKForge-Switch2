using System.Globalization;
using System.Text;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using SkiaSharp;

namespace PKForge.MauiLite.Rendering;

/// <summary>How a run of text looks. Sizes are in units.</summary>
readonly record struct TextStyle(string? Family, double FontSize, FontAttributes Attributes, SKColor Color, SKColor? Background,
    double CharacterSpacing, TextDecorations Decorations);

/// <summary>
/// Lines of text broken as Android's TextView breaks them for MAUI's line-break modes, measured in pixels.
/// Each run is split again wherever a character needs a fallback face.
/// </summary>
sealed class TextLayout
{
    public sealed record Piece(string Text, SKFont Font, TextStyle Style, float X, float Width);

    public sealed record Line(IReadOnlyList<Piece> Pieces, float Width, float Ascent, float Descent, float Top, float Height);

    const string Ellipsis = "…";

    public IReadOnlyList<Line> Lines { get; }
    public float Width { get; }
    public float Height { get; }

    TextLayout(IReadOnlyList<Line> lines)
    {
        Lines = lines;
        Width = lines.Count == 0 ? 0 : lines.Max(line => line.Width);
        Height = lines.Count == 0 ? 0 : lines[^1].Top + lines[^1].Height;
    }

    public static TextLayout Build(IReadOnlyList<(string Text, TextStyle Style)> runs, float maxWidth, LineBreakMode mode, int maxLines,
        double lineHeight, float scale)
    {
        var lineLimit = mode switch
        {
            LineBreakMode.NoWrap or LineBreakMode.HeadTruncation or LineBreakMode.MiddleTruncation => 1,
            LineBreakMode.TailTruncation => maxLines > 0 ? maxLines : 1,
            _ => maxLines > 0 ? maxLines : int.MaxValue,
        };
        var wraps = mode is LineBreakMode.WordWrap or LineBreakMode.CharacterWrap || (mode == LineBreakMode.TailTruncation && lineLimit > 1);
        var byCharacter = mode == LineBreakMode.CharacterWrap;
        var atoms = Atoms(runs, scale);
        var built = new List<List<Atom>>();
        var current = new List<Atom>();
        float x = 0;
        var fallbackStyle = runs.Count > 0 ? runs[0].Style : default;

        void Push()
        {
            while (current.Count > 0 && current[^1].IsSpace)
                current.RemoveAt(current.Count - 1);
            built.Add(current);
            current = [];
            x = 0;
        }

        for (var i = 0; i < atoms.Count; i++)
        {
            var atom = atoms[i];
            if (atom.IsNewline)
            {
                Push();
                continue;
            }
            if (atom.IsSpace)
            {
                if (current.Count > 0 || !wraps)
                {
                    current.Add(atom);
                    x += atom.Width;
                }
                continue;
            }
            // A word is the atom and every following atom with no break opportunity between them.
            var word = new List<Atom> { atom };
            while (!byCharacter && i + 1 < atoms.Count && atoms[i + 1].JoinsPrevious)
                word.Add(atoms[++i]);
            var wordWidth = word.Sum(a => a.Width);
            if (wraps && x + wordWidth > maxWidth && current.Any(a => !a.IsSpace))
                Push();
            if (wraps && wordWidth > maxWidth)
            {
                // A word wider than the line breaks between characters, as Android does.
                foreach (var part in word.SelectMany(a => a.Characters()))
                {
                    if (x + part.Width > maxWidth && current.Count > 0)
                        Push();
                    current.Add(part);
                    x += part.Width;
                }
                continue;
            }
            current.AddRange(word);
            x += wordWidth;
        }
        if (current.Count > 0 || built.Count == 0)
            Push();

        var truncated = built.Count > lineLimit;
        if (truncated)
            built.RemoveRange(lineLimit, built.Count - lineLimit);
        var lines = new List<Line>();
        float top = 0;
        for (var n = 0; n < built.Count; n++)
        {
            var atomsOnLine = built[n];
            var last = n == built.Count - 1;
            var ellipsize = mode switch
            {
                LineBreakMode.TailTruncation => last && (truncated || LineWidth(atomsOnLine) > maxWidth),
                LineBreakMode.HeadTruncation or LineBreakMode.MiddleTruncation => LineWidth(atomsOnLine) > maxWidth,
                _ => false,
            };
            var pieces = ellipsize ? Ellipsized(atomsOnLine, maxWidth, mode, scale) : Merge(atomsOnLine);
            lines.Add(MakeLine(pieces, top, lineHeight, fallbackStyle, scale));
            top += lines[^1].Height;
        }
        return new TextLayout(lines);
    }

    static float LineWidth(List<Atom> atoms) => atoms.Sum(a => a.Width);

    static Line MakeLine(List<Piece> pieces, float top, double lineHeight, TextStyle fallbackStyle, float scale)
    {
        float ascent = 0, descent = 0;
        if (pieces.Count == 0)
        {
            using var font = MakeFont(FontRegistry.Face(fallbackStyle.Family), fallbackStyle, scale);
            var metrics = font.Metrics;
            (ascent, descent) = (metrics.Ascent, metrics.Descent);
        }
        foreach (var piece in pieces)
        {
            var metrics = piece.Font.Metrics;
            ascent = Math.Min(ascent, metrics.Ascent);
            descent = Math.Max(descent, metrics.Descent);
        }
        var height = descent - ascent;
        if (lineHeight > 0)
            height *= (float)lineHeight;
        var width = pieces.Count == 0 ? 0 : pieces[^1].X + pieces[^1].Width;
        return new Line(pieces, width, ascent, descent, top, height);
    }

    static List<Piece> Merge(List<Atom> atoms)
    {
        var pieces = new List<Piece>();
        float x = 0;
        var text = new StringBuilder();
        Atom? first = null;
        float width = 0;
        foreach (var atom in atoms)
        {
            if (first is not null && (!ReferenceEquals(first.Font, atom.Font) || first.Style != atom.Style))
            {
                pieces.Add(new Piece(text.ToString(), first.Font, first.Style, x, width));
                x += width;
                text.Clear();
                width = 0;
            }
            if (text.Length == 0)
                first = atom;
            text.Append(atom.Text);
            width += atom.Width;
        }
        if (first is not null && text.Length > 0)
            pieces.Add(new Piece(text.ToString(), first.Font, first.Style, x, width));
        return pieces;
    }

    static List<Piece> Ellipsized(List<Atom> atoms, float maxWidth, LineBreakMode mode, float scale)
    {
        var characters = atoms.SelectMany(a => a.Characters()).ToList();
        var style = characters.Count > 0 ? characters[0].Style : default;
        var font = characters.Count > 0 ? characters[0].Font : MakeFont(SKTypeface.Default, style, scale);
        var dots = new Atom(Ellipsis, font, style, Measure(Ellipsis, font, style), false, false, false);
        var room = maxWidth - dots.Width;
        List<Atom> kept;
        switch (mode)
        {
            case LineBreakMode.HeadTruncation:
                kept = TakeWhileFits(Enumerable.Reverse(characters), room);
                kept.Reverse();
                kept.Insert(0, dots);
                break;
            case LineBreakMode.MiddleTruncation:
                var head = TakeWhileFits(characters, room / 2);
                var tail = TakeWhileFits(Enumerable.Reverse(characters), room - LineWidth(head));
                tail.Reverse();
                kept = [.. head, dots, .. tail];
                break;
            default:
                kept = TakeWhileFits(characters, room);
                while (kept.Count > 0 && kept[^1].IsSpace)
                    kept.RemoveAt(kept.Count - 1);
                kept.Add(dots);
                break;
        }
        return Merge(kept);
    }

    static List<Atom> TakeWhileFits(IEnumerable<Atom> characters, float room)
    {
        var kept = new List<Atom>();
        float width = 0;
        foreach (var c in characters)
        {
            if (width + c.Width > room)
                break;
            kept.Add(c);
            width += c.Width;
        }
        return kept;
    }

    sealed record Atom(string Text, SKFont Font, TextStyle Style, float Width, bool IsSpace, bool IsNewline, bool JoinsPrevious)
    {
        public IEnumerable<Atom> Characters()
        {
            if (Text.Length <= 1)
            {
                yield return this;
                yield break;
            }
            var e = StringInfo.GetTextElementEnumerator(Text);
            while (e.MoveNext())
            {
                var element = e.GetTextElement();
                yield return this with { Text = element, Width = Measure(element, Font, Style) };
            }
        }
    }

    /// <summary>Splits runs into words, spaces and newlines, each in one face.</summary>
    static List<Atom> Atoms(IReadOnlyList<(string Text, TextStyle Style)> runs, float scale)
    {
        var atoms = new List<Atom>();
        var lastWasWord = false;
        foreach (var (text, style) in runs)
        {
            if (string.IsNullOrEmpty(text))
                continue;
            var fonts = new Dictionary<SKTypeface, SKFont>();
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\n')
                {
                    atoms.Add(new Atom("\n", Font(SKTypeface.Default), style, 0, false, true, false));
                    i++;
                    lastWasWord = false;
                    continue;
                }
                if (c == '\r')
                {
                    i++;
                    continue;
                }
                if (c == ' ' || c == '\t')
                {
                    var font = Font(FontRegistry.FaceFor(style.Family, style.Attributes, ' '));
                    atoms.Add(new Atom(" ", font, style, Measure(" ", font, style), true, false, false));
                    i++;
                    lastWasWord = false;
                    continue;
                }
                // A word: up to the next space or newline, cut wherever the face changes.
                var start = i;
                var face = FontRegistry.FaceFor(style.Family, style.Attributes, char.ConvertToUtf32(text, i));
                while (i < text.Length && text[i] is not (' ' or '\t' or '\n' or '\r'))
                {
                    var codepoint = char.ConvertToUtf32(text, i);
                    if (FontRegistry.FaceFor(style.Family, style.Attributes, codepoint) != face)
                        break;
                    i += char.IsSurrogatePair(text, i) ? 2 : 1;
                }
                var part = text[start..i];
                var partFont = Font(face);
                atoms.Add(new Atom(part, partFont, style, Measure(part, partFont, style), false, false, lastWasWord));
                lastWasWord = true;
            }

            SKFont Font(SKTypeface face)
            {
                if (!fonts.TryGetValue(face, out var font))
                    fonts[face] = font = MakeFont(face, style, scale);
                return font;
            }
        }
        return atoms;
    }

    public static SKFont MakeFont(SKTypeface face, TextStyle style, float scale) => new(face, (float)style.FontSize * scale)
    {
        // MAUI on Android fakes bold and italic for a face that has neither.
        Embolden = style.Attributes.HasFlag(FontAttributes.Bold) && !face.IsBold,
        SkewX = style.Attributes.HasFlag(FontAttributes.Italic) && !face.IsItalic ? -0.25f : 0,
        Subpixel = true,
        Edging = SKFontEdging.Antialias,
    };

    /// <summary>Width in pixels, with MAUI's character spacing (1/16 em per unit, as on Android).</summary>
    static float Measure(string text, SKFont font, TextStyle style)
    {
        var width = font.MeasureText(text);
        if (style.CharacterSpacing != 0)
            width += LetterSpacing(font, style) * new StringInfo(text).LengthInTextElements;
        return width;
    }

    public static float LetterSpacing(SKFont font, TextStyle style) => (float)style.CharacterSpacing * 0.0624f * font.Size;
}
