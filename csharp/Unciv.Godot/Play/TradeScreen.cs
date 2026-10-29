using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
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
    private readonly UiTheme theme;
    private readonly Dictionary<string, ListRow> partnerButtons = [];
    private readonly Dictionary<string, Button> controls = [];
    private ScreenFrame frame = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Label message = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private VBoxContainer partnerList = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private VBoxContainer incoming = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private VBoxContainer giveColumn = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private VBoxContainer askColumn = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Label summary = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Label hint = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private PanelContainer hintPill = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Label hintPillText = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Label pending = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Button propose = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Button clear = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private Button retract = null!; // JUSTIFY: C073 assigned by BuildFrame in the constructor
    private string messageText = "";
    private int generation;
    private int hintRequest;

    public TradeScreen(IGameSession gameSession, UiTheme uiTheme)
    {
        session = gameSession;
        queries = new GameQueries(gameSession);
        theme = uiTheme;
        Name = "TradePanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        Theme = uiTheme.Theme;
        BuildFrame();
    }

    public override void _EnterTree() => theme.LayoutChanged += OnLayoutChanged;

    public override void _ExitTree() => theme.LayoutChanged -= OnLayoutChanged;

    /// <summary>Sizes are fixed when a view is built, so a change of scale or breakpoint builds the frame again and fills it from the current state while the screen is open.</summary>
    private void OnLayoutChanged()
    {
        BuildFrame();
        if (!Visible) return;
        RebuildPartners();
        Redraw();
    }

    private void BuildFrame()
    {
        if (frame != null)
        {
            RemoveChild(frame); // at once: a queued free would leave the old frame in the layout until the frame ends
            frame.QueueFree();
        }
        partnerButtons.Clear(); // the controls of the old frame are gone; the next redraw fills them again
        controls.Clear();
        frame = new ScreenFrame(theme, "Trade", closeLabel: "Close trade");
        frame.CloseRequested += Close;
        AddChild(frame);
        int gap = theme.Px(UiTokens.S2);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", gap);
        message = new Label { Name = "Message", ThemeTypeVariation = UiVariants.LabelSecondary, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, Text = messageText, Visible = messageText.Length > 0 };
        column.AddChild(message);
        incoming = new VBoxContainer { Name = "Incoming" };
        incoming.AddThemeConstantOverride("separation", gap);
        // a small window cannot hold the cards and both columns: the body scrolls and the footer actions stay in view
        var body = new ScrollContainer { Name = "Body", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", gap);
        body.AddChild(stack);
        column.AddChild(body);
        stack.AddChild(incoming);
        var middle = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        middle.AddThemeConstantOverride("separation", theme.Px(UiTokens.S3));
        stack.AddChild(middle);

        var partnerScroll = new ScrollContainer { CustomMinimumSize = new Vector2(theme.Px(theme.Breakpoint == UiBreakpoint.Compact ? 150 : 220), 0f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        partnerList = new VBoxContainer { Name = "PartnerList", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        partnerList.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        partnerScroll.AddChild(partnerList);
        middle.AddChild(partnerScroll);

        var detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        detail.AddThemeConstantOverride("separation", gap);
        middle.AddChild(detail);
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", gap);
        detail.AddChild(columns);
        giveColumn = new VBoxContainer { Name = "GiveColumn" };
        askColumn = new VBoxContainer { Name = "AskColumn" };
        columns.AddChild(Scrolling("You give", giveColumn));
        columns.AddChild(Scrolling("You ask for", askColumn));
        pending = new Label { Name = "Pending", ThemeTypeVariation = UiVariants.LabelSecondary, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(100f, 0f) };
        detail.AddChild(pending);
        var verdict = new HBoxContainer { Name = "Verdict" };
        verdict.AddThemeConstantOverride("separation", gap);
        summary = new Label { Name = "Summary", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(100f, 0f), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        verdict.AddChild(summary);
        hintPillText = new Label { Name = "HintBadge", HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = UiVariants.LabelSmall };
        hintPill = new PanelContainer { Name = "HintPill", Visible = false, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        hintPill.AddChild(hintPillText);
        verdict.AddChild(hintPill);
        detail.AddChild(verdict);
        hint = new Label { Name = "Hint", ThemeTypeVariation = UiVariants.LabelSecondary, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(100f, 0f) };
        detail.AddChild(hint);
        frame.SetContent(column);

        propose = new Button { Name = "Propose", Text = "Propose trade".Tr() };
        clear = new Button { Name = "ClearTrade", Text = "Clear".Tr() };
        retract = new Button { Name = "Retract", Text = "Retract offer".Tr() };
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
        frame.SetFooter([(retract, ChoiceRole.Danger), (clear, ChoiceRole.Ghost), (propose, ChoiceRole.Primary)]);
    }

    private VBoxContainer Scrolling(string heading, VBoxContainer content)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(100f, 0f) };
        box.AddChild(new Label { Text = heading.Tr(), ThemeTypeVariation = UiVariants.LabelBodyStrong });
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0f, theme.Px(96)), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
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

    /// <summary>C073: the AI's likely answer to the staged trade as the enum the pill is drawn from.</summary>
    public TradeAcceptance Acceptance { get; private set; }

    /// <summary>The pill that shows <see cref="Acceptance"/>; hidden when there is no hint.</summary>
    public Control HintPill => hintPill;

    public string HintBadgeText => hintPillText.Text;

    /// <summary>The frame of the screen (title, close button, footer actions).</summary>
    public ScreenFrame Frame => frame;

    public string PendingText => pending.Text;

    public string MessageText => messageText;

    /// <summary>The name of the civ being traded with (the pressed row of the list).</summary>
    public string TitleText => Partners.FirstOrDefault(it => it.CivId == SelectedCivId)?.Name ?? "";

    public Button ProposeButton => propose;

    public Button RetractButton => retract;

    public Button ClearButton => clear;

    /// <summary>Raised after the engine accepted a command made here, so the owner refreshes the world.</summary>
    public event Action? CommandAccepted;

    public event Action? Closed;

    /// <summary>C039: the player takes back its offer to this civ (the engine tells only the recipient, so the owner learns of it here).</summary>
    public event Action<string>? Retracting;

    public ListRow? PartnerButton(string civId) => partnerButtons.GetValueOrDefault(civId);

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
        var group = new ButtonGroup { AllowUnpress = false };
        foreach (var row in Partners)
        {
            var button = new ListRow(theme, row.Name, row.Subtitle, icon: row.Icon, group: group) { Name = "Partner_" + row.CivId, TooltipText = row.Label.TrText() };
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
        ShowAcceptance(TradeAcceptance.None);
        SetMessage("");
        Fire(RefreshHintAsync());
    }

    private static string Join(IReadOnlyList<string> parts) => parts.Count == 0 ? "nothing" : string.Join(", ", parts);

    private void Fill(VBoxContainer column, TradeBuilder builder, TradeSide side)
    {
        foreach (var group in builder.Groups(side))
        {
            column.AddChild(new SectionHeader(group.Title));
            foreach (var choice in group.Choices)
            {
                var line = new HBoxContainer();
                line.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
                var offer = new ListRow(theme, choice.Text, icon: choice.Icon, disabledReason: choice.Entry.Tradable ? null : "Not possible") { Name = choice.Id, TooltipText = choice.Label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
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
                    AddStep(line, "Minus_" + choice.Id, "Reduce", "Less", chosen, -1);
                    AddStep(line, "Plus_" + choice.Id, "Increase", "More", chosen, 1);
                }
                column.AddChild(line);
            }
        }
        if (builder.Groups(side).Count == 0) column.AddChild(new EmptyState(theme, AssetKey.Of(RulesetAssetKeys.UiIcon, "Diplomacy"), "Nothing to offer") { Name = "Empty" });
    }

    private void AddStep(HBoxContainer line, string name, string icon, string label, TradeChoice choice, int steps)
    {
        var button = new IconButton(theme, AssetKey.Of(RulesetAssetKeys.UiIcon, icon), label) { Name = name, SizeFlagsVertical = SizeFlags.ShrinkCenter };
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
            var texts = Card("Incoming_" + id, trade.Title, out var buttons);
            var flow = new HFlowContainer { Name = "Offers" };
            flow.AddThemeConstantOverride("h_separation", theme.Px(UiTokens.S3));
            AddItems(flow, "They give", trade.GiveItems);
            AddItems(flow, "They ask", trade.AskItems);
            texts.AddChild(flow);
            AddControl(buttons, "Accept_" + id, "Sounds good!", ChoiceRole.Primary, () => Fire(SendAsync(trade.Accept)));
            AddControl(buttons, "Decline_" + id, "Not this time.", ChoiceRole.Secondary, () => Fire(SendAsync(trade.Decline)));
            AddControl(buttons, "Counter_" + id, "Counter...", ChoiceRole.Ghost, () => Fire(CounterAsync(id)));
        }
        foreach (var demand in IncomingDemands)
        {
            string id = demand.FromId;
            Card("Demand_" + id, demand.Title, out var buttons);
            AddControl(buttons, "DemandAccept_" + id, "Very well.", ChoiceRole.Secondary, () => Fire(SendAsync(demand.Accept)));
            AddControl(buttons, "DemandRefuse_" + id, "Never!", ChoiceRole.Danger, () => Fire(SendAsync(demand.Refuse)));
        }
    }

    // A card with its wrapped text and its answer buttons beside it: a long offer wraps instead of pushing the buttons off the screen.
    private VBoxContainer Card(string name, string text, out HBoxContainer buttons)
    {
        var card = new PanelContainer { Name = name, ThemeTypeVariation = UiVariants.PanelCard };
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", theme.Px(UiTokens.S3));
        card.AddChild(box);
        var texts = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        texts.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        texts.AddChild(new Label { Text = text.TrText(), ThemeTypeVariation = UiVariants.LabelBodyStrong, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(100f, 0f) });
        box.AddChild(texts);
        buttons = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        buttons.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        box.AddChild(buttons);
        incoming.AddChild(card);
        return texts;
    }

    // "They give" followed by the offers with their icons, wrapping onto further lines.
    private void AddItems(HFlowContainer flow, string heading, IReadOnlyList<TradeItem> items)
    {
        flow.AddChild(new Label { Text = heading.Tr() + ":", ThemeTypeVariation = UiVariants.LabelSecondary });
        if (items.Count == 0) flow.AddChild(new Label { Text = "nothing".Tr(), ThemeTypeVariation = UiVariants.LabelSecondary });
        foreach (var item in items) flow.AddChild(new IconLabel(theme, item.Icon, item.Text.TrText()));
    }

    private void AddControl(HBoxContainer box, string name, string text, ChoiceRole role, Action pressed)
    {
        var button = new Button { Name = name, Text = text.Tr(), ThemeTypeVariation = Modal.VariationOf(role) };
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
        ShowAcceptance(TradeScreenModel.Acceptance(answer));
    }

    // The hint is drawn from the enum: a pill with a short word (gold for good news, red for a refusal) and the full sentence beside it.
    private void ShowAcceptance(TradeAcceptance acceptance)
    {
        Acceptance = acceptance;
        hint.Text = TradeScreenModel.AcceptanceText(acceptance).TrText();
        hint.Visible = acceptance != TradeAcceptance.None;
        hintPillText.Text = TradeScreenModel.AcceptanceBadge(acceptance).TrText();
        hintPill.Visible = acceptance != TradeAcceptance.None;
        hintPill.ThemeTypeVariation = acceptance == TradeAcceptance.Refused ? UiVariants.PanelBadge : UiVariants.PanelBadgeAccent;
        hintPillText.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(acceptance == TradeAcceptance.Refused ? UiTokens.TextPrimary : UiTokens.OnAccent));
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

    private void SetMessage(string text)
    {
        messageText = text;
        message.Text = text;
        message.Visible = text.Length > 0;
    }
}
