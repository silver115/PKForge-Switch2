namespace PKForge.MauiLite;

/// <summary>What a text field asks the software keyboard for: its text, its placeholder as the guide, and its limits.</summary>
public sealed record SoftKeyboardRequest(string Text, string? Guide, int MaxLength, bool Numeric, bool Multiline);

/// <summary>
/// The software keyboard a text field opens when it takes focus. The console sets it (swkbd); left null, as in the
/// headless host, focus opens nothing. It returns the new text, or null when the user cancelled.
/// </summary>
public static class SoftKeyboard
{
    public static Func<SoftKeyboardRequest, string?>? Show { get; set; }
}
