using System.Xml.Linq;

namespace PKForge.Switch2.App.Tests;

// The Switch 2 build ships the same package files as the Android app, at the same names.
public class AssetListTests
{
    internal static string RepoRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (Directory.Exists(Path.Combine(folder.FullName, "src", "PKForge.App")))
                return folder.FullName;
        throw new DirectoryNotFoundException("The repository root was not found above the test folder.");
    }

    static IEnumerable<XElement> Items(string file, string name) =>
        XDocument.Load(file).Descendants().Where(element => element.Name.LocalName == name);

    static string? Prefixed(string? paths) =>
        paths is null ? null : string.Join(';', paths.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => "../PKForge.App/" + p));

    // AppInfo on the Switch reports the Android manifest's values; this catches an upstream version bump.
    [Fact]
    public void AppVersion_MatchesAndroid()
    {
        var project = XDocument.Load(Path.Combine(RepoRoot(), "src", "PKForge.App", "PKForge.App.csproj"));
        string Property(string name) => project.Descendants().First(element => element.Name.LocalName == name).Value;

        Assert.Equal(PKForge.Switch2.AppHost.Version, Property("ApplicationDisplayVersion"));
        Assert.Equal(PKForge.Switch2.AppHost.Build, Property("ApplicationVersion"));
    }

    [Fact]
    public void AssetList_MatchesUpstream()
    {
        var root = RepoRoot();
        var upstream = Path.Combine(root, "src", "PKForge.App", "PKForge.App.csproj");
        var ours = Items(Path.Combine(root, "src", "PKForge.Switch2.App", "Assets.targets"), "Content")
            .Select(item => (Include: (string?)item.Attribute("Include"), Exclude: (string?)item.Attribute("Exclude"), Link: (string?)item.Attribute("Link")))
            .ToList();

        var expected = Items(upstream, "MauiAsset")
            .Select(item => (Include: Prefixed((string?)item.Attribute("Include")), Exclude: Prefixed((string?)item.Attribute("Exclude")),
                Link: (string?)("app/" + (string?)item.Attribute("LogicalName"))))
            .Concat(Items(upstream, "MauiFont")
                .Select(item => (Include: Prefixed((string?)item.Attribute("Include")), Exclude: (string?)null, Link: (string?)"app/%(Filename)%(Extension)")))
            .ToList();

        Assert.Equal(expected.Count, ours.Count);
        foreach (var item in expected)
            Assert.Contains(item, ours);
    }
}
