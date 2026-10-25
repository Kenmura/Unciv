using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.GodotClient.Ui;
using Unciv.Models.Ruleset;
using Unciv.Queries.Cities;
using Unciv.Models.Translations;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C018: the production half of the city screen: the queue (raise, lower, to the top, remove), the buildable list with turns, cost and a buy button per currency
/// (disabled with the reason as tooltip), the built buildings with a sell button, and the puppet / razing state. Everything shown is the engine's answer
/// formatted by <see cref="CityProductionModel"/>; every click is a command sent through <see cref="send"/> (the owning <see cref="CityScreen"/> forwards it to the session).
/// </summary>
public partial class CityProductionView : HBoxContainer
{
    private readonly Func<GameCommand, Task> send;
    private readonly Action<string> report;
    private readonly UiTheme theme;
    private readonly VBoxContainer queueColumn = new() { Name = "QueueColumn" };
    private readonly VBoxContainer buildColumn = new() { Name = "BuildColumn" };
    private readonly VBoxContainer buildingColumn = new() { Name = "BuildingColumn" };
    private readonly Dictionary<string, Button> buttons = [];
    private readonly List<string> queueTexts = [];
    private readonly List<string> buildableTexts = [];
    private readonly List<string> buildingTexts = [];
    private string stateText = "";

    public CityProductionView(Func<GameCommand, Task> sendCommand, Action<string> reportMessage, UiTheme uiTheme)
    {
        send = sendCommand;
        report = reportMessage;
        theme = uiTheme;
        Name = "Production";
        SizeFlagsVertical = SizeFlags.ExpandFill;
        queueColumn.CustomMinimumSize = new Vector2(uiTheme.Px(205), 0f);
        buildingColumn.CustomMinimumSize = new Vector2(uiTheme.Px(125), 0f);
        buildColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(queueColumn);
        var scroll = new ScrollContainer { Name = "BuildScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(buildColumn);
        AddChild(scroll);
        AddChild(buildingColumn);
    }

    /// <summary>The queue lines, the buildable lines, the built building lines and the state line, as shown.</summary>
    public IReadOnlyList<string> QueueTexts => queueTexts;

    public IReadOnlyList<string> BuildableTexts => buildableTexts;

    public IReadOnlyList<string> BuildingTexts => buildingTexts;

    public string StateText => stateText;

    /// <summary>A button by name: <c>Up_i</c>, <c>Down_i</c>, <c>Top_i</c>, <c>Remove_i</c> (queue entry), <c>Build_name</c>, <c>Buy_name_Stat</c>, <c>Sell_name</c>, <c>State_label</c>.</summary>
    public Button? ButtonNamed(string name) => buttons.GetValueOrDefault(name);

    public void Show(CityPanelResult panel, ConstructionListResult list, int gold, Ruleset ruleset)
    {
        Clear(queueColumn);
        Clear(buildColumn);
        Clear(buildingColumn);
        buttons.Clear();
        queueTexts.Clear();
        buildableTexts.Clear();
        buildingTexts.Clear();

        queueColumn.AddChild(Heading("Production queue"));
        var queue = CityProductionModel.Queue(panel, list, gold);
        if (queue.Count == 0) queueColumn.AddChild(new Label { Text = "The queue is empty.".Tr() });
        foreach (var row in queue)
        {
            queueTexts.Add(row.Label);
            var line = new VBoxContainer { Name = "QueueRow_" + row.Index };
            line.AddChild(Wrapped(row.Label, 190));
            var controls = new HBoxContainer();
            controls.AddChild(MakeButton($"Top_{row.Index}", "Top", row.CanTop, "Move to the top", CityProductionModel.Reorder(panel.CityId, row, QueueMove.Top)));
            controls.AddChild(MakeButton($"Up_{row.Index}", "Up", row.CanRaise, "Raise priority", CityProductionModel.Reorder(panel.CityId, row, QueueMove.Up)));
            controls.AddChild(MakeButton($"Down_{row.Index}", "Down", row.CanLower, "Lower priority", CityProductionModel.Reorder(panel.CityId, row, QueueMove.Down)));
            controls.AddChild(MakeButton($"Remove_{row.Index}", "X", row.CanRemove, "Remove from the queue", CityProductionModel.Remove(panel.CityId, row)));
            foreach (var option in row.Buy)
                controls.AddChild(MakeButton($"QBuy_{row.Index}_{option.Stat}", option.Label, option.Allowed, option.Allowed ? $"Buy {row.Name} now" : option.Why, CityProductionModel.Purchase(panel, row.Name, option)));
            line.AddChild(controls);
            queueColumn.AddChild(line);
        }

        // Buildable list, grouped by category
        var buildables = CityProductionModel.Buildables(panel, list, gold);
        foreach (string category in CityProductionModel.Categories)
        {
            var group = buildables.Where(it => it.Category == category).ToList();
            if (group.Count == 0) continue;
            buildColumn.AddChild(Heading(CityProductionModel.CategoryHeading(category)));
            foreach (var row in group)
            {
                buildableTexts.Add(row.Label);
                var line = new HBoxContainer { Name = "Buildable_" + row.Name };
                var add = MakeButton("Build_" + row.Name, row.Name, row.CanQueue, row.CanQueue ? row.Tooltip : row.QueueWhy + "\n" + row.Tooltip, CityProductionModel.Enqueue(panel.CityId, row));
                add.CustomMinimumSize = new Vector2(theme.Px(70), 0f);
                add.ClipText = true;
                add.Alignment = HorizontalAlignment.Left;
                line.AddChild(add);
                var detail = new Label { Name = "Detail", Text = row.Detail.TrText(), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(theme.Px(75), 0f), TooltipText = row.Tooltip.TrText(), MouseFilter = MouseFilterEnum.Stop };
                detail.AddThemeFontSizeOverride("font_size", theme.Px(10));
                line.AddChild(detail);
                foreach (var option in row.Buy)
                {
                    var buy = MakeButton($"Buy_{row.Name}_{option.Stat}", option.Label, option.Allowed, option.Allowed ? $"Buy {row.Name} now" : option.Why, CityProductionModel.Purchase(panel, row.Name, option));
                    line.AddChild(buy);
                }
                buildColumn.AddChild(line);
            }
        }

        // Built buildings
        buildingColumn.AddChild(Heading("Buildings"));
        var buildings = CityProductionModel.Buildings(panel, ruleset);
        if (buildings.Count == 0) buildingColumn.AddChild(new Label { Text = "None yet.".Tr() });
        foreach (var row in buildings)
        {
            buildingTexts.Add(row.Label);
            var line = new VBoxContainer { Name = "Built_" + row.Name };
            line.AddChild(Wrapped(row.Label, 120));
            if (row.Kind == "Building")
                line.AddChild(MakeButton("Sell_" + row.Name, "Sell", row.CanSell, row.CanSell ? $"Sell for {row.SellPrice} gold (one per turn)" : CityProductionModel.PuppetWhy, CityProductionModel.Sell(panel.CityId, row)));
            buildingColumn.AddChild(line);
        }

        // The state of the city and the destructive buttons last, away from the ones used all the time
        stateText = CityProductionModel.StateLine(panel);
        if (stateText.Length > 0) buildingColumn.AddChild(Wrapped(stateText, 120, "StateText"));
        foreach (var action in CityProductionModel.StateActions(panel))
            buildingColumn.AddChild(MakeButton("State_" + action.Label, action.Label, true, action.Tooltip, action.Command));
    }

    private Label Wrapped(string text, int width, string name = "") => new()
    {
        Name = name.Length == 0 ? "Text" : name, Text = text.TrText(), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(theme.Px(width), 0f),
    };

    private Button MakeButton(string name, string text, bool enabled, string tooltip, GameCommand command)
    {
        var button = new Button { Name = name, Text = text.TrText(), Disabled = !enabled, TooltipText = tooltip.TrText() };
        button.AddThemeFontSizeOverride("font_size", theme.Px(11));
        button.Pressed += () => Send(command);
        buttons[name] = button;
        return button;
    }

    private async void Send(GameCommand command)
    {
        try
        {
            await send(command);
        }
        catch (Exception e)
        {
            GD.PushError("Session call failed: " + e);
            report("Connection problem: " + e.Message);
        }
    }

    private Label Heading(string text)
    {
        var label = new Label { Text = text.TrText() };
        label.AddThemeFontSizeOverride("font_size", theme.Px(14));
        return label;
    }

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
