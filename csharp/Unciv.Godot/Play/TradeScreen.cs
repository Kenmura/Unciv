using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.Net.Visibility;
using Unciv.Queries.Empire;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C022: the trade screen. A list of the major civs the viewer has met; for the chosen one two columns of everything the engine says each side could offer
/// (<see cref="TradeOptionsResult"/>), a staged trade built from them (<see cref="TradeBuilder"/>), the AI's likely answer when the game shows hints, and
/// Propose / Retract. Trades and demands other civs sent the viewer wait at the top with Accept / Decline / "How about something else". Everything shown
/// comes from the <see cref="IGameSession"/>; the only thing that changes the game is a command submitted through it, and the engine decides the answer
/// (an AI answers on its own turn).
/// </summary>
public partial class TradeScreen : PanelContainer
{
    private readonly IGameSession session;
    private readonly GameQueries queries;
    private readonly Label message = new() { Name = "Message" };
    private readonly VBoxContainer partnerList = new() { Name = "PartnerList" };
    private readonly VBoxContainer incoming = new() { Name = "Incoming" };
    private readonly Label title = new() { Name = "Title" };
    private readonly VBoxContainer giveColumn = new() { Name = "GiveColumn" };
    private readonly VBoxContainer askColumn = new() { Name = "AskColumn" };
    private readonly Label summary = new() { Name = "Summary", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(100f, 0f) };
    private readonly Label hint = new() { Name = "Hint" };
    private readonly Label pending = new() { Name = "Pending", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(100f, 0f) };
    private readonly Button propose = new() { Name = "Propose", Text = "Propose trade".Tr() };
    private readonly Button clear = new() { Name = "ClearTrade", Text = "Clear".Tr() };
    private readonly Button retract = new() { Name = "Retract", Text = "Retract offer".Tr() };
    private readonly Dictionary<string, Button> partnerButtons = [];
    private readonly Dictionary<string, Button> controls = [];
    private int generation;
    private int hintRequest;

    public TradeScreen(IGameSession gameSession, UiTheme uiTheme)
    {
        session = gameSession;
        queries = new GameQueries(gameSession);
        Name = "TradePanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        Theme = uiTheme.Theme;

        var column = new VBoxContainer();
        AddChild(column);
        var head = new HBoxContainer();
        column.AddChild(head);
        var heading = new Label { Text = "Trade".Tr(), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddThemeFontSizeOverride("font_size", uiTheme.Px(20));
        head.AddChild(heading);
        message.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        message.ClipText = true;
        message.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(message);
        var close = new Button { Name = "TradeClose", Text = "Close trade".Tr() };
        close.Pressed += Close;
        head.AddChild(close);

        column.AddChild(incoming);
        var middle = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(middle);
        partnerList.CustomMinimumSize = new Vector2(uiTheme.Px(130), 0f);
        partnerList.AddThemeConstantOverride("separation", 2);
        middle.AddChild(partnerList);
        var detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        middle.AddChild(detail);
        title.AddThemeFontSizeOverride("font_size", uiTheme.Px(18));
        detail.AddChild(title);
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        detail.AddChild(columns);
        columns.AddChild(Scrolling("You give".Tr(), giveColumn));
        columns.AddChild(Scrolling("You ask for".Tr(), askColumn));
        detail.AddChild(pending);
        detail.AddChild(summary);
        detail.AddChild(hint);
        var buttons = new HBoxContainer();
        detail.AddChild(buttons);
        propose.Pressed += () => Fire(ProposeAsync());
        clear.Pressed += () =>
        {
            Builder?.Clear();
            Redraw();
        };
        retract.Pressed += () =>
        {
            string partner = SelectedCivId ?? "";
            Retracting?.Invoke(partner);
            Fire(SendAsync(new Commands.Diplomacy.RetractTrade { CivId = partner }));
        };
        buttons.AddChild(propose);
        buttons.AddChild(clear);
        buttons.AddChild(retract);
    }

    private static VBoxContainer Scrolling(string heading, VBoxContainer content)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(100f, 0f) };
        box.AddChild(new Label { Text = heading });
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0f, 70f), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(content);
        box.AddChild(scroll);
        return box;
    }

    /// <summary>The civs one can trade with.</summary>
    public IReadOnlyList<DiplomacyRow> Partners { get; private set; } = [];

    public string? SelectedCivId { get; private set; }

    /// <summary>The offer builder for the chosen civ; null while loading or when the engine would not show the civ.</summary>
    public TradeBuilder? Builder { get; private set; }

    public IReadOnlyList<IncomingTrade> IncomingTrades { get; private set; } = [];

    public IReadOnlyList<IncomingDemand> IncomingDemands { get; private set; } = [];

    public string SummaryText => summary.Text;

    public string HintText => hint.Text;

    public string PendingText => pending.Text;

    public string MessageText => message.Text;

    public string TitleText => title.Text;

    public Button ProposeButton => propose;

    public Button RetractButton => retract;

    public Button ClearButton => clear;

    /// <summary>Raised after the engine accepted a command made here, so the owner refreshes the world.</summary>
    public event Action? CommandAccepted;

    public event Action? Closed;

    /// <summary>C039: the player takes back its offer to this civ (the engine tells only the recipient, so the owner learns of it here).</summary>
    public event Action<string>? Retracting;

    public Button? PartnerButton(string civId) => partnerButtons.GetValueOrDefault(civId);

    /// <summary>A button of the screen by node name: <c>Give_...</c>/<c>Ask_...</c> offers (see <see cref="TradeChoice.Id"/>), <c>Plus_</c>/<c>Minus_</c> of those, <c>Accept_&lt;civ&gt;</c>, <c>Decline_&lt;civ&gt;</c>, <c>Counter_&lt;civ&gt;</c>, <c>DemandAccept_&lt;civ&gt;</c>, <c>DemandRefuse_&lt;civ&gt;</c>.</summary>
    public Button? Control(string id) => controls.GetValueOrDefault(id);

    /// <summary>Shows the screen with the met major civs, on the given civ (the chosen one if still met, else the first).</summary>
    public async Task OpenAsync(string? civId = null, PendingTradeDto? stageFrom = null)
    {
        Visible = true;
        Partners = TradeScreenModel.Partners(session.Snapshot);
        RebuildPartners();
        string? target = civId ?? SelectedCivId;
        if (target == null || Partners.All(it => it.CivId != target)) target = Partners.Count > 0 ? Partners[0].CivId : null;
        await SelectAsync(target, target == stageFrom?.From ? stageFrom : null);
    }

    public void Close()
    {
        generation++;
        Visible = false;
        Closed?.Invoke();
    }

    /// <summary>Chooses a civ of the list, asks the engine what each side could offer and starts an empty trade.</summary>
    public async Task SelectAsync(string? civId, PendingTradeDto? stageFrom = null)
    {
        int ticket = ++generation;
        SelectedCivId = civId;
        Builder = null;
        var row = Partners.FirstOrDefault(it => it.CivId == civId);
        TradeOptionsResult? options = row == null ? null : await queries.TradeOptions(row.Name);
        var decisions = await queries.Pending();
        if (ticket != generation) return; // a newer choice or a close superseded this answer
        IncomingDemands = decisions == null ? [] : TradeScreenModel.Demands(decisions, session.Snapshot);
        if (row != null && options != null)
        {
            Builder = new TradeBuilder(row.CivId, options);
            if (stageFrom != null && Builder.StageRequest(stageFrom) > 0) SetMessage("Some of the offers are no longer possible.".Tr());
        }
        Redraw();
        if (row == null || options == null) SetMessage(Partners.Count == 0 ? "You have not met anyone yet.".Tr() : row == null ? "" : "The engine would not show this civ.".Tr());
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void RebuildPartners()
    {
        Clear(partnerList);
        partnerButtons.Clear();
        foreach (var row in Partners)
        {
            var button = new Button { Name = "Partner_" + row.CivId, Text = row.Label, Alignment = HorizontalAlignment.Left, ToggleMode = true };
            string id = row.CivId;
            button.Pressed += () => Fire(SelectAsync(id));
            partnerButtons[row.CivId] = button;
            partnerList.AddChild(button);
        }
    }

    private string? NameLookup(string type, string name)
    {
        var options = Builder?.Options;
        if (options == null) return null;
        return options.Ours.Concat(options.Theirs).FirstOrDefault(it => it.Type == type && it.Name == name)?.DisplayName;
    }

    private void Redraw()
    {
        controls.Clear();
        foreach (var (id, button) in partnerButtons) button.SetPressedNoSignal(id == SelectedCivId);
        var row = Partners.FirstOrDefault(it => it.CivId == SelectedCivId);
        title.Text = row?.Name ?? "";
        RebuildIncoming();
        Clear(giveColumn);
        Clear(askColumn);
        var builder = Builder;
        if (builder != null)
        {
            Fill(giveColumn, builder, TradeSide.Give);
            Fill(askColumn, builder, TradeSide.Ask);
        }
        var outgoing = SelectedCivId == null ? null : TradeScreenModel.Outgoing(session.Snapshot, SelectedCivId, NameLookup);
        bool waiting = builder != null && (builder.Options.HasPendingOfferFromUs || outgoing != null);
        pending.Text = outgoing == null ? (waiting ? "Your offer waits for their answer".Tr() : "")
            : $"Your offer waits for their answer: you give {Join(outgoing.Gives)}; you ask {Join(outgoing.Asks)}";
        summary.Text = builder == null ? "" : string.Join("\n", builder.Summary());
        propose.Disabled = builder == null || builder.IsEmpty || waiting;
        propose.TooltipText = waiting ? "Retract your offer first".Tr() : "";
        clear.Disabled = builder == null || builder.IsEmpty;
        retract.Visible = waiting;
        hint.Text = "";
        SetMessage("");
        Fire(RefreshHintAsync());
    }

    private static string Join(IReadOnlyList<string> parts) => parts.Count == 0 ? "nothing" : string.Join(", ", parts);

    private void Fill(VBoxContainer column, TradeBuilder builder, TradeSide side)
    {
        foreach (var group in builder.Groups(side))
        {
            var heading = new Label { Text = "- " + group.Title + " -" };
            column.AddChild(heading);
            foreach (var choice in group.Choices)
            {
                var line = new HBoxContainer();
                var offer = new Button
                {
                    Name = choice.Id,
                    Text = choice.Label,
                    Alignment = HorizontalAlignment.Left,
                    ToggleMode = true,
                    ClipText = true, // a long offer must not widen the column past the screen
                    TooltipText = choice.Label,
                    Disabled = !choice.Entry.Tradable,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                };
                offer.SetPressedNoSignal(choice.Staged);
                var chosen = choice;
                offer.Pressed += () =>
                {
                    builder.Toggle(chosen.Side, chosen.Entry.Type, chosen.Entry.Name);
                    Redraw();
                };
                controls[choice.Id] = offer;
                line.AddChild(offer);
                if (choice.Staged && choice.Quantity)
                {
                    AddStep(line, "Minus_" + choice.Id, "-", chosen, -1);
                    AddStep(line, "Plus_" + choice.Id, "+", chosen, 1);
                }
                column.AddChild(line);
            }
        }
        if (builder.Groups(side).Count == 0) column.AddChild(new Label { Text = "Nothing to offer".Tr() });
    }

    private void AddStep(HBoxContainer line, string name, string text, TradeChoice choice, int steps)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += () =>
        {
            Builder?.Adjust(choice.Side, choice.Entry.Type, choice.Entry.Name, steps);
            Redraw();
        };
        controls[name] = button;
        line.AddChild(button);
    }

    private void RebuildIncoming()
    {
        Clear(incoming);
        IncomingTrades = TradeScreenModel.Incoming(session.Snapshot, NameLookup);
        foreach (var trade in IncomingTrades)
        {
            string id = trade.FromId;
            var box = Boxed("Incoming_" + id, $"{trade.Title}: they give {Join(trade.Gives)}; they ask {Join(trade.Asks)}", out var buttons);
            AddControl(buttons, "Accept_" + id, "Sounds good!".Tr(), () => Fire(SendAsync(trade.Accept)));
            AddControl(buttons, "Decline_" + id, "Not this time.".Tr(), () => Fire(SendAsync(trade.Decline)));
            AddControl(buttons, "Counter_" + id, "Counter...".Tr(), () => Fire(CounterAsync(id)));
            incoming.AddChild(box);
        }
        foreach (var demand in IncomingDemands)
        {
            string id = demand.FromId;
            var box = Boxed("Demand_" + id, demand.Title, out var buttons);
            AddControl(buttons, "DemandAccept_" + id, "Very well.".Tr(), () => Fire(SendAsync(demand.Accept)));
            AddControl(buttons, "DemandRefuse_" + id, "Never!".Tr(), () => Fire(SendAsync(demand.Refuse)));
            incoming.AddChild(box);
        }
    }

    // A wrapped text with its answer buttons beside it: a long offer wraps instead of pushing the buttons off the screen.
    private static HBoxContainer Boxed(string name, string text, out HBoxContainer buttons)
    {
        var box = new HBoxContainer { Name = name };
        box.AddChild(new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(100f, 0f), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        buttons = new HBoxContainer();
        box.AddChild(buttons);
        return box;
    }

    private void AddControl(HBoxContainer box, string name, string text, Action pressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += pressed;
        controls[name] = button;
        box.AddChild(button);
    }

    private async Task RefreshHintAsync()
    {
        int ticket = ++hintRequest;
        var builder = Builder;
        var row = Partners.FirstOrDefault(it => it.CivId == SelectedCivId);
        if (builder == null || row == null || builder.IsEmpty) return;
        var answer = await queries.TradeHint(row.Name, builder.HintRequests(TradeSide.Give), builder.HintRequests(TradeSide.Ask));
        if (ticket != hintRequest || builder != Builder) return;
        hint.Text = TradeScreenModel.HintText(answer);
    }

    private async Task ProposeAsync()
    {
        var command = Builder?.Command();
        if (command != null) await SendAsync(command);
    }

    private async Task CounterAsync(string civId)
    {
        var request = session.Snapshot.PendingTrades.FirstOrDefault(it => it.From == civId && it.To == session.Snapshot.You);
        var result = await session.SubmitAsync(new Commands.Diplomacy.DismissTradeRequest { CivId = civId });
        if (!result.Accepted)
        {
            await SelectAsync(SelectedCivId);
            SetMessage("Refused".Tr() + ": " + result.Message.TrText());
            return;
        }
        CommandAccepted?.Invoke();
        await SelectAsync(civId, request);
    }

    private async Task SendAsync(GameCommand command)
    {
        var result = await session.SubmitAsync(command);
        if (!result.Accepted)
        {
            await SelectAsync(SelectedCivId);
            SetMessage("Refused".Tr() + ": " + result.Message.TrText());
            return;
        }
        CommandAccepted?.Invoke();
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

    private void SetMessage(string text) => message.Text = text;
}
