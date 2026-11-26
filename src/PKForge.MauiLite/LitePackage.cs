namespace PKForge.MauiLite;

/// <summary>Opens a file from the app package (Android's assets; romfs on the Switch). The host sets the opener.</summary>
static class LitePackage
{
    public static Func<string, Stream>? Opener { get; set; }

    public static Stream Open(string name) =>
        Opener is { } open ? open(name) : throw new FileNotFoundException($"No app package is configured, so '{name}' cannot be opened.", name);
}
