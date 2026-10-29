using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.GodotClient.Assets;
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
/// C069: a queue entry shows its icon, name, turns and a progress bar, and moves by its buttons, by Ctrl+Up / Ctrl+Down while it has the focus, or by dragging it onto another
/// entry; the buildable list is <see cref="ListRow"/>s; Raze is a Danger button that asks for confirmation first.
/// </summary>
public partial class CityProductionView : HBoxContainer
{
    private readonly Func<GameCommand, Task<bool>> send;
    private readonly Action<string> report;
    private readonly Action<CityStateAction> confirm;
    private readonly UiTheme theme;
    private readonly VBoxContainer queueColumn = new() { Name = "QueueColumn" };
    private readonly VBoxContainer buildColumn = new() { Name = "BuildColumn" };
    private readonly VBoxContainer buildingColumn = new() { Name = "BuildingColumn" };
    private readonly Dictionary<string, Button> buttons = [];
    private readonly List<string> queueTexts = [];
    private readonly List<string> buildableTexts = [];
    private readonly List<string> buildingTexts = [];
    private readonly List<QueueEntryRow> queueRows = [];
    private CityPanelResult? shown;
    private int refocus = -1;
    private string stateText = "";

    public CityProductionView(Func<GameCommand, Task<bool>> sendCommand, Action<string> reportMessage, Action<CityStateAction> confirmAction, UiTheme uiTheme)
    {
        send = sendCommand;
        report = reportMessage;
        confirm = confirmAction;
        theme = uiTheme;
        Name = "Production";
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S3));
        queueColumn.CustomMinimumSize = new Vector2(uiTheme.Px(245), 0f);
        queueColumn.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        buildingColumn.CustomMinimumSize = new Vector2(uiTheme.Px(uiTheme.MinTargetPx > 0 ? 150 : 105), 0f); // T286: a phone gives the building names room to wrap on fewer lines
        buildingColumn.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        buildColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(Scroll("QueueScroll", queueColumn, expand: false));
        AddChild(Scroll("BuildScroll", buildColumn, expand: true));
        AddChild(Scroll("BuildingScroll", buildingColumn, expand: false));
    }

    private static ScrollContainer Scroll(string name, Control content, bool expand)
    {
        var scroll = new ScrollContainer { Name = name, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        if (expand) scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(content);
        return scroll;
    }

    /// <summary>The queue lines, the buildable lines, the built building lines and the state line, as shown.</summary>
    public IReadOnlyList<string> QueueTexts => queueTexts;

    public IReadOnlyList<string> BuildableTexts => buildableTexts;

    public IReadOnlyList<string> BuildingTexts => buildingTexts;

    public string StateText => stateText;

    /// <summary>The queue entries in order (the harness drives the keyboard move and the drop through them).</summary>
    public IReadOnlyList<Control> QueueEntries => queueRows;

    /// <summary>A button by name: <c>Up_i</c>, <c>Down_i</c>, <c>Top_i</c>, <c>Remove_i</c> (queue entry), <c>Build_name</c>, <c>Buy_name_Stat</c>, <c>Sell_name</c>, <c>State_label</c>.</summary>
    public Button? ButtonNamed(string name) => buttons.GetValueOrDefault(name);

    /// <summary>
    /// Moves queue entry <paramref name="from"/> to place <paramref name="to"/> (what a drop on another entry does); it is a run of the same Up / Down commands the buttons send.
    /// The moved entry keeps the keyboard focus after the screen refreshes when <paramref name="keepFocus"/>.
    /// </summary>
    public void MoveEntry(int from, int to, bool keepFocus = false)
    {
        if (shown == null) return;
        var commands = CityProductionModel.Drag(shown, from, to);
        if (commands.Count == 0) return;
        if (keepFocus) refocus = to;
        Run(commands);
    }

    /// <summary>Forgets a pending focus request (the city closed).</summary>
    public void ClearPendingFocus() => refocus = -1;

    public void Show(CityPanelResult panel, ConstructionListResult list, int gold, Ruleset ruleset)
    {
        shown = panel;
        Clear(queueColumn);
        Clear(buildColumn);
        Clear(buildingColumn);
        buttons.Clear();
        queueTexts.Clear();
        buildableTexts.Clear();
        buildingTexts.Clear();
        queueRows.Clear();

        queueColumn.AddChild(new SectionHeader("Production queue"));
        var queue = CityProductionModel.Queue(panel, list, gold);
        if (queue.Count == 0) queueColumn.AddChild(new Label { Text = "The queue is empty.".Tr(), ThemeTypeVariation = UiVariants.LabelSecondary });
        foreach (var row in queue)
        {
            queueTexts.Add(row.Label);
            var entry = QueueRow(panel, row);
            queueRows.Add(entry);
            queueColumn.AddChild(entry);
        }
        if (refocus >= 0 && refocus < queueRows.Count) queueRows[refocus].CallDeferred(Control.MethodName.GrabFocus);
        refocus = -1;

        // Buildable list, grouped by category
        var buildables = CityProductionModel.Buildables(panel, list, gold);
        foreach (string category in CityProductionModel.Categories)
        {
            var group = buildables.Where(it => it.Category == category).ToList();
            if (group.Count == 0) continue;
            buildColumn.AddChild(new SectionHeader(CityProductionModel.CategoryHeading(category)));
            foreach (var row in group)
            {
                buildableTexts.Add(row.Label);
                var line = new HBoxContainer { Name = "Buildable_" + row.Name };
                string subtitle = row.Cost == null ? "perpetual" : CityProductionModel.TurnsText(row.Turns);
                var add = new ListRow(theme, row.Name, subtitle, row.Cost?.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Icon, row.CanQueue ? null : row.QueueWhy)
                {
                    Name = "Build_" + row.Name,
                    ToggleMode = false,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    TooltipText = (row.CanQueue ? row.Tooltip : row.QueueWhy + "\n" + row.Tooltip).TrText(),
                };
                var command = CityProductionModel.Enqueue(panel.CityId, row);
                add.Pressed += () => Send(command);
                buttons[add.Name] = add;
                line.AddChild(add);
                foreach (var option in row.Buy)
                    line.AddChild(BuyChip($"Buy_{row.Name}_{option.Stat}", option, row.Name, panel));
                buildColumn.AddChild(line);
            }
        }

        // Built buildings
        buildingColumn.AddChild(new SectionHeader("Buildings"));
        var buildings = CityProductionModel.Buildings(panel, ruleset);
        if (buildings.Count == 0) buildingColumn.AddChild(new Label { Text = "None yet.".Tr(), ThemeTypeVariation = UiVariants.LabelSecondary });
        foreach (var row in buildings)
        {
            buildingTexts.Add(row.Label);
            var line = new VBoxContainer { Name = "Built_" + row.Name };
            var top = new HBoxContainer();
            top.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
            top.AddChild(Icon(row.Icon, 20));
            top.AddChild(Wrapped(row.Label, theme.MinTargetPx > 0 ? 115 : 70));
            line.AddChild(top);
            if (row.Kind == "Building")
                line.AddChild(MakeButton("Sell_" + row.Name, "Sell", row.CanSell, row.CanSell ? $"Sell for {row.SellPrice} gold (one per turn)" : CityProductionModel.PuppetWhy, CityProductionModel.Sell(panel.CityId, row), UiVariants.ButtonGhost));
            buildingColumn.AddChild(line);
        }

        // The state of the city and the destructive buttons last, away from the ones used all the time
        stateText = CityProductionModel.StateLine(panel);
        if (stateText.Length > 0)
        {
            var state = Wrapped(stateText, 100, "StateText");
            state.ThemeTypeVariation = UiVariants.LabelSmall;
            buildingColumn.AddChild(state);
        }
        foreach (var action in CityProductionModel.StateActions(panel))
        {
            var chosen = action;
            buildingColumn.AddChild(MakeButton("State_" + action.Label, action.Label, true, action.Tooltip, action.Command, action.Dangerous ? UiVariants.ButtonDanger : UiVariants.ButtonSecondary,
                action.Dangerous ? () => confirm(chosen) : null));
        }
    }

    private QueueEntryRow QueueRow(CityPanelResult panel, QueueRow row)
    {
        var entry = new QueueEntryRow(theme, row.Index, row.CanRaise || row.CanLower, MoveEntry) { Name = "QueueRow_" + row.Index };
        var line = new VBoxContainer();
        line.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        head.AddChild(Icon(row.Icon, 24));
        var texts = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        texts.AddThemeConstantOverride("separation", 0);
        texts.AddChild(new Label { Name = "Text", Text = $"{row.Index + 1}. {row.Name}".TrText(), ThemeTypeVariation = UiVariants.LabelBodyStrong, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore });
        texts.AddChild(new Label { Name = "Detail", Text = row.Detail.TrText(), ThemeTypeVariation = UiVariants.LabelSmall, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore });
        head.AddChild(texts);
        foreach (var option in row.Buy) head.AddChild(BuyChip($"QBuy_{row.Index}_{option.Stat}", option, row.Name, panel));
        line.AddChild(head);
        if (row.Cost != null && row.WorkDone > 0) line.AddChild(new StatBar(theme, "Production", row.WorkDone, row.Cost.Value));
        var controls = new HFlowContainer();
        controls.AddThemeConstantOverride("h_separation", theme.Px(UiTokens.S1));
        controls.AddChild(MakeButton($"Top_{row.Index}", "Top", row.CanTop, "Move to the top", CityProductionModel.Reorder(panel.CityId, row, QueueMove.Top), UiVariants.ButtonGhost));
        controls.AddChild(MakeButton($"Up_{row.Index}", "Up", row.CanRaise, "Raise priority (Ctrl+Up)", CityProductionModel.Reorder(panel.CityId, row, QueueMove.Up), UiVariants.ButtonGhost));
        controls.AddChild(MakeButton($"Down_{row.Index}", "Down", row.CanLower, "Lower priority (Ctrl+Down)", CityProductionModel.Reorder(panel.CityId, row, QueueMove.Down), UiVariants.ButtonGhost));
        var remove = new IconButton(theme, AssetKey.Of(RulesetAssetKeys.UiIcon, "Close"), "Remove from the queue") { Name = $"Remove_{row.Index}", Disabled = !row.CanRemove };
        var removeCommand = CityProductionModel.Remove(panel.CityId, row);
        remove.Pressed += () => Send(removeCommand);
        buttons[remove.Name] = remove;
        controls.AddChild(remove);
        line.AddChild(controls);
        entry.AddChild(line);
        return entry;
    }

    /// <summary>A purchase as a chip: the currency's icon and the price; the tooltip says what it buys, or why it is disabled.</summary>
    private Button BuyChip(string name, BuyOption option, string construction, CityPanelResult panel)
    {
        var chip = MakeButton(name, option.Cost.ToString(System.Globalization.CultureInfo.InvariantCulture), option.Allowed, option.Allowed ? $"{option.Label}: {construction}" : option.Why, CityProductionModel.Purchase(panel, construction, option), UiVariants.ButtonChip);
        chip.Icon = theme.Icon(AssetKey.Of(RulesetAssetKeys.StatIcon, option.Stat));
        chip.AddThemeConstantOverride("icon_max_width", theme.Px(UiTheme.IconSize));
        return chip;
    }

    private TextureRect Icon(AssetKey key, int size) => new()
    {
        Texture = theme.Icon(key),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        TextureFilter = TextureFilterEnum.LinearWithMipmaps,
        CustomMinimumSize = new Vector2(theme.Px(size), theme.Px(size)),
        SizeFlagsVertical = SizeFlags.ShrinkCenter,
        MouseFilter = MouseFilterEnum.Ignore,
    };

    private Label Wrapped(string text, int width, string name = "") => new()
    {
        Name = name.Length == 0 ? "Text" : name, Text = text.TrText(), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(theme.Px(width), 0f),
    };

    private Button MakeButton(string name, string text, bool enabled, string tooltip, GameCommand command, string variation, Action? press = null)
    {
        var button = new Button { Name = name, Text = text.TrText(), Disabled = !enabled, TooltipText = tooltip.TrText(), ThemeTypeVariation = variation };
        button.Pressed += press ?? (() => Send(command));
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

    private async void Run(IReadOnlyList<GameCommand> commands)
    {
        try
        {
            // a later step names an index that only holds if the earlier ones moved the entry, so the run stops at the first refusal
            foreach (var command in commands)
            {
                if (await send(command)) continue;
                refocus = -1;
                return;
            }
        }
        catch (Exception e)
        {
            refocus = -1;
            GD.PushError("Session call failed: " + e);
            report("Connection problem: " + e.Message);
        }
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

/// <summary>C069: a queue entry as a card that can be dragged onto another entry and, with the keyboard focus, moved by Ctrl+Up / Ctrl+Down.</summary>
public partial class QueueEntryRow : PanelContainer
{
    private readonly UiTheme theme;
    private readonly int index;
    private readonly bool movable;
    private readonly Action<int, int, bool> move;

    public QueueEntryRow(UiTheme uiTheme, int entryIndex, bool canMove, Action<int, int, bool> moveEntry)
    {
        theme = uiTheme;
        index = entryIndex;
        movable = canMove;
        move = moveEntry;
        ThemeTypeVariation = index % 2 == 0 ? UiVariants.PanelCellEven : UiVariants.PanelCellOdd;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public int Index => index;

    /// <summary>What Ctrl+Up (-1) and Ctrl+Down (+1) do.</summary>
    public void Nudge(int delta) => move(index, index + delta, true);

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, CtrlPressed: true, Keycode: Key.Up or Key.Down } key)
        {
            Nudge(key.Keycode == Key.Up ? -1 : 1);
            AcceptEvent();
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!movable) return default;
        SetDragPreview(new Label { Text = $"{index + 1}", ThemeTypeVariation = UiVariants.LabelHeading });
        return index;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => data.VariantType == Variant.Type.Int && (int)data != index;

    public override void _DropData(Vector2 atPosition, Variant data) => move((int)data, index, false);

    public override void _Draw()
    {
        if (HasFocus()) DrawRect(new Rect2(Vector2.Zero, Size), ProceduralAssets.ToColor(UiTokens.Accent), false, theme.Px(UiTokens.RingWidth));
    }
}
