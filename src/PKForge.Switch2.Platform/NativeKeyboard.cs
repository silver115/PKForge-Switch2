using System.Runtime.InteropServices;
using System.Text;
using PKForge.MauiLite;

namespace PKForge.Switch2.Platform;

/// <summary>The console's software keyboard (swkbd) for MauiLite's text fields, through switch2/pkfhost/pkf_keyboard.c.</summary>
public static unsafe class NativeKeyboard
{
    private const string Lib = "pkfhost";

    // The longest text asked for; UTF-8 takes up to 4 bytes a character.
    private const int MaxCharacters = 500;

    [DllImport(Lib)]
    private static extern long pkf_keyboard_show(byte* text, byte* guide, int maxLength, int numeric, int multiline, byte* output, int outputLength);

    /// <summary>Shows the keyboard and returns the text entered, or null when it was cancelled or could not open.</summary>
    public static string? Show(SoftKeyboardRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = Encoding.UTF8.GetBytes(request.Text + "\0");
        var guide = request.Guide is null ? null : Encoding.UTF8.GetBytes(request.Guide + "\0");
        var output = new byte[MaxCharacters * 4 + 1];
        long result;
        fixed (byte* pText = text)
        fixed (byte* pGuide = guide)
        fixed (byte* pOutput = output)
            result = pkf_keyboard_show(pText, pGuide, Math.Min(request.MaxLength, MaxCharacters), request.Numeric ? 1 : 0,
                request.Multiline ? 1 : 0, pOutput, output.Length);
        if (result == 0)
        {
            var end = Array.IndexOf(output, (byte)0);
            return Encoding.UTF8.GetString(output, 0, end < 0 ? output.Length : end);
        }
        if (result < 0)
            UiErrors.Report(new IOException($"The software keyboard did not open: result 0x{-result:X}"), "keyboard");
        return null;
    }
}
