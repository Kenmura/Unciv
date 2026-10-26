using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C044: the "Victory status" page opened from the menu: for each victory the viewer's own milestones with their state and every civ's progress, from
/// <see cref="VictoryStatusModel"/>. A scrolling list and a Close button; it shows what it is given.
/// </summary>
public partial class VictoryStatusView : Control
{
    private readonly ColorRect dim = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly PanelContainer panel = new() { Name = "VictoryPanel" };
    private readonly VBoxContainer lines = new() { Name = "VictoryLines" };
    private readonly ScrollContainer scroll = new() { Name = "VictoryScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly Button close = new() { Name = "VictoryClose", Text = GameMenuModel.CloseLabel.TrText() };

    public VictoryStatusView()
    {
        Name = "VictoryStatus";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        dim.Fill();
        AddChild(dim);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.Fill();
        AddChild(centre);
        centre.AddChild(panel);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(new Label { Text = VictoryStatusModel.Title.TrText(), HorizontalAlignment = HorizontalAlignment.Center });
        scroll.AddChild(lines);
        lines.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        column.AddChild(scroll);
        column.AddChild(close);
        close.Pressed += Close;
    }

    /// <summary>The text of each line as shown, top to bottom.</summary>
    public IReadOnlyList<string> LineTexts => lines.GetChildren().OfType<Label>().Select(it => it.Text).ToList();

    public Button CloseButton => close;

    public void Init(UiTheme uiTheme)
    {
        Theme = uiTheme.Theme;
        var shade = uiTheme.Panel.BgColor;
        shade.A = 0.75f;
        dim.Color = shade;
        scroll.CustomMinimumSize = new Vector2(uiTheme.Px(440), uiTheme.Px(260));
    }

    public void LayoutIn(Vector2 screen) => Size = screen;

    public void Open(IReadOnlyList<VictorySection> sections)
    {
        foreach (var child in lines.GetChildren())
        {
            lines.RemoveChild(child);
            child.QueueFree();
        }
        if (sections.Count == 0) lines.AddChild(Wrapped(VictoryStatusModel.NoVictories));
        foreach (var section in sections)
        {
            lines.AddChild(new Label { Name = "Header_" + section.Name, Text = section.Header.TrText() });
            foreach (var own in section.Own)
            {
                string mark = own.Status switch { "Completed" => "[x] ", "Partially" => "[~] ", _ => "[ ] " };
                lines.AddChild(Wrapped(mark + own.Text.TrText() + (own.Progress.Length > 0 ? " (" + own.Progress + ")" : "")));
            }
            foreach (var civ in section.Civs)
            {
                var label = Wrapped("  " + civ.Text.TrText() + (civ.IsDefeated ? " (defeated)".TrText() : ""));
                label.Name = "Civ_" + section.Name + "_" + civ.Civ;
                lines.AddChild(label);
            }
        }
        Visible = true;
    }

    public void Close() => Visible = false;

    private Label Wrapped(string text) => new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
}
