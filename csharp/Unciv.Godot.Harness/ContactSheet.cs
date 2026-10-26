using Godot;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// Builds the owner's review artefacts (C008) from the results of earlier scenario runs: <c>out/review/contact-sheet.png</c>, a grid
/// of every captured step with its caption, and <c>out/review/index.md</c> with the checklist. Never fails because a scenario failed;
/// it shows the failure instead. User arguments: <c>--out=DIR</c> (the scenario output root), <c>--scenarios=a,b,c</c>.
/// </summary>
public partial class ContactSheet : Node
{
    private const int SourceWidth = 640;
    private const int SourceHeight = 480;

    public override void _Ready() => _ = BuildAsync();

    private async Task BuildAsync()
    {
        int exitCode = 1;
        try
        {
            await Build();
            exitCode = 0;
        }
        catch (Exception e)
        {
            GD.PrintErr("[sheet] crashed: " + e);
        }
        GetTree().Quit(exitCode);
    }

    private async Task Build()
    {
        string outDir = "out";
        var names = new List<string>();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--out=", StringComparison.Ordinal)) outDir = arg["--out=".Length..];
            else if (arg.StartsWith("--scenarios=", StringComparison.Ordinal))
                names.AddRange(arg["--scenarios=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries));
        }

        var sections = new List<ReviewSection>();
        var cells = new List<(string Png, string Caption, bool Passed)>();
        foreach (string name in names)
        {
            string resultFile = Path.Combine(outDir, name, "result.json");
            if (!File.Exists(resultFile))
            {
                sections.Add(new ReviewSection(name, false, ["no result.json: the scenario crashed or did not run"], [], []));
                continue;
            }
            var result = ScenarioResult.FromJson(File.ReadAllText(resultFile));
            sections.Add(new ReviewSection(name, result.Passed, result.Failures, result.Captures, result.Checklist));
            foreach (var capture in result.Captures)
                cells.Add((Path.Combine(outDir, name, capture.Step + ".png"), $"{(result.Passed ? "" : "FAILED ")}{name} / {capture.Step}\n{capture.Caption}", result.Passed));
        }

        string reviewDir = Path.Combine(outDir, "review");
        Directory.CreateDirectory(reviewDir);
        File.WriteAllText(Path.Combine(reviewDir, "index.md"), ReviewIndex.Render(sections, "contact-sheet.png", "../"));

        var layout = ContactSheetLayout.Compute(Math.Max(1, cells.Count), SourceWidth, SourceHeight, ContactSheetLayout.ColumnsFor(cells.Count));
        var viewport = new SubViewport
        {
            Size = new Vector2I(layout.Width, layout.Height),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };
        AddChild(viewport);
        viewport.AddChild(new ColorRect { Color = new Color(0.18f, 0.18f, 0.2f), Size = new Vector2(layout.Width, layout.Height) });
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = layout.Cells[i];
            var (png, caption, passed) = cells[i];
            using var image = Image.LoadFromFile(png);
            viewport.AddChild(new TextureRect
            {
                Texture = ImageTexture.CreateFromImage(image),
                Position = new Vector2(cell.X, cell.Y),
                Size = new Vector2(cell.ThumbWidth, cell.ThumbHeight),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
            });
            var label = new Label
            {
                Text = caption,
                Position = new Vector2(cell.X, cell.Y + cell.ThumbHeight),
                Size = new Vector2(cell.ThumbWidth, cell.CaptionHeight),
                ClipText = true,
            };
            label.AddThemeFontSizeOverride("font_size", 12);
            if (!passed) label.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.4f));
            viewport.AddChild(label);
        }

        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string sheetPath = Path.Combine(reviewDir, "contact-sheet.png");
        ViewportCapture.CaptureViewportPng(viewport, sheetPath);
        GD.Print($"[sheet] {cells.Count} capture(s) from {sections.Count} scenario(s) -> {sheetPath}");
    }
}
