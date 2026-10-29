using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C087: the collapsible rival stats ribbon of the world HUD (<see cref="WorldHud"/> places it under the research pills, at the left). A header button shows or hides one block
/// per known rival: its name (hover for the luxuries it could trade), then science, culture, gold and military strength. The figures are <see cref="RivalRibbonModel"/>'s:
/// the engine's own answers, "?" when the game does not tell. It shows what it is given and raises <see cref="ExpandedChanged"/>; it asks for nothing itself.
/// </summary>
public partial class RivalRibbonView : PanelContainer
{
    private readonly VBoxContainer column = new() { Name = "RivalColumn", MouseFilter = MouseFilterEnum.Ignore };
    private readonly Button toggle = new() { Name = "RivalsToggle", ThemeTypeVariation = UiVariants.ButtonGhost, Alignment = HorizontalAlignment.Left };
    private readonly VBoxContainer body = new() { Name = "RivalRows", MouseFilter = MouseFilterEnum.Ignore };
    private readonly Dictionary<string, Control> names = [];
    private readonly Dictionary<string, Dictionary<string, Control>> figures = [];
    private UiTheme theme = null!; // JUSTIFY: C087 assigned by Init before any use
    private IReadOnlyList<RivalRow> rows = [];
    private bool expanded;

    public RivalRibbonView()
    {
        Name = "RivalRibbon";
        MouseFilter = MouseFilterEnum.Stop;
        ThemeTypeVariation = UiVariants.PanelCard;
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        Visible = false;
        AddChild(column);
        column.AddChild(toggle);
        column.AddChild(body);
        toggle.Pressed += () => Expanded = !Expanded;
        UpdateToggle();
    }

    /// <summary>Whether the rival blocks show (the ribbon starts collapsed to its header).</summary>
    public bool Expanded
    {
        get => expanded;
        set
        {
            if (expanded == value) return;
            expanded = value;
            body.Visible = value;
            UpdateToggle();
            ExpandedChanged?.Invoke(value);
        }
    }

    public event Action<bool>? ExpandedChanged;

    public Button Toggle => toggle;

    public IReadOnlyList<RivalRow> Rows => rows;

    /// <summary>The figure text of one rival by figure id ("Science", "Culture", "Gold", "Military"), null when there is no such rival or figure.</summary>
    public string? FigureText(string civ, string id) => rows.FirstOrDefault(it => it.CivName == civ)?.Figures.FirstOrDefault(it => it.Id == id)?.Text;

    /// <summary>The tooltip over a rival's name.</summary>
    public string? LuxuryTooltip(string civ) => names.GetValueOrDefault(civ)?.TooltipText;

    /// <summary>The controls of the rival blocks showing (the harness checks them for clipping).</summary>
    public IEnumerable<Control> Blocks => expanded ? body.GetChildren().OfType<Control>() : [];

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
        column.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S1));
        body.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        Rebuild();
    }

    /// <summary>Shows the rivals (the ribbon hides while there are none).</summary>
    public void Show(IReadOnlyList<RivalRow> rivals)
    {
        rows = rivals;
        Rebuild();
    }

    private void UpdateToggle()
    {
        toggle.Text = (expanded ? "v " : "> ") + "Rivals".TrText() + (rows.Count > 0 ? " " + rows.Count : "");
        toggle.TooltipText = (expanded ? "Hide" : "Show").TrText();
    }

    private void Rebuild()
    {
        foreach (var child in body.GetChildren())
        {
            body.RemoveChild(child); // at once, so the ribbon's size is right this frame
            child.QueueFree();
        }
        names.Clear();
        figures.Clear();
        Visible = rows.Count > 0;
        body.Visible = expanded;
        UpdateToggle();
        if (theme == null) return;
        bool compact = theme.Breakpoint == UiBreakpoint.Compact;
        foreach (var row in rows)
        {
            var block = new VBoxContainer { Name = "Rival_" + row.CivName, MouseFilter = MouseFilterEnum.Stop };
            block.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
            var name = new Label
            {
                Name = "Name",
                Text = row.CivName.TrText() + (row.AtWar ? " - " + "War".TrText() : ""),
                ThemeTypeVariation = UiVariants.LabelBodyStrong,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = new Vector2(theme.Px(80), 0f),
                MouseFilter = MouseFilterEnum.Pass,
            };
            string tooltip = LuxuryText(row);
            name.TooltipText = tooltip;
            block.TooltipText = tooltip;
            block.AddChild(name);
            var grid = new GridContainer { Name = "Figures", Columns = compact ? 2 : 4, MouseFilter = MouseFilterEnum.Ignore };
            grid.AddThemeConstantOverride("h_separation", theme.Px(UiTokens.S3));
            grid.AddThemeConstantOverride("v_separation", theme.Px(UiTokens.S1));
            var byId = new Dictionary<string, Control>();
            foreach (var figure in row.Figures)
            {
                Control item = figure.Icon.Category == RulesetAssetKeys.StatIcon ? new StatChip(theme, figure.Icon.Name, figure.Text) : new IconLabel(theme, figure.Icon, figure.Text);
                item.Name = figure.Id;
                item.MouseFilter = MouseFilterEnum.Stop; // a tooltip needs the pointer
                item.TooltipText = figure.Known ? figure.Tooltip.TrText() : figure.Tooltip.TrText() + ": " + "Unknown".TrText();
                grid.AddChild(item);
                byId[figure.Id] = item;
            }
            block.AddChild(grid);
            body.AddChild(block);
            names[row.CivName] = name;
            figures[row.CivName] = byId;
        }
    }

    /// <summary>The figure controls of a rival by figure id (the harness checks them for clipping).</summary>
    public IReadOnlyDictionary<string, Control>? FigureControls(string civ) => figures.GetValueOrDefault(civ);

    private static string LuxuryText(RivalRow row) => row.Luxuries switch
    {
        null => "Tradable luxuries".TrText() + ": " + "Unknown".TrText(),
        { Count: 0 } => "No tradable luxuries".TrText(),
        var luxuries => "Tradable luxuries".TrText() + ": " + string.Join(", ", luxuries.Select(it => it.TrText())),
    };
}
