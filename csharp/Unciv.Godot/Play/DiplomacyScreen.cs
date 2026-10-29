using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Ui;
using Unciv.Queries.Empire;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C021: the diplomacy screen. A list of the civs the viewer has met (<see cref="DiplomacyScreenModel.Rows"/>, from the viewer's own snapshot) and, for the chosen
/// civ, the engine's panel (status, attitude or influence, modifiers), its city-state quests, and the buttons the engine says are legal now (declare war, denounce,
/// offer peace; for a city-state gifts, bullying, protection, peace). Destructive buttons need a second press. Everything shown comes from the <see cref="IGameSession"/>;
/// the only thing that changes the game is a command submitted through it.
/// </summary>
public partial class DiplomacyScreen : PanelContainer
{
    private readonly IGameSession session;
    private readonly GameQueries queries;
    private readonly UiTheme theme;
    private readonly Dictionary<string, ListRow> civButtons = [];
    private readonly Dictionary<string, Button> actionButtons = [];
    private ScreenFrame frame = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Label message = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private VBoxContainer civList = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Label title = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Label status = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private PanelContainer statusPill = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private TextureRect flag = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Label info = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Label modifiers = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Label quests = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private VBoxContainer detail = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Container actions = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private SectionHeader modifierHeader = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private SectionHeader questHeader = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private SectionHeader actionHeader = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private EmptyState? empty;
    private IReadOnlyList<DiplomacyButton> buttons = [];
    private string messageText = "";
    private string? armed;
    private int generation;

    public DiplomacyScreen(IGameSession gameSession, UiTheme uiTheme)
    {
        session = gameSession;
        queries = new GameQueries(gameSession);
        theme = uiTheme;
        Name = "DiplomacyPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        Theme = uiTheme.Theme;
        BuildFrame();
    }

    public override void _EnterTree() => theme.LayoutChanged += OnLayoutChanged;

    public override void _ExitTree() => theme.LayoutChanged -= OnLayoutChanged;

    /// <summary>Sizes are fixed when a view is built, so a change of scale or breakpoint builds the frame again and asks the engine again while the screen is open.</summary>
    private void OnLayoutChanged()
    {
        BuildFrame();
        if (Visible) Fire(OpenAsync());
    }

    private void BuildFrame()
    {
        if (frame != null)
        {
            RemoveChild(frame); // at once: a queued free would leave the old frame in the layout until the frame ends
            frame.QueueFree();
        }
        civButtons.Clear(); // the controls of the old frame are gone; the next open fills them again
        actionButtons.Clear();
        empty = null;
        frame = new ScreenFrame(theme, "Diplomacy", closeLabel: "Close diplomacy");
        frame.CloseRequested += Close;
        AddChild(frame);
        int gap = theme.Px(UiTokens.S2);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", gap);
        message = new Label { Name = "Message", ThemeTypeVariation = UiVariants.LabelSecondary, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, Text = messageText, Visible = messageText.Length > 0 };
        column.AddChild(message);
        var middle = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        middle.AddThemeConstantOverride("separation", theme.Px(UiTokens.S4));
        column.AddChild(middle);

        var listScroll = new ScrollContainer { CustomMinimumSize = new Vector2(theme.Px(theme.Breakpoint == UiBreakpoint.Compact ? 190 : 240), 0f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        civList = new VBoxContainer { Name = "CivList", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        civList.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        listScroll.AddChild(civList);
        middle.AddChild(listScroll);

        var detailScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        detail = new VBoxContainer { Name = "Detail", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        detail.AddThemeConstantOverride("separation", gap);
        detailScroll.AddChild(detail);
        middle.AddChild(detailScroll);

        var head = new HBoxContainer { Name = "DetailHead" };
        head.AddThemeConstantOverride("separation", gap);
        flag = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            CustomMinimumSize = new Vector2(theme.Px(UiTokens.S8), theme.Px(UiTokens.S8)),
            Visible = false,
        };
        head.AddChild(flag);
        title = new Label { Name = "Title", ThemeTypeVariation = UiVariants.LabelHeading, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        head.AddChild(title);
        status = new Label { Name = "StatusText", HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = UiVariants.LabelSmall };
        statusPill = new PanelContainer { Name = "StatusPill", Visible = false, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        statusPill.AddChild(status);
        head.AddChild(statusPill);
        detail.AddChild(head);
        info = Wrapped("Info", UiVariants.LabelBodyStrong);
        detail.AddChild(info);
        modifierHeader = new SectionHeader("Modifiers") { Visible = false };
        detail.AddChild(modifierHeader);
        modifiers = Wrapped("Modifiers", UiVariants.LabelSecondary);
        detail.AddChild(modifiers);
        questHeader = new SectionHeader("Quests") { Visible = false };
        detail.AddChild(questHeader);
        quests = Wrapped("Quests", UiVariants.LabelSecondary);
        detail.AddChild(quests);
        actionHeader = new SectionHeader("Actions") { Visible = false };
        detail.AddChild(actionHeader);
        actions = new HFlowContainer { Name = "Actions", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        actions.AddThemeConstantOverride("h_separation", gap);
        actions.AddThemeConstantOverride("v_separation", gap);
        detail.AddChild(actions);
        frame.SetContent(column);
    }

    private static Label Wrapped(string name, string variation) =>
        new() { Name = name, ThemeTypeVariation = variation, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(100f, 0f), SizeFlagsHorizontal = SizeFlags.ExpandFill };

    /// <summary>The frame of the screen (title, close button).</summary>
    public ScreenFrame Frame => frame;

    /// <summary>The status pill beside the title (red at war), or hidden while no civ is shown.</summary>
    public Control StatusPill => statusPill;

    public string StatusText => status.Text;

    /// <summary>The empty state shown when there is no civ to show, or null.</summary>
    public EmptyState? Empty => empty;

    /// <summary>The detail column (the harness checks that it fits the screen).</summary>
    public Control DetailColumn => detail;

    /// <summary>The cells of the action area, in order: each a column of the button and, when it is disabled, the reason under it.</summary>
    public IReadOnlyList<Control> ActionCells => actions.GetChildren().OfType<Control>().ToList();

    /// <summary>The civs on the list (the met ones).</summary>
    public IReadOnlyList<DiplomacyRow> Rows { get; private set; } = [];

    public string? SelectedCivId { get; private set; }

    /// <summary>The engine's answers for the chosen civ; null while loading or when the engine refused (an unmet civ is never asked for).</summary>
    public DiplomacyPanelResult? Panel { get; private set; }

    public DiplomacyActionsResult? Actions { get; private set; }

    public CityStateQuestsResult? QuestList { get; private set; }

    public IReadOnlyList<DiplomacyButton> Buttons => buttons;

    public string TitleText => title.Text;

    public string InfoText => info.Text;

    public string ModifierText => modifiers.Text;

    public string QuestText => quests.Text;

    public string MessageText => messageText;

    /// <summary>The button that is armed for confirmation (its next press sends), or null.</summary>
    public string? ArmedButton => armed;

    /// <summary>Raised after the engine accepted a command made here, so the owner refreshes the world.</summary>
    public event Action? CommandAccepted;

    public event Action? Closed;

    public ListRow? CivButton(string civId) => civButtons.GetValueOrDefault(civId);

    public Button? ActionButton(string id) => actionButtons.GetValueOrDefault(id);

    /// <summary>Shows the screen with the met civs of the current snapshot, keeping the chosen civ if it is still met, else the first.</summary>
    public async Task OpenAsync(string? civId = null)
    {
        Visible = true;
        RebuildList();
        string? target = civId ?? SelectedCivId;
        if (target == null || Rows.All(it => it.CivId != target)) target = Rows.Count > 0 ? Rows[0].CivId : null;
        await SelectAsync(target);
    }

    public void Close()
    {
        generation++;
        Visible = false;
        armed = null;
        Closed?.Invoke();
    }

    /// <summary>Chooses a civ of the list and asks the engine about it.</summary>
    public async Task SelectAsync(string? civId)
    {
        int ticket = ++generation;
        armed = null;
        SelectedCivId = civId;
        Panel = null;
        Actions = null;
        QuestList = null;
        var row = Rows.FirstOrDefault(it => it.CivId == civId);
        if (row == null)
        {
            ShowNothing(Rows.Count == 0 ? "You have not met anyone yet." : "");
            return;
        }
        var panel = await queries.DiplomacyPanel(row.Name);
        var legal = await queries.DiplomacyActions(row.Name);
        var questList = row.IsCityState ? await queries.CityStateQuests(row.Name) : null;
        if (ticket != generation) return; // a newer choice or a close superseded this answer
        if (panel == null || legal == null)
        {
            ShowNothing("The engine would not show this civ.");
            return;
        }
        Panel = panel;
        Actions = legal;
        QuestList = questList;
        Rebuild(row, panel, legal, questList);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void RebuildList()
    {
        Rows = DiplomacyScreenModel.Rows(session.Snapshot);
        Clear(civList);
        civButtons.Clear();
        var group = new ButtonGroup { AllowUnpress = false };
        foreach (var row in Rows)
        {
            var button = new ListRow(theme, row.Name, row.IsCityState ? "City-state".TrText() + " - " + row.Status.TrText() : row.Status.TrText(), icon: row.Icon, group: group) { Name = "Civ_" + row.CivId, TooltipText = row.Label.TrText() };
            string id = row.CivId;
            button.Pressed += () =>
            {
                if (id != SelectedCivId) Fire(SelectAsync(id)); // choosing the civ already shown must not disarm a confirm step
            };
            civButtons[row.CivId] = button;
            civList.AddChild(button);
        }
    }

    private void ShowNothing(string text)
    {
        title.Text = "";
        flag.Visible = false;
        statusPill.Visible = false;
        info.Text = "";
        modifiers.Text = "";
        quests.Text = "";
        modifierHeader.Visible = false;
        questHeader.Visible = false;
        actionHeader.Visible = false;
        buttons = [];
        Clear(actions);
        actionButtons.Clear();
        DropEmpty();
        if (text.Length > 0)
        {
            empty = new EmptyState(theme, AssetKey.Of(RulesetAssetKeys.UiIcon, "Diplomacy"), text) { Name = "Empty" };
            detail.AddChild(empty);
        }
        foreach (var (id, button) in civButtons) button.SetPressedNoSignal(id == SelectedCivId);
        SetMessage("");
    }

    private void DropEmpty()
    {
        if (empty == null) return;
        detail.RemoveChild(empty);
        empty.QueueFree();
        empty = null;
    }

    private void Rebuild(DiplomacyRow row, DiplomacyPanelResult panel, DiplomacyActionsResult legal, CityStateQuestsResult? questList)
    {
        foreach (var (id, button) in civButtons) button.SetPressedNoSignal(id == row.CivId);
        DropEmpty();
        title.Text = DiplomacyScreenModel.Title(panel).TrText();
        flag.Texture = theme.Icon(row.Icon);
        flag.Visible = true;
        status.Text = panel.Status.TrText();
        statusPill.ThemeTypeVariation = panel.Status == DiplomacyScreenModel.WarStatus ? UiVariants.PanelBadge : UiVariants.PanelBadgeAccent;
        status.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(panel.Status == DiplomacyScreenModel.WarStatus ? UiTokens.TextPrimary : UiTokens.OnAccent));
        statusPill.Visible = true;
        info.Text = string.Join("\n", DiplomacyScreenModel.InfoLines(panel).Select(it => it.TrText()));
        var modifierLines = DiplomacyScreenModel.ModifierLines(panel);
        modifiers.Text = string.Join("\n", modifierLines.Select(it => it.TrText()));
        modifierHeader.Visible = modifierLines.Count > 0;
        modifiers.Visible = modifierLines.Count > 0;
        quests.Text = questList == null ? "" : string.Join("\n", DiplomacyScreenModel.QuestLines(questList).Select(it => it.TrText()));
        questHeader.Visible = questList != null;
        quests.Visible = questList != null;
        Clear(actions);
        actionButtons.Clear();
        buttons = DiplomacyScreenModel.Buttons(row, panel, legal);
        actionHeader.Visible = buttons.Count > 0;
        foreach (var entry in buttons)
        {
            var button = new Button
            {
                Name = entry.Id,
                Text = entry.Label.TrText(),
                Disabled = !entry.Enabled,
                TooltipText = entry.Enabled ? "" : entry.Hint.TrText(),
                ThemeTypeVariation = entry.Danger ? UiVariants.ButtonDanger : UiVariants.ButtonSecondary,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            var chosen = entry;
            button.Pressed += () => OnPressed(chosen);
            actionButtons[entry.Id] = button;
            var cell = new VBoxContainer { Name = "Cell_" + entry.Id, CustomMinimumSize = new Vector2(theme.Px(150), 0f) };
            cell.AddThemeConstantOverride("separation", 0);
            cell.AddChild(button);
            if (!entry.Enabled)
            {
                // the reason a button is off is shown, not only hovered (design 4: disabled-with-reason)
                cell.AddChild(new Label { Name = "Reason_" + entry.Id, Text = entry.Hint.TrText(), ThemeTypeVariation = UiVariants.LabelSmall, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(100f, 0f) });
            }
            actions.AddChild(cell);
        }
        SetMessage("");
    }

    private void OnPressed(DiplomacyButton entry)
    {
        if (entry.NeedsConfirm && armed != entry.Id)
        {
            if (armed != null && actionButtons.TryGetValue(armed, out var previous)) previous.Text = buttons.First(it => it.Id == armed).Label.TrText();
            armed = entry.Id;
            actionButtons[entry.Id].Text = entry.ConfirmLabel.TrText();
            SetMessage("Press again to confirm.");
            return;
        }
        armed = null;
        Fire(Send(entry.Command));
    }

    private async Task Send(GameCommand command)
    {
        var result = await session.SubmitAsync(command);
        if (!result.Accepted)
        {
            await SelectAsync(SelectedCivId);
            SetMessage("Refused: " + result.Message);
            return;
        }
        CommandAccepted?.Invoke();
        RebuildList();
        await SelectAsync(SelectedCivId);
    }

    /// <summary>Observes a UI-triggered Task: a transport failure is shown instead of vanishing (IGameSession error contract).</summary>
    private async void Fire(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            GD.PushError("Session call failed: " + e);
            SetMessage("Connection problem: " + e.Message);
        }
    }

    private static void Clear(Control box)
    {
        foreach (var child in box.GetChildren())
        {
            box.RemoveChild(child); // at once: a queued free would leave the old rows in the layout until the frame ends
            child.QueueFree();
        }
    }

    private void SetMessage(string text)
    {
        messageText = text.TrText();
        message.Text = messageText;
        message.Visible = messageText.Length > 0;
    }
}
