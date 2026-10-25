using Unciv.Assets.Packaging;

namespace Unciv.Tests.Assets;

// C042: where the client finds bundled vs checkout rulesets.
public sealed class ContentLocatorTests
{
    private static Func<string, bool> Exists(params string[] dirs) => dirs.ToHashSet().Contains;

    [Fact]
    public void BundledJsonsNextToTheExecutable()
    {
        var bundled = Path.Combine("/opt/unciv", "data", "jsons");
        Assert.Equal(bundled, ContentLocator.BundledJsons("/opt/unciv"));
        Assert.Equal(bundled, ContentLocator.FindJsons("/opt/unciv", "/opt/unciv", Exists(bundled)));
    }

    [Fact]
    public void BundledCopyBeatsACheckoutAboveTheProject()
    {
        var bundled = ContentLocator.BundledJsons("/repo/exports/linux");
        var checkout = Path.Combine("/repo", "android", "assets", "jsons");
        Assert.Equal(bundled, ContentLocator.FindJsons("/repo/exports/linux", "/repo/exports/linux", Exists(bundled, checkout)));
    }

    [Fact]
    public void FallsBackToTheNearestCheckoutAncestor()
    {
        var near = Path.Combine("/repo", "csharp", "android", "assets", "jsons");
        var far = Path.Combine("/repo", "android", "assets", "jsons");
        Assert.Equal(far, ContentLocator.FindJsons("/godot", "/repo/csharp/Unciv.Godot/", Exists(far)));
        Assert.Equal(near, ContentLocator.FindJsons("/godot", "/repo/csharp/Unciv.Godot", Exists(near, far)));
    }

    [Fact]
    public void NothingFoundIsNull()
    {
        Assert.Null(ContentLocator.FindJsons("/opt/unciv", "/opt/unciv", Exists()));
        Assert.Null(ContentLocator.FindJsons("", "", Exists()));
    }
}
