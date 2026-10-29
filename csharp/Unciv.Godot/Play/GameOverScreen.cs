using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.Queries.Victory;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C024: the victory and defeat screen, a modal over the world: the title and the victory type's own paragraph, the ranking by score and the score history graph
/// (<see cref="GameOverModel"/>, all from the engine's answers) and the buttons the model lists. Pressing a button only raises <see cref="Chosen"/>; the owner sends
/// <c>OneMoreTurn</c>, hides the screen or leaves for the main menu. The view shows what it is given.
/// C077: a C066 <see cref="Modal"/>: a heading in the accent (victory) or danger colour (defeat), the ranking as a <see cref="DataTable"/> (your row in the accent colour, the defeated muted),
/// the graph with a value axis (lowest and highest score), a turn axis (first and last turn) and gridlines, and a legend of the civs in their colours.
/// </summary>
public partial class GameOverScreen : Modal
{
    private readonly Label body = new() { Name = "GameOverText", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly DataTable ranking = new([new DataColumn("Rank", Numeric: true), new DataColumn("Civilization"), new DataColumn("Score", Numeric: true)]);
    private readonly GraphControl graph = new() { Name = "GameOverGraph" };
    private readonly HFlowContainer legend = new() { Name = "GameOverLegend" };
    private readonly VBoxContainer content = new() { Name = "GameOverContent" };
    private readonly ScrollContainer scroll = new() { Name = "GameOverScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly Dictionary<string, Button> byId = [];

    public GameOverScreen()
    {
        Name = "GameOver";
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        graph.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ranking.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(content);
    }

    public GameOverView? Current { get; private set; }

    public string TitleText => TitleLabel.Text;

    public string BodyText => body.Text;

    /// <summary>The ranking table.</summary>
    public DataTable Ranking => ranking;

    /// <summary>How many lines the graph draws and how many points they have in all.</summary>
    public (int Lines, int Points) GraphSize => (graph.Lines.Count, graph.Lines.Sum(it => it.Points.Count));

    /// <summary>The graph's axis labels: lowest and highest value, first and last turn.</summary>
    public IReadOnlyList<string> GraphAxisTexts => graph.AxisTexts;

    /// <summary>The civs of the legend, in line order.</summary>
    public IReadOnlyList<string> LegendTexts => legend.GetChildren().SelectMany(it => it.GetChildren().OfType<Label>()).Select(it => it.Text).ToList();

    public IReadOnlyList<string> ButtonIds => Actions.Select(it => it.Name.ToString()).ToList();

    public Button? ButtonById(string id) => byId.GetValueOrDefault(id);

    public event Action<GameOverButtonKind>? Chosen;

    public override void Init(UiTheme uiTheme)
    {
        base.Init(uiTheme);
        graph.Bind(uiTheme);
        legend.AddThemeConstantOverride("h_separation", uiTheme.Px(UiTokens.S3));
        content.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
    }

    /// <param name="view">What to show.</param>
    /// <param name="colourOf">The civ's colour by name; null for a civ the ruleset does not know.</param>
    public void Open(GameOverView view, Func<string, Rgb?> colourOf)
    {
        Current = view;
        SetHeader(view.Title);
        TitleLabel.Text = view.Title.Tr(); // the title keeps its [..] markers, which TrText would take for composed output
        TitleLabel.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(view.Status switch
        {
            GameOverQuery.Won => UiTokens.Accent,
            GameOverQuery.Lost or GameOverQuery.Defeated => UiTokens.DangerText,
            _ => UiTokens.TextPrimary,
        }));
        float width = Fit(MaxWidth);
        body.Text = view.Text.Tr();
        body.CustomMinimumSize = new Vector2(width, 0f);
        var viewerTone = ProceduralAssets.ToColor(UiTokens.Accent);
        var mutedTone = ProceduralAssets.ToColor(UiTokens.TextMuted);
        ranking.RowTone = row => view.Ranking[row].IsViewer ? viewerTone : view.Ranking[row].IsDefeated ? mutedTone : null;
        ranking.SetRows(RankingRows(view));
        foreach (var child in content.GetChildren())
        {
            content.RemoveChild(child);
            if (child is SectionHeader) child.QueueFree(); // the section headers are made per Open; the other children are reused
        }
        foreach (var child in legend.GetChildren())
        {
            legend.RemoveChild(child);
            child.QueueFree();
        }
        Color Colour(string civ) => ProceduralAssets.ToColor(colourOf(civ) ?? PlaceholderPalette.FromHash(civ, 150, 210));
        graph.Show(view.Graph, line => Colour(line.Civ));
        foreach (var line in view.Graph.Lines)
        {
            var key = new ColorRect { Color = Colour(line.Civ), CustomMinimumSize = new Vector2(Ui.Px(14), Ui.Px(14)), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            var entry = new HBoxContainer();
            entry.AddThemeConstantOverride("separation", Ui.Px(UiTokens.S1));
            entry.AddChild(key);
            entry.AddChild(new Label { Text = line.Civ.Tr(), ThemeTypeVariation = UiVariants.LabelSecondary });
            legend.AddChild(entry);
        }
        content.AddChild(body);
        if (view.Ranking.Count > 0)
        {
            content.AddChild(new SectionHeader("Rankings"));
            content.AddChild(ranking);
        }
        if (view.Graph.Lines.Count > 0)
        {
            content.AddChild(new SectionHeader("Score"));
            graph.CustomMinimumSize = new Vector2(0f, Ui.Px(110));
            content.AddChild(graph);
            content.AddChild(legend);
        }
        foreach (var child in Body.GetChildren()) Body.RemoveChild(child);
        scroll.CustomMinimumSize = new Vector2(width, 0f);
        Body.AddChild(scroll);
        FitScroll(scroll, content, 0.72f);
        byId.Clear();
        var actions = new List<(Button, ChoiceRole)>();
        foreach (var spec in view.Buttons)
        {
            var button = new Button { Name = spec.Id, Text = spec.Label.TrText() };
            var kind = spec.Kind;
            button.Pressed += () => Chosen?.Invoke(kind);
            actions.Add((button, spec.Kind == GameOverButtonKind.MainMenu && view.Buttons.Count > 1 ? ChoiceRole.Secondary : ChoiceRole.Primary));
            byId[spec.Id] = button;
        }
        SetActions(actions);
        Visible = true;
    }

    public void Close()
    {
        Current = null;
        Visible = false;
    }

    private static IReadOnlyList<IReadOnlyList<string>> RankingRows(GameOverView view) => view.Ranking.Select(row => (IReadOnlyList<string>)
    [
        row.IsDefeated ? "-" : row.Place.ToString(CultureInfo.InvariantCulture),
        row.Civ.Tr() + (row.IsDefeated ? " (defeated)".TrText() : ""),
        row.Value.ToString(CultureInfo.InvariantCulture),
    ]).ToList();

    /// <summary>Draws the model's lines in a frame with gridlines, the value axis at the left and the turn axis below.</summary>
    private sealed partial class GraphControl : Control
    {
        private Func<GraphLine, Color> colourOf = _ => default;
        private GraphAxes axes = new(0, 0, 0, 0, []);
        private UiTheme? theme;

        public IReadOnlyList<GraphLine> Lines => axes.Lines;

        /// <summary>Lowest value, highest value, first turn, last turn as drawn.</summary>
        public IReadOnlyList<string> AxisTexts => axes.Lines.Count == 0 ? [] :
        [
            axes.MinValue.ToString(CultureInfo.InvariantCulture),
            axes.MaxValue.ToString(CultureInfo.InvariantCulture),
            axes.FirstTurn.ToString(CultureInfo.InvariantCulture),
            axes.LastTurn.ToString(CultureInfo.InvariantCulture),
        ];

        public void Bind(UiTheme uiTheme) => theme = uiTheme;

        public void Show(GraphAxes graphAxes, Func<GraphLine, Color> colours)
        {
            axes = graphAxes;
            colourOf = colours;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (theme == null) return;
            var font = GetThemeDefaultFont();
            int fontSize = Math.Max(10, theme.Px(12));
            var texts = AxisTexts;
            float labelWidth = texts.Count == 0 ? 0f : texts.Take(2).Max(it => font.GetStringSize(it, HorizontalAlignment.Left, -1, fontSize).X);
            float pad = theme.Px(UiTokens.S2);
            float labelHeight = font.GetHeight(fontSize);
            var area = new Rect2(labelWidth + 2 * pad, pad, Size.X - labelWidth - 3 * pad - theme.Px(UiTokens.S2), Size.Y - labelHeight - 3 * pad);
            var grid = ProceduralAssets.ToColor(UiTokens.BorderSubtle);
            var frame = ProceduralAssets.ToColor(UiTokens.BorderStrong);
            var muted = ProceduralAssets.ToColor(UiTokens.TextSecondary);
            for (int i = 1; i < 4; i++)
            {
                float y = area.Position.Y + area.Size.Y * i / 4f;
                DrawLine(new Vector2(area.Position.X, y), new Vector2(area.End.X, y), grid, 1f);
            }
            DrawRect(area, frame, filled: false);
            if (texts.Count == 4)
            {
                DrawString(font, new Vector2(area.Position.X - pad - labelWidth, area.Position.Y + fontSize), texts[1], HorizontalAlignment.Right, labelWidth, fontSize, muted);
                DrawString(font, new Vector2(area.Position.X - pad - labelWidth, area.End.Y), texts[0], HorizontalAlignment.Right, labelWidth, fontSize, muted);
                float baseline = area.End.Y + pad + labelHeight - font.GetDescent(fontSize);
                DrawString(font, new Vector2(area.Position.X, baseline), texts[2], HorizontalAlignment.Left, -1, fontSize, muted);
                DrawString(font, new Vector2(area.End.X - 200f, baseline), texts[3], HorizontalAlignment.Right, 200f, fontSize, muted);
                string turn = "Turn".TrText();
                float turnWidth = font.GetStringSize(turn, HorizontalAlignment.Left, -1, fontSize).X;
                DrawString(font, new Vector2(area.GetCenter().X - turnWidth / 2f, baseline), turn, HorizontalAlignment.Left, -1, fontSize, muted);
            }
            var outline = ProceduralAssets.ToColor(UiTokens.Bg0);
            var inner = area.Grow(-pad / 2f);
            foreach (var line in axes.Lines)
            {
                var points = line.Points.Select(it => new Vector2(inner.Position.X + it.X * inner.Size.X, inner.Position.Y + it.Y * inner.Size.Y)).ToArray();
                var colour = colourOf(line);
                float width = line.IsViewer ? 3f : 1.5f;
                // a dark outline under the civ's colour, so a dark civ colour still shows on the dark panel
                if (points.Length == 1)
                {
                    DrawCircle(points[0], width + 3f, outline);
                    DrawCircle(points[0], width + 1f, colour);
                }
                else
                {
                    DrawPolyline(points, outline, width + 2f, antialiased: true);
                    DrawPolyline(points, colour, width, antialiased: true);
                }
            }
        }
    }
}
