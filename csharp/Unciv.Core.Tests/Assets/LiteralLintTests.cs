using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

public class LiteralLintTests
{
    [Theory]
    [InlineData("var t = GD.Load(\"res://x.png\");", "res:// literal")]
    [InlineData("var c = new Color(1, 0, 0);", "colour literal")]
    [InlineData("var c = new Color (1, 0, 0);", "colour literal")]
    [InlineData("var c = Colors.Red;", "colour literal")]
    [InlineData("var c = Color.Color8(1, 2, 3);", "colour literal")]
    [InlineData("var c = Color.FromHtml(\"x\");", "colour literal")]
    [InlineData("var c = \"#ff00aa\";", "colour literal")]
    [InlineData("var c = \"#fff\";", "colour literal")]
    public void DetectsViolations(string line, string rule) => Assert.Equal(rule, Assert.Single(LiteralLint.Scan("X.cs", line)).Rule);

    [Theory]
    [InlineData("// res://x.png in a comment")]
    [InlineData("    /// new Color(1,2,3)")]
    [InlineData(" * Colors.Red")]
    [InlineData("var s = \"issue #12\";")]
    [InlineData("var c = registry.Get<Material>(key);")]
    [InlineData("var c = MyColors.Red;")]
    public void IgnoresCommentsAndLookalikes(string line) => Assert.Empty(LiteralLint.Scan("X.cs", line));

    [Fact]
    public void ReportsLineNumbers() => Assert.Equal(3, LiteralLint.Scan("X.cs", "a\nb\nres://c").Single().Line);

    [Fact]
    public void ScanDirectorySkipsAssetsAndBuildOutput()
    {
        var root = Directory.CreateTempSubdirectory("lint-").FullName;
        try
        {
            foreach (var dir in new[] { "Assets", "obj", "UI" }) Directory.CreateDirectory(Path.Combine(root, dir));
            File.WriteAllText(Path.Combine(root, "Assets", "A.cs"), "res://ok");
            File.WriteAllText(Path.Combine(root, "obj", "B.cs"), "res://ok");
            File.WriteAllText(Path.Combine(root, "UI", "C.cs"), "res://bad");
            File.WriteAllText(Path.Combine(root, "D.cs"), "fine");
            var found = LiteralLint.ScanDirectory(root);
            Assert.Equal("UI/C.cs", Assert.Single(found).File);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GodotClientHasNoLiteralsOutsideAssets()
    {
        string? client = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null && client == null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Unciv.Godot");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "Unciv.sln"))) client = candidate;
        }
        Assert.NotNull(client);
        var violations = LiteralLint.ScanDirectory(client);
        Assert.True(violations.Count == 0, string.Join("\n", violations.Select(v => $"{v.File}:{v.Line} {v.Rule}: {v.Text}")));
    }
}
