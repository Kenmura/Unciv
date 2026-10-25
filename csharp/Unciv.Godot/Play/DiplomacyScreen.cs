using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Ui;
using Unciv.Queries.Empire;
using Unciv.Models.Translations;
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
    private readonly Label message = new() { Name = "Message" };
    private readonly VBoxContainer civList = new() { Name = "CivList" };
    private readonly Label title = new() { Name = "Title" };
    private readonly Label info = new() { Name = "Info", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label modifiers = new() { Name = "Modifiers", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer actions = new() { Name = "Actions" };
    private readonly Label quests = new() { Name = "Quests", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Dictionary<string, Button> civButtons = [];
    private readonly Dictionary<string, Button> actionButtons = [];
    private IReadOnlyList<DiplomacyButton> buttons = [];
    private string? armed;
    private int generation;

    public DiplomacyScreen(IGameSession gameSession, UiTheme uiTheme)
    {
        session = gameSession;
        queries = new GameQueries(gameSession);
        Name = "DiplomacyPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        Theme = uiTheme.Theme;

        var column = new VBoxContainer();
        AddChild(column);
        var head = new HBoxContainer();
        column.AddChild(head);
        var heading = new Label { Text = "Diplomacy".Tr(), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddThemeFontSizeOverride("font_size", uiTheme.Px(20));
        head.AddChild(heading);
        message.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        message.ClipText = true;
        message.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(message);
        var close = new Button { Name = "DiplomacyClose", Text = "Close diplomacy".Tr() };
        close.Pressed += Close;
        head.AddChild(close);

        var middle = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(middle);
        civList.CustomMinimumSize = new Vector2(uiTheme.Px(220), 0f);
        civList.AddThemeConstantOverride("separation", 2);
        middle.AddChild(civList);
        var detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        middle.AddChild(detail);
        title.AddThemeFontSizeOverride("font_size", uiTheme.Px(18));
        detail.AddChild(title);
        detail.AddChild(info);
        detail.AddChild(modifiers);
        actions.AddThemeConstantOverride("separation", 2);
        detail.AddChild(actions);
        detail.AddChild(quests);
    }

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

    public string MessageText => message.Text;

    /// <summary>The button that is armed for confirmation (its next press sends), or null.</summary>
    public string? ArmedButton => armed;

    /// <summary>Raised after the engine accepted a command made here, so the owner refreshes the world.</summary>
    public event Action? CommandAccepted;

    public event Action? Closed;

    public Button? CivButton(string civId) => civButtons.GetValueOrDefault(civId);

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
        foreach (var row in Rows)
        {
            var button = new Button { Name = "Civ_" + row.CivId, Text = row.Label.TrText(), Alignment = HorizontalAlignment.Left, ToggleMode = true };
            string id = row.CivId;
            button.Pressed += () => Fire(SelectAsync(id));
            civButtons[row.CivId] = button;
            civList.AddChild(button);
        }
    }

    private void ShowNothing(string text)
    {
        title.Text = "";
        info.Text = text.TrText();
        modifiers.Text = "";
        quests.Text = "";
        buttons = [];
        Clear(actions);
        actionButtons.Clear();
        foreach (var (id, button) in civButtons) button.SetPressedNoSignal(id == SelectedCivId);
        SetMessage("");
    }

    private void Rebuild(DiplomacyRow row, DiplomacyPanelResult panel, DiplomacyActionsResult legal, CityStateQuestsResult? questList)
    {
        foreach (var (id, button) in civButtons) button.SetPressedNoSignal(id == row.CivId);
        title.Text = DiplomacyScreenModel.Title(panel).TrText();
        info.Text = string.Join("\n", DiplomacyScreenModel.InfoLines(panel).Select(it => it.TrText()));
        modifiers.Text = string.Join("\n", DiplomacyScreenModel.ModifierLines(panel).Select(it => it.TrText()));
        quests.Text = questList == null ? "" : "Quests".Tr() + "\n" + string.Join("\n", DiplomacyScreenModel.QuestLines(questList).Select(it => it.TrText()));
        Clear(actions);
        actionButtons.Clear();
        buttons = DiplomacyScreenModel.Buttons(row, panel, legal);
        foreach (var entry in buttons)
        {
            var button = new Button { Name = entry.Id, Text = entry.Label.TrText(), Disabled = !entry.Enabled, TooltipText = entry.Enabled ? "" : entry.Hint.TrText(), Alignment = HorizontalAlignment.Left };
            var chosen = entry;
            button.Pressed += () => OnPressed(chosen);
            actionButtons[entry.Id] = button;
            actions.AddChild(button);
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

    private void SetMessage(string text) => message.Text = text.TrText();
}
