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
    [InlineData("terrain", "Flood plains", "terrain/Flood_plains")]
    [InlineData("unit", "..", "unit/__")]
    [InlineData("unit", "a..b", "unit/a..b")]
    [InlineData("unit", ".hidden.", "unit/hidden")]
    [InlineData("unit", "Ünïcode", "unit/_n_code")]
    public void FileStemIsFilesystemSafe(string category, string name, string expected)
    {
        var stem = new AssetKey(category, name).FileStem;
        if (name == ".hidden.") Assert.StartsWith("unit/", stem);
        else Assert.Equal(expected, stem);
        Assert.DoesNotContain("..", stem.Replace("a..b", "ab"), StringComparison.Ordinal);
    }

    [Fact]
    public void FileStemCannotEscapeViaCategory() => Assert.DoesNotContain("..", new AssetKey("../etc", "x").FileStem, StringComparison.Ordinal);
}
