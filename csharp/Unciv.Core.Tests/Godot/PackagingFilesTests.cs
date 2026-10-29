namespace Unciv.Tests.Godot;

// C042: the packaging inputs exist and say what the smoke test and the licences rely on (the export itself needs the Godot editor and templates).
public sealed class PackagingFilesTests
{
    private static readonly string Project = FindProject();

    private static string FindProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Unciv.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Unciv.sln not found"), "Unciv.Godot");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Project, relative));

    [Theory]
    [InlineData("Linux", "Linux", "build/linux/Unciv.x86_64")]
    [InlineData("Windows", "Windows Desktop", "build/windows/Unciv.exe")]
    public void ExportPresetNamesPlatformAndOutputUnderBuild(string name, string platform, string path)
    {
        var cfg = Read("export_presets.cfg");
        var start = cfg.IndexOf($"name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, name);
        var section = cfg[start..];
        Assert.Contains($"platform=\"{platform}\"", section[..section.IndexOf("[preset", 1, StringComparison.Ordinal)]);
        Assert.Contains($"export_path=\"{path}\"", section);
    }

    [Fact]
    public void ExportedFilesStayOutOfSourceControl()
    {
        Assert.Contains("build/", Read(".gitignore").Split('\n').Select(it => it.Trim()));
    }

    [Fact]
    public void NoticesNameTheLicencesWeShip()
    {
        var notices = Read("THIRD_PARTY_NOTICES.md");
        Assert.Contains("Mozilla Public License 2.0", notices);
        Assert.Contains("Godot Engine", notices);
        Assert.Contains("MIT", notices);
        Assert.Contains("Firaxis", notices);
    }

    [Fact]
    public void PackageScriptCopiesRulesetsNoticesAndLicence()
    {
        var script = Read("tools/package.sh");
        Assert.Contains("android/assets/jsons\" \"$root/data/jsons\"", script);
        Assert.Contains("THIRD_PARTY_NOTICES.md", script);
        Assert.Contains("LICENSE", script);
        Assert.Contains("--export-release", script);
    }

    [Fact]
    public void TemplatesMatchThePinnedEditorVersion()
    {
        var pinned = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(Path.Combine(Project, "..", "Unciv.Godot.Harness", "tools", "setup-godot.sh")), "^version=\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.Multiline).Groups[1].Value;
        Assert.NotEmpty(pinned);
        Assert.Contains($"version=\"{pinned}\"", Read("tools/install-export-templates.sh"));
    }
}
