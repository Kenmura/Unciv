using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

public class LiteralLintTests
{
    [Theory]
    [InlineData("var t = GD.Load(\"res://x.png\");", "res:// literal")]
    [InlineData("var c = new Color(1, 0, 0);", "colour literal")]
    [InlineData("var c = new Color (1, 0, 0);", "colour literal")]
    [InlineData("var c = Colors.Red;", "colour literal")]
    [InlineData("var c = new Godot.Color(1, 0, 0);", "colour literal")]
    [InlineData("var c = Color.Color8(1, 2, 3);", "colour literal")]
    [InlineData("var c = Color.FromHtml(\"x\");", "colour literal")]
    [InlineData("var c = \"#ff00aa\";", "colour literal")]
    [InlineData("var c = \"#fff\";", "colour literal")]
    [InlineData("Color c = new(1f, 0f, 0f);", "colour literal")]
    [InlineData("var p = \"uid://abc\";", "res:// literal")]
    [InlineData("var p = \"user://save\";", "res:// literal")]
    [InlineData("var p = \"res\" + \"://\" + x;", "res:// literal")]
    [InlineData("var b = new Button { Text = \"Cancel\" };", "untranslated text literal")]
    [InlineData("x.TooltipText = \"Not available yet\";", "untranslated text literal")]
    [InlineData("new LineEdit { PlaceholderText = \"random\" };", "untranslated text literal")]
    public void DetectsViolations(string line, string rule) => Assert.Equal(rule, Assert.Single(LiteralLint.Scan("X.cs", line)).Rule);

    [Theory]
    [InlineData("// res://x.png in a comment")]
    [InlineData("    /// new Color(1,2,3)")]
    [InlineData("var s = \"issue #12\";")]
    [InlineData("var c = registry.Get<Material>(key);")]
    [InlineData("var c = MyColors.Red;")]
    [InlineData("var b = new Button { Text = \"Cancel\".Tr() };")]
    [InlineData("label.Text = \"\";")]
    [InlineData("label.Text = \"+\";")]
    [InlineData("label.Text = $\"{x} units\";")]
    [InlineData("var b = new Button { Name = \"Cancel\" };")]
    public void IgnoresCommentsAndLookalikes(string line) => Assert.Empty(LiteralLint.Scan("X.cs", line));

    [Fact]
    public void BlockCommentsAreSkippedButCodeAfterThemIsScanned()
    {
        Assert.Empty(LiteralLint.Scan("X.cs", "/* start\nres://inside\n end */"));
        Assert.Equal(4, Assert.Single(LiteralLint.Scan("X.cs", "/* a */\n/* b\nc */\nres://x")).Line);
        Assert.Empty(LiteralLint.Scan("X.cs", "/* one line res:// */"));
    }

    [Fact]
    public void TargetTypedVectorsAreNotColours() => Assert.Empty(LiteralLint.Scan("X.cs", "Vector3 v = new(1f, 2f, 3f);"));

    [Fact]
    public void ReportsLineNumbers() => Assert.Equal(3, LiteralLint.Scan("X.cs", "a\nb\nres://c").Single().Line);

    [Fact]
    public void ScanDirectorySkipsAssetsAndBuildOutput()
    {
        var root = Directory.CreateTempSubdirectory("lint-").FullName;
        try
        {
            foreach (var dir in new[] { "Assets", "obj", "UI", "Screens/Assets", "Screens/bin" }) Directory.CreateDirectory(Path.Combine(root, dir));
            File.WriteAllText(Path.Combine(root, "Assets", "A.cs"), "res://ok");
            File.WriteAllText(Path.Combine(root, "obj", "B.cs"), "res://ok");
            File.WriteAllText(Path.Combine(root, "UI", "C.cs"), "res://bad");
            File.WriteAllText(Path.Combine(root, "D.cs"), "fine");
            File.WriteAllText(Path.Combine(root, "Screens", "Assets", "E.cs"), "res://nested assets dir is not exempt");
            File.WriteAllText(Path.Combine(root, "Screens", "bin", "F.cs"), "res://build output");
            var found = LiteralLint.ScanDirectory(root);
            Assert.Equal(["Screens/Assets/E.cs", "UI/C.cs"], found.Select(v => v.File));
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
