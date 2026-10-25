using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

public class AssetKeyTests
{
    [Fact]
    public void ParseSplitsAtLastSlash()
    {
        var key = AssetKey.Parse("icon/stat/Gold");
        Assert.Equal("icon/stat", key.Category);
        Assert.Equal("Gold", key.Name);
        Assert.Equal("icon/stat/Gold", key.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("nocategory")]
    [InlineData("/Name")]
    [InlineData("cat/")]
    [InlineData("a//b")]
    [InlineData("a/b/")]
    public void ParseRejectsMalformed(string text)
    {
        Assert.False(AssetKey.TryParse(text, out _));
        Assert.Throws<FormatException>(() => AssetKey.Parse(text));
    }

    [Fact]
    public void TryParseNullIsFalse() => Assert.False(AssetKey.TryParse(null, out _));

    [Fact]
    public void ConstructorRejectsSlashInName() => Assert.Throws<ArgumentException>(() => new AssetKey("unit", "a/b"));

    [Fact]
    public void ConstructorRejectsBadCategory()
    {
        Assert.Throws<ArgumentException>(() => new AssetKey("", "x"));
        Assert.Throws<ArgumentException>(() => new AssetKey("/a", "x"));
        Assert.Throws<ArgumentException>(() => new AssetKey("a/", "x"));
        Assert.Throws<ArgumentException>(() => new AssetKey("a//b", "x"));
        Assert.Throws<ArgumentException>(() => new AssetKey("a", ""));
    }

    [Fact]
    public void OfReplacesSlashInRulesetNames() => Assert.Equal("unit/Foo_Bar", AssetKey.Of("unit", "Foo/Bar").ToString());

    [Fact]
    public void DefaultKeyStaysInCategory()
    {
        var key = new AssetKey("icon/stat", "Gold");
        Assert.Equal("icon/stat/_default", key.Default.ToString());
        Assert.True(key.Default.IsDefault);
        Assert.False(key.IsDefault);
    }

    [Theory]
    [InlineData("terrain", "Grassland", "terrain/Grassland")]
    [InlineData("terrain", "Flood plains", "terrain/Flood%20plains")]
    [InlineData("unit", "..", "unit/%2E%2E")]
    [InlineData("unit", ".", "unit/%2E")]
    [InlineData("unit", "a..b", "unit/a..b")]
    [InlineData("unit", "%", "unit/%25")]
    [InlineData("unit", "\u4E2D", "unit/%E4%B8%AD")]
    public void FileStemIsFilesystemSafe(string category, string name, string expected) => Assert.Equal(expected, new AssetKey(category, name).FileStem);

    [Fact]
    public void FileStemIsCollisionFree()
    {
        string[] names = ["a b", "a_b", "a%20b", "\u4E2D", "\u6587", "\u00E9", "e", "A", "a"];
        Assert.Equal(names.Length, names.Select(n => new AssetKey("unit", n).FileStem).Distinct().Count());
    }

    [Fact]
    public void FileStemCannotEscapeViaCategory() => Assert.DoesNotContain("..", new AssetKey("../etc", "x").FileStem, StringComparison.Ordinal);
}
