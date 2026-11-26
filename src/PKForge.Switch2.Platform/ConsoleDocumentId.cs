using System.Globalization;

namespace PKForge.Switch2.Platform;

/// <summary>Document ids of console saves: <c>console:&lt;title, 16 hex&gt;:&lt;user, 32 hex&gt;/&lt;file&gt;</c>.</summary>
public static class ConsoleDocumentId
{
    private const string Prefix = "console:";
    private const int Body = 16 + 1 + 32 + 1;  // title, ':', user, '/'

    public static string Format(ulong titleId, ConsoleUser user, string file) => $"{Prefix}{titleId:X16}:{user.Hex}/{file}";

    public static bool TryParse(string id, out ulong titleId, out ConsoleUser user, out string file)
    {
        titleId = 0;
        user = default;
        file = "";
        if (!id.StartsWith(Prefix, StringComparison.Ordinal) || id.Length <= Prefix.Length + Body) return false;
        var rest = id.AsSpan(Prefix.Length);
        if (rest[16] != ':' || rest[49] != '/') return false;
        if (!Hex(rest[..16], out titleId) || !Hex(rest.Slice(17, 16), out var hi) || !Hex(rest.Slice(33, 16), out var lo)) return false;
        user = new ConsoleUser(hi, lo);
        file = rest[Body..].ToString();
        return true;
    }

    private static bool Hex(ReadOnlySpan<char> text, out ulong value) =>
        ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
}
