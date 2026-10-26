using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C024: the victory and defeat screen, a modal over the world: the title and the victory type's own paragraph, the ranking by score and the score history graph
/// (<see cref="GameOverModel"/>, all from the engine's answers) and the buttons the model lists. Pressing a button only raises <see cref="Chosen"/>; the owner sends
/// <c>OneMoreTurn</c>, hides the screen or leaves for the main menu. The view shows what it is given.
/// </summary>
public partial class GameOverScreen : Control
{
    private readonly ColorRect dim = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly PanelContainer panel = new() { Name = "GameOverPanel" };
    private readonly Label title = new() { Name = "GameOverTitle", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Label body = new() { Name = "GameOverText", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer ranking = new() { Name = "GameOverRanking" };
    private readonly GraphControl graph = new() { Name = "GameOverGraph" };
    private readonly HBoxContainer legend = new() { Name = "GameOverLegend" };
    private readonly HBoxContainer buttons = new() { Name = "GameOverButtons", Alignment = BoxContainer.AlignmentMode.Center };
    private readonly Dictionary<string, Button> byId = [];

    public GameOverScreen()
    {
        Name = "GameOver";
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
        column.AddChild(title);
        column.AddChild(body);
        column.AddChild(ranking);
        column.AddChild(graph);
        column.AddChild(legend);
        column.AddChild(buttons);
    }

    public GameOverView? Current { get; private set; }

    public string TitleText => title.Text;

    public string BodyText => body.Text;

    /// <summary>The ranking lines as shown, top to bottom.</summary>
    public IReadOnlyList<string> RankingTexts => ranking.GetChildren().OfType<Label>().Select(it => it.Text).ToList();

    /// <summary>How many lines the graph draws and how many points they have in all.</summary>
    public (int Lines, int Points) GraphSize => (graph.Lines.Count, graph.Lines.Sum(it => it.Points.Count));

    public IReadOnlyList<string> ButtonIds => buttons.GetChildren().Select(it => it.Name.ToString()).ToList();

    public Button? ButtonById(string id) => byId.GetValueOrDefault(id);

    public event Action<GameOverButtonKind>? Chosen;

    public void Init(UiTheme uiTheme)
    {
        Theme = uiTheme.Theme;
        var shade = uiTheme.Panel.BgColor;
        shade.A = 0.75f;
        dim.Color = shade;
        title.AddThemeFontSizeOverride("font_size", uiTheme.Px(26));
        graph.CustomMinimumSize = new Vector2(uiTheme.Px(420), uiTheme.Px(150));
        graph.Axis = uiTheme.Panel.BorderColor;
        body.CustomMinimumSize = new Vector2(uiTheme.Px(420), 0f);
    }

    public void LayoutIn(Vector2 screen) => Size = screen;

    /// <param name="view">What to show.</param>
    /// <param name="colourOf">The civ's colour by name; null for a civ the ruleset does not know.</param>
    public void Open(GameOverView view, Func<string, Rgb?> colourOf)
    {
        Current = view;
        title.Text = view.Title.Tr();
        body.Text = view.Text.Tr();
        foreach (var child in ranking.GetChildren())
        {
            ranking.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var row in view.Ranking)
        {
            var label = new Label { Text = GameOverModel.RowText(row).Tr() };
            if (row.IsViewer) label.AddThemeColorOverride("font_color", ToColour(PlaceholderPalette.Accent));
            ranking.AddChild(label);
        }
        foreach (var child in legend.GetChildren())
        {
            legend.RemoveChild(child);
            child.QueueFree();
        }
        graph.Show(view.Graph, line => ToColour(colourOf(line.Civ) ?? PlaceholderPalette.FromHash(line.Civ, 150, 210)));
        foreach (var line in view.Graph.Lines)
        {
            var key = new ColorRect { Color = ToColour(colourOf(line.Civ) ?? PlaceholderPalette.FromHash(line.Civ, 150, 210)), CustomMinimumSize = new Vector2(14f, 14f), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            legend.AddChild(key);
            legend.AddChild(new Label { Text = line.Civ.Tr() });
        }
        foreach (var child in buttons.GetChildren())
        {
            buttons.RemoveChild(child);
            child.QueueFree();
        }
        byId.Clear();
        foreach (var spec in view.Buttons)
        {
            var button = new Button { Name = spec.Id, Text = spec.Label.Tr() };
            var kind = spec.Kind;
            button.Pressed += () => Chosen?.Invoke(kind);
            buttons.AddChild(button);
            byId[spec.Id] = button;
        }
        Visible = true;
    }

    public void Close()
    {
        Current = null;
        Visible = false;
    }

    private static Color ToColour(Rgb rgb) => new(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);

    /// <summary>Draws the model's lines in the control's rectangle (with a small margin) on top of an axis frame.</summary>
    private sealed partial class GraphControl : Control
    {
        private Func<GraphLine, Color> colourOf = _ => default;

        public IReadOnlyList<GraphLine> Lines { get; private set; } = [];

        public Color Axis { get; set; }

        private static Color Outline => ToColour(PlaceholderPalette.Text);

        public void Show(GraphAxes axes, Func<GraphLine, Color> colours)
        {
            Lines = axes.Lines;
            colourOf = colours;
            QueueRedraw();
        }

        public override void _Draw()
        {
            const float margin = 6f;
            var area = new Rect2(margin, margin, Size.X - 2 * margin, Size.Y - 2 * margin);
            DrawRect(new Rect2(Vector2.Zero, Size), Axis, filled: false);
            foreach (var line in Lines)
            {
                var points = line.Points.Select(it => new Vector2(area.Position.X + it.X * area.Size.X, area.Position.Y + it.Y * area.Size.Y)).ToArray();
                var colour = colourOf(line);
                float width = line.IsViewer ? 3f : 1.5f;
                // an outline in the text colour under the civ's colour, so a dark civ colour still shows on the dark panel
                if (points.Length == 1)
                {
                    DrawCircle(points[0], width + 3f, Outline);
                    DrawCircle(points[0], width + 1f, colour);
                }
                else
                {
                    DrawPolyline(points, Outline, width + 2f, antialiased: true);
                    DrawPolyline(points, colour, width, antialiased: true);
                }
            }
        }
    }
}
