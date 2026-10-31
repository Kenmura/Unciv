using Godot;
using Unciv.Assets;
using Unciv.Client.Net.Hosting;
using Unciv.GodotClient.Assets;
using Unciv.Models.Translations;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Net.Relay;
using Unciv.Server.Transport;

namespace Unciv.GodotClient.Setup;

/// <summary>What the Browse games screen needs from the machine, as plain callbacks so a scenario can supply an in-memory directory and a fixed set of installed rulesets.</summary>
public sealed class BrowseScreenEnvironment
{
    /// <summary>The configured relay, or null when none is set (there is no default relay).</summary>
    public required Func<RelayEndpoint?> Relay { get; init; }

    /// <summary>Reads the directory of <c>relay</c> (the real one is <see cref="RelayDirectoryClient.FetchAsync"/>). Throws <see cref="RelayException"/> when the relay cannot be used.</summary>
    public required Func<RelayEndpoint, DirectoryQuery, CancellationToken, Task<DirectoryResult>> Fetch { get; init; }

    /// <summary>What this machine has installed, how it hashes rules and which host keys it knows; read again on every refresh.</summary>
    public required Func<BrowseContext> Context { get; init; }
}

/// <summary>
/// T339 (design/RELAY.md sections 7 and 9, R16): the Browse games screen. It asks the CONFIGURED relay for its public game list (over the tunnel, never over HTTP; every listing is verified before it is shown, see
/// <see cref="ListingVerifier"/>), and lists game name, host, players as the host reports them, rules and mods, speed and the password flag, with the relay's name, contact and the measured round trip in the header. A search
/// box and filters (free slots, hide started, open or password games, ruleset) narrow it; Refresh asks again. A game this client cannot join is greyed out with the reason. Join opens a dialog that says whose key is being trusted
/// (first time or known), asks for the password when the game has one and tells which mods will be offered for download; then <see cref="JoinRequested"/> hands the game on (the join screen runs the connection). Empty, offline,
/// relay-too-old, no-directory, rate-limited and no-relay-configured are different states with different sentences. The layout follows the live window: list and details side by side on a wide window, stacked on a narrow one.
/// </summary>
public partial class BrowseScreen : Control
{
    private const string AnyRuleset = "Any ruleset";
    private readonly UiTheme theme;
    private readonly BrowseScreenEnvironment env;
    private readonly ScreenFrame frame;
    private readonly Label relayLine = new() { Name = "RelayLine", ThemeTypeVariation = UiVariants.LabelSecondary, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer listBox = new() { Name = "GameList", SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly ScrollContainer listScroll = new() { Name = "GameScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Label detailLabel = new() { Name = "GameDetail", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly PanelContainer detailPanel = new() { Name = "DetailPanel", ThemeTypeVariation = UiVariants.PanelCard };
    private readonly BoxContainer body = new() { Name = "BrowseBody", SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly VBoxContainer stateHost = new() { Name = "StateHost", SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
    private readonly List<string?> rulesetChoices = [];
    private readonly ButtonGroup group = new();
    private readonly List<BrowseRow> rows = [];
    private readonly List<ListRow> rowButtons = [];
    private CancellationTokenSource? loading;
    private int selected = -1;
    private EmptyState? state;
    private JoinDialog? dialog;

    public BrowseScreen(UiTheme theme, BrowseScreenEnvironment env)
    {
        this.theme = theme;
        this.env = env;
        Name = "BrowseScreen";
        this.Fill();
        var backdrop = new ColorRect { Name = "Backdrop", Color = ProceduralAssets.ToColor(UiTokens.Bg1), MouseFilter = MouseFilterEnum.Ignore };
        backdrop.Fill();
        AddChild(backdrop);
        frame = new ScreenFrame(theme, "Browse games", closeButton: false);
        frame.Fill();
        AddChild(frame);

        var column = new VBoxContainer { Name = "BrowseColumn" };
        column.FitConstant(theme, "separation", UiTokens.S2);
        column.AddChild(relayLine);

        var toolbar = new HFlowContainer { Name = "BrowseToolbar" };
        toolbar.FitConstant(theme, "h_separation", UiTokens.S2);
        toolbar.FitConstant(theme, "v_separation", UiTokens.S2);
        SearchField = new LineEdit { Name = "SearchField", PlaceholderText = "Search by game or host name".Tr(), ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill }.FitMin(theme, 240);
        SearchField.TextSubmitted += _ => Refresh();
        RefreshButton = new Button { Name = "RefreshButton", Text = "Refresh".Tr() };
        RefreshButton.Pressed += Refresh;
        toolbar.AddChild(SearchField);
        toolbar.AddChild(RefreshButton);
        column.AddChild(toolbar);

        var filters = new HFlowContainer { Name = "BrowseFilters" };
        filters.FitConstant(theme, "h_separation", UiTokens.S3);
        filters.FitConstant(theme, "v_separation", UiTokens.S2);
        FreeSlotsCheck = new CheckBox { Name = "FreeSlotsCheck", Text = "Free slots".Tr() };
        FreeSlotsCheck.Toggled += _ => Refresh();
        HideStartedCheck = new CheckBox { Name = "HideStartedCheck", Text = "Hide started games".Tr() };
        HideStartedCheck.Toggled += _ => Refresh();
        PasswordFilter = new Segmented(["Any", "Open", "Password"], "Any") { Name = "PasswordFilter" };
        PasswordFilter.Chosen += _ => Refresh();
        RulesetPicker = new OptionButton { Name = "RulesetPicker", TooltipText = "Only games with this ruleset".Tr() };
        FillRulesets();
        RulesetPicker.ItemSelected += _ => Refresh();
        filters.AddChild(FreeSlotsCheck);
        filters.AddChild(HideStartedCheck);
        filters.AddChild(PasswordFilter);
        filters.AddChild(RulesetPicker);
        column.AddChild(filters);

        listScroll.AddChild(listBox);
        body.AddChild(listScroll);
        var detailScroll = new ScrollContainer { Name = "DetailScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        detailScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        detailPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        detailPanel.AddChild(detailLabel);
        detailScroll.AddChild(detailPanel);
        body.AddChild(detailScroll);
        body.FitConstant(theme, "separation", UiTokens.S3);
        // the details sit beside the list on a wide window and under it on a narrow one; their size follows the live scale and window
        theme.Fit(this, () =>
        {
            bool compact = theme.Breakpoint == UiBreakpoint.Compact;
            body.Vertical = compact;
            detailScroll.CustomMinimumSize = compact ? new Vector2(0, theme.Px(150)) : new Vector2(theme.Px(380), 0);
            detailScroll.SizeFlagsStretchRatio = compact ? 1f : 0.8f;
        });
        column.AddChild(body);
        column.AddChild(stateHost);
        frame.SetContent(column);

        JoinButton = new Button { Text = "Join".Tr(), Name = "JoinButton", Disabled = true };
        JoinButton.Pressed += OpenJoinDialog;
        BackButton = new Button { Text = "Back".Tr(), Name = "BackButton" };
        BackButton.Pressed += () => BackRequested?.Invoke();
        frame.SetFooter([(BackButton, ChoiceRole.Ghost), (JoinButton, ChoiceRole.Primary)]);
        ShowStatus(BrowseStatus.Loading);
    }

    public LineEdit SearchField { get; }

    public Button RefreshButton { get; }

    public CheckBox FreeSlotsCheck { get; }

    public CheckBox HideStartedCheck { get; }

    public Segmented PasswordFilter { get; }

    public OptionButton RulesetPicker { get; }

    public Button JoinButton { get; }

    public Button BackButton { get; }

    public BrowseStatus Status { get; private set; }

    /// <summary>The sentence of the current non-list state (empty while games are listed).</summary>
    public string StateMessage => state?.Message ?? "";

    /// <summary>The "Open Options" action of the no-relay state, or null in every other.</summary>
    public Button? OptionsButton => state?.ActionButton;

    /// <summary>The relay's name, contact, game count and ping, or what could not be read.</summary>
    public string RelayHeader => relayLine.Text;

    /// <summary>The games listed now, in order.</summary>
    public IReadOnlyList<BrowseRow> Rows => rows;

    public ListRow? RowButton(int index) => index >= 0 && index < rowButtons.Count ? rowButtons[index] : null;

    public int SelectedIndex => selected;

    /// <summary>The detail text of the selected game, or empty.</summary>
    public string Detail => selected >= 0 ? detailLabel.Text : "";

    /// <summary>The join dialog while it is open, else null.</summary>
    public JoinDialog? Dialog => dialog;

    public bool IsLoading => Status == BrowseStatus.Loading;

    /// <summary>The query the filters describe now.</summary>
    public DirectoryQuery Query => new()
    {
        Search = SearchField.Text,
        Ruleset = RulesetPicker.Selected > 0 && RulesetPicker.Selected < rulesetChoices.Count ? rulesetChoices[RulesetPicker.Selected] : null,
        OnlyWithSlots = FreeSlotsCheck.ButtonPressed,
        HideStarted = HideStartedCheck.ButtonPressed,
        PasswordRequired = PasswordFilter.Selected switch { "Open" => false, "Password" => true, _ => null },
    };

    public event Action? BackRequested;

    /// <summary>Raised when the player confirmed the join dialog; the owner runs the connection (the join screen) with the game and the password (null when the game has none).</summary>
    public event Action<BrowseRow, string?>? JoinRequested;

    /// <summary>Raised by the "Open Options" action of the no-relay state.</summary>
    public event Action? OptionsRequested;

    public override void _Ready() => Refresh();

    public override void _ExitTree()
    {
        loading?.Cancel();
        loading?.Dispose();
        loading = null;
    }

    private void FillRulesets()
    {
        RulesetPicker.Clear();
        rulesetChoices.Clear();
        RulesetPicker.AddItem(AnyRuleset.TrText());
        rulesetChoices.Add(null);
        foreach (var name in env.Context().BaseRulesets.Order(StringComparer.Ordinal))
        {
            RulesetPicker.AddItem(name);
            rulesetChoices.Add(name);
        }
    }

    /// <summary>Asks the relay again with the current search and filters.</summary>
    public void Refresh()
    {
        loading?.Cancel();
        loading?.Dispose();
        loading = null;
        CloseDialog();
        if (env.Relay() is not { } relay)
        {
            ShowStatus(BrowseStatus.NoRelay);
            return;
        }
        ShowStatus(BrowseStatus.Loading);
        loading = new CancellationTokenSource();
        _ = LoadAsync(relay, Query, loading);
    }

    private async Task LoadAsync(RelayEndpoint relay, DirectoryQuery query, CancellationTokenSource mine)
    {
        DirectoryResult? result = null;
        (BrowseStatus Status, string? Detail)? failure = null;
        try
        {
            result = await env.Fetch(relay, query, mine.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return; // a newer refresh took over
        }
        catch (RelayException e)
        {
            failure = BrowseModel.FromRelayCode(e.Code);
        }
        catch (Exception e) when (e is not OutOfMemoryException) // anything else must end the Loading state too: this task is not awaited
        {
            failure = (BrowseStatus.Failed, "The relay's answer could not be used: " + e.Message);
        }
        if (mine != loading || !IsInstanceValid(this)) return;
        if (failure is { } failed)
        {
            relayLine.Text = relay.Key;
            ShowStatus(failed.Status, failed.Detail);
            return;
        }
        ShowResult(result!);
    }

    private void ShowResult(DirectoryResult result)
    {
        string? selectedRoom = selected >= 0 && selected < rows.Count ? rows[selected].Game.Room : null;
        var browse = env.Context();
        rows.Clear();
        rows.AddRange(result.Games.Select(game => BrowseModel.Describe(game, browse)));
        var line = BrowseModel.RelayLine(result.Info.Name, result.Info.Contact, result.Info.Listed, result.PingMillis);
        if (result.Dropped > 0) line += $" - {result.Dropped.ToString(System.Globalization.CultureInfo.InvariantCulture)} {"hidden (failed the safety checks or the filters)".Tr()}";
        if (result.More) line += " - " + "more games exist: narrow the search".Tr();
        relayLine.Text = line;
        FillList();
        int again = selectedRoom == null ? -1 : rows.FindIndex(it => it.Game.Room == selectedRoom && it.CanJoin);
        if (rows.Count == 0) ShowStatus(Query == new DirectoryQuery() ? BrowseStatus.Empty : BrowseStatus.NoMatch);
        else ShowStatus(BrowseStatus.Ready);
        Select(again);
    }

    private void FillList()
    {
        foreach (var child in listBox.GetChildren())
        {
            listBox.RemoveChild(child);
            child.QueueFree();
        }
        rowButtons.Clear();
        selected = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            int index = i;
            var row = rows[i];
            var button = new ListRow(theme, row.Title, row.Subtitle, row.Trailing, disabledReason: row.DisabledReason, group: group) { Name = "Game-" + row.Game.Room, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            button.Pressed += () => Select(index);
            rowButtons.Add(button);
            listBox.AddChild(button);
        }
    }

    private void Select(int index)
    {
        selected = index >= 0 && index < rows.Count ? index : -1;
        if (selected >= 0) rowButtons[selected].SetPressedNoSignal(true);
        detailLabel.Text = selected >= 0 ? rows[selected].Detail : "Select a game to see its details.".Tr();
        JoinButton.Disabled = selected < 0 || !rows[selected].CanJoin;
    }

    private void ShowStatus(BrowseStatus status, string? detail = null)
    {
        Status = status;
        bool list = status == BrowseStatus.Ready;
        body.Visible = list;
        stateHost.Visible = !list;
        foreach (var child in stateHost.GetChildren())
        {
            stateHost.RemoveChild(child);
            child.QueueFree();
        }
        state = null;
        RefreshButton.Disabled = status == BrowseStatus.Loading;
        if (status == BrowseStatus.Loading) relayLine.Text = env.Relay()?.Key ?? "";
        else if (status == BrowseStatus.NoRelay) relayLine.Text = "No relay set".Tr();
        if (list)
        {
            return;
        }
        rows.Clear();
        rowButtons.Clear();
        Select(-1);
        state = new EmptyState(theme, null, BrowseModel.StateText(status, detail), status == BrowseStatus.NoRelay ? "Open Options" : null, wrap: true) { Name = "BrowseState" };
        state.ActionPressed += () => OptionsRequested?.Invoke();
        stateHost.AddChild(state);
    }

    // The join dialog

    /// <summary>Opens the join dialog for the selected game (what the Join button does).</summary>
    public void OpenJoinDialog()
    {
        if (selected < 0 || !rows[selected].CanJoin || dialog != null) return;
        var row = rows[selected];
        dialog = new JoinDialog(row, () =>
        {
            var password = dialog?.PasswordField.Text;
            CloseDialog();
            JoinRequested?.Invoke(row, row.Game.Info.PasswordRequired ? password : null);
        }, CloseDialog);
        AddChild(dialog);
        dialog.Init(theme);
        dialog.Open();
    }

    private void CloseDialog()
    {
        if (dialog == null) return;
        var closing = dialog;
        dialog = null;
        closing.QueueFree();
    }

    /// <summary>The dialog before a join: whose key is trusted, the mods that will be offered, and the password when the game has one.</summary>
    public partial class JoinDialog : Modal
    {
        private readonly BrowseRow row;
        private readonly Action confirm;
        private readonly Action cancel;
        private readonly Label prompt = new() { Name = "JoinPrompt", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        private readonly Label error = new() { Name = "JoinError", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };

        public JoinDialog(BrowseRow row, Action confirm, Action cancel)
        {
            this.row = row;
            this.confirm = confirm;
            this.cancel = cancel;
            Name = "JoinDialog";
            PasswordField = new LineEdit { Name = "DialogPasswordField", Secret = true, PlaceholderText = "Password".Tr(), Visible = row.Game.Info.PasswordRequired };
            PasswordField.TextSubmitted += _ => Confirm();
        }

        public LineEdit PasswordField { get; }

        public Button ConfirmButton { get; private set; } = null!; // JUSTIFY: T339 assigned by Open, which the owner calls right after Init

        public Button CancelButton { get; private set; } = null!; // JUSTIFY: T339 assigned by Open, which the owner calls right after Init

        /// <summary>The text shown above the fields.</summary>
        public string PromptText => prompt.Text;

        /// <summary>The problem under the fields (an empty password), or empty.</summary>
        public string ErrorText => error.Visible ? error.Text : "";

        public void Open()
        {
            SetHeader("Join this game?", row.Game.Info.Name);
            prompt.Text = BrowseModel.JoinPrompt(row).TrText();
            float width = Fit(440, prompt);
            var scroll = new ScrollContainer { Name = "JoinPromptScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(width, 0) };
            scroll.AddChild(prompt);
            Body.AddChild(scroll);
            FitScroll(scroll, prompt, 0.5f);
            Body.AddChild(PasswordField);
            error.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(UiTokens.Warning));
            Body.AddChild(error);
            ConfirmButton = new Button { Name = "ConfirmJoinButton", Text = "Join".Tr() };
            ConfirmButton.Pressed += Confirm;
            CancelButton = new Button { Name = "CancelJoinButton", Text = "Cancel".Tr() };
            CancelButton.Pressed += cancel;
            SetActions([(CancelButton, ChoiceRole.Ghost), (ConfirmButton, ChoiceRole.Primary)]);
            Visible = true;
            if (PasswordField.Visible) PasswordField.GrabFocus();
        }

        private void Confirm()
        {
            if (row.Game.Info.PasswordRequired && PasswordField.Text.Length == 0)
            {
                error.Text = "Type the password the host gave you.".Tr();
                error.Visible = true;
                return;
            }
            confirm();
        }

        protected override bool OnCancel()
        {
            cancel();
            return true;
        }
    }
}
