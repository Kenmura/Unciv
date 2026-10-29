using Godot;
using Unciv.GodotClient.Localization;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Ui;
using TabBar = Unciv.GodotClient.Ui.TabBar;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C023, redesigned in C074: the empire overview in a <see cref="ScreenFrame"/>. Tabs for stats, cities, units, resources, ranking, religion, espionage and politics. The stats page is a grid of
/// cards; cities, units, resources, ranking, known civilizations, politics and the spies are sortable <see cref="DataTable"/>s with typed (right-aligned) number columns. Cities and units are the
/// viewer's own entries of its snapshot and have a trailing "Go to" icon button; every other number is the engine's answer to a query (<see cref="OverviewModel"/> only words it). A tab whose query
/// the game refuses (rankings hidden, espionage or religion off) says so instead of showing an empty table. The only change this screen makes is the spy command it sends through the <see cref="IGameSession"/>.
/// </summary>
public partial class OverviewScreen : PanelContainer
{
    private readonly IGameSession session;
    private readonly GameQueries queries;
    private readonly UiTheme theme;
    private readonly Dictionary<string, VBoxContainer> pages = [];
    private readonly Dictionary<string, List<OverviewTable>> tables = [];
    private readonly Dictionary<string, List<DataTable>> dataTables = [];
    private readonly Dictionary<string, Button> jumpButtons = [];
    private readonly Dictionary<string, Button> spyButtons = [];
    private readonly Dictionary<string, string> notices = [];
    private ScreenFrame frame = null!; // JUSTIFY: C074 assigned by BuildFrame in the constructor
    private Label message = null!; // JUSTIFY: C074 assigned by BuildFrame in the constructor
    private TabBar tabs = null!; // JUSTIFY: C074 assigned by BuildFrame in the constructor
    private string selectedTab = OverviewModel.Stats;
    private int generation;

    public OverviewScreen(IGameSession gameSession, UiTheme uiTheme)
    {
        session = gameSession;
        queries = new GameQueries(gameSession);
        theme = uiTheme;
        Name = "OverviewPanel";
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
        if (Visible) Fire(RefreshAsync());
    }

    private void BuildFrame()
    {
        if (frame != null)
        {
            RemoveChild(frame); // at once: a queued free would leave the old frame in the layout until the frame ends
            frame.QueueFree();
        }
        pages.Clear();
        frame = new ScreenFrame(theme, "Overview", closeLabel: "Close overview");
        frame.CloseRequested += Close;
        AddChild(frame);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        message = new Label { Name = "Message", ThemeTypeVariation = UiVariants.LabelSecondary, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, Visible = false };
        column.AddChild(message);
        tabs = new TabBar { Name = "OverviewTabs", SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(tabs);
        foreach (string tab in OverviewModel.Tabs)
        {
            var scroll = new ScrollContainer { Name = "Scroll_" + tab, HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, SizeFlagsVertical = SizeFlags.ExpandFill };
            var page = new VBoxContainer { Name = "Page_" + tab, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            page.AddThemeConstantOverride("separation", theme.Px(UiTokens.S3));
            scroll.AddChild(page);
            pages[tab] = page;
            tabs.AddTab(tab, scroll);
        }
        frame.SetContent(column);
        tabs.Select(Math.Max(0, OverviewModel.Tabs.ToList().IndexOf(selectedTab)));
        tabs.TabSelected += index => selectedTab = OverviewModel.Tabs[index];
    }

    /// <summary>The name of the tab now shown.</summary>
    public string SelectedTab => OverviewModel.Tabs[Math.Max(0, tabs.SelectedIndex)];

    public string MessageText => message.Text;

    /// <summary>The tables of a page as drawn (the ranking tab has one per column); empty when the page shows a notice instead.</summary>
    public IReadOnlyList<OverviewTable> Tables(string tab) => tables.TryGetValue(tab, out var list) ? list : [];

    /// <summary>The <see cref="DataTable"/> controls of a page, in the order of <see cref="Tables"/> (a table without rows shows an empty state instead and has no control).</summary>
    public IReadOnlyList<DataTable> DataTables(string tab) => dataTables.TryGetValue(tab, out var list) ? list : [];

    /// <summary>The notice a page shows instead of data (a refused query), or null.</summary>
    public string? Notice(string tab) => notices.GetValueOrDefault(tab);

    /// <summary>The frame of the screen (title, close button).</summary>
    public ScreenFrame Frame => frame;

    /// <summary>The jump button of a row of the cities (key = city name) or units (key = unit id) tab.</summary>
    public Button? JumpButton(string tab, string key) => jumpButtons.GetValueOrDefault(tab + "/" + key);

    /// <summary>The button of a spy's move (<paramref name="index"/> 0 is the first the engine offers) or the coup button (index -1).</summary>
    public Button? SpyButton(string spy, int index) => spyButtons.GetValueOrDefault(spy + "/" + index);

    public Button? TabButton(string tab) => tabs.FindChild("Tab_" + tab, true, false) as Button;

    public event Action<string>? CityChosen;

    public event Action<int>? UnitChosen;

    /// <summary>Raised after the engine accepted a command made here (a spy order), so the owner refreshes the world.</summary>
    public event Action? CommandAccepted;

    public event Action? Closed;

    /// <summary>Shows the screen and fills every page from the engine's current answers.</summary>
    public async Task OpenAsync(string? tab = null)
    {
        Visible = true;
        if (tab != null) tabs.Select(Math.Max(0, OverviewModel.Tabs.ToList().IndexOf(tab)));
        await RefreshAsync();
    }

    public void Close()
    {
        generation++;
        Visible = false;
        Closed?.Invoke();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Asks the engine again and redraws all pages (the viewer's snapshot is read at the same time).</summary>
    public async Task RefreshAsync()
    {
        int ticket = ++generation;
        var snapshot = session.Snapshot;
        var overview = await queries.Overview();
        var bar = await queries.TopBar();
        var rankings = await queries.Rankings();
        var religion = await queries.Religion();
        var spies = await queries.Spies();
        var vote = await queries.DiplomaticVote();
        var politics = new List<PoliticsEntry>();
        if (overview != null)
            foreach (var civ in overview.KnownCivs) politics.Add(new PoliticsEntry(civ, await queries.DiplomacyPanel(civ.CivName)));
        if (ticket != generation) return; // closed or refreshed meanwhile
        jumpButtons.Clear();
        spyButtons.Clear();
        tables.Clear();
        dataTables.Clear();
        notices.Clear();
        foreach (var page in pages.Values) Clear(page);

        if (overview == null) ShowNotice(OverviewModel.Stats, "The engine would not show the empire overview.");
        else
        {
            AddCards(OverviewModel.Stats, OverviewModel.StatsCards(overview));
            AddTable(OverviewModel.Stats, OverviewModel.KnownCivs(overview), "Known civilizations");
        }
        AddTable(OverviewModel.Cities, OverviewModel.CityTable(snapshot), null, name => CityChosen?.Invoke(name));
        AddTable(OverviewModel.Units, OverviewModel.UnitTable(snapshot), null, id => UnitChosen?.Invoke(int.Parse(id, System.Globalization.CultureInfo.InvariantCulture)));
        if (bar == null) ShowNotice(OverviewModel.Resources, "The engine would not show the resources.");
        else AddTable(OverviewModel.Resources, OverviewModel.ResourceTable(bar), null);
        if (rankings == null) ShowNotice(OverviewModel.Ranking, "Rankings are not shown in this game.");
        else
        {
            var ranking = new HBoxContainer();
            ranking.AddThemeConstantOverride("separation", theme.Px(UiTokens.S3));
            pages[OverviewModel.Ranking].AddChild(ranking);
            foreach (var (title, table) in OverviewModel.RankingTables(rankings))
            {
                var box = BuildTable(OverviewModel.Ranking, table, title, null);
                box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                ranking.AddChild(box);
            }
        }
        if (religion == null) ShowNotice(OverviewModel.Religion, "The engine would not show religion.");
        else AddLines(OverviewModel.Religion, OverviewModel.ReligionLines(religion));
        if (spies == null) ShowNotice(OverviewModel.Espionage, "Espionage is not enabled in this game.");
        else BuildSpies(OverviewModel.SpyRows(spies));
        if (overview == null) ShowNotice(OverviewModel.Politics, "The engine would not show the empire overview.");
        else
        {
            AddTable(OverviewModel.Politics, OverviewModel.PoliticsTable(politics), "Relations with the civilizations you have met");
            AddLines(OverviewModel.Politics, OverviewModel.VoteLines(vote, id => snapshot.Civs.FirstOrDefault(it => it.CivId == id)?.Name ?? id));
        }
        SetMessage("");
    }

    private void ShowNotice(string tab, string text)
    {
        notices[tab] = text;
        pages[tab].AddChild(new EmptyState(theme, null, text));
    }

    private void AddLines(string tab, IEnumerable<string> lines)
    {
        foreach (string line in lines) pages[tab].AddChild(new Label { Text = line.TrText(), AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    /// <summary>The stats cards in a two-column grid; a stat entry shows its stat icon (a <see cref="StatChip"/>), a breakdown entry is indented.</summary>
    private void AddCards(string tab, IReadOnlyList<StatsCard> cards)
    {
        var grid = new GridContainer { Name = "StatsCards", Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", theme.Px(UiTokens.S3));
        grid.AddThemeConstantOverride("v_separation", theme.Px(UiTokens.S3));
        foreach (var card in cards)
        {
            var panel = new PanelContainer { Name = "Card_" + card.Title, ThemeTypeVariation = UiVariants.PanelCard, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.Fill };
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
            box.AddChild(new SectionHeader(card.Title));
            foreach (var entry in card.Entries)
            {
                var row = new HBoxContainer();
                var label = new Label
                {
                    Text = entry.Label.TrText().TrimStart('¤', '~'), ThemeTypeVariation = UiVariants.LabelSecondary, SizeFlagsHorizontal = SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                };
                if (entry.Indent) row.AddChild(new Control { CustomMinimumSize = new Vector2(theme.Px(UiTokens.S3), 0f) });
                row.AddChild(label);
                if (entry.Stat.Length > 0) row.AddChild(new StatChip(theme, entry.Stat, entry.Value));
                else row.AddChild(new Label { Text = entry.Value, ThemeTypeVariation = UiVariants.LabelNumber, HorizontalAlignment = HorizontalAlignment.Right });
                box.AddChild(row);
            }
            panel.AddChild(box);
            grid.AddChild(panel);
        }
        pages[tab].AddChild(grid);
    }

    private void AddTable(string tab, OverviewTable table, string? title, Action<string>? jump = null) => pages[tab].AddChild(BuildTable(tab, table, title, jump));

    private Control BuildTable(string tab, OverviewTable table, string? title, Action<string>? jump)
    {
        if (!tables.TryGetValue(tab, out var list)) tables[tab] = list = [];
        list.Add(table);
        var box = new VBoxContainer { Name = "Table_" + (title ?? tab) };
        box.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        if (title != null) box.AddChild(new SectionHeader(title));
        if (table.Count == 0)
        {
            box.AddChild(new EmptyState(theme, null, table.Empty));
            return box;
        }
        var data = new DataTable(table.Columns.Select(it => new DataColumn(it.Heading, it.Numeric)).ToList(), actions: jump != null) { Name = "Data_" + (title ?? tab) };
        data.SetRows(table.Rows.Select(it => (IReadOnlyList<string>)it.Cells).ToList(), jump == null ? null : index =>
        {
            string key = table.Rows[index].Key;
            var button = new IconButton(theme, AssetKey.Of(RulesetAssetKeys.UiIcon, "ArrowRight"), "Go to") { Name = $"Jump_{tab}_{key}" };
            button.Pressed += () => jump(key);
            jumpButtons[tab + "/" + key] = button;
            return button;
        });
        if (!dataTables.TryGetValue(tab, out var controls)) dataTables[tab] = controls = [];
        controls.Add(data);
        box.AddChild(data);
        return box;
    }

    /// <summary>The spies as a table (rank, location, action and the coup button), then one row of destination chips per spy that the engine lets move.</summary>
    private void BuildSpies(IReadOnlyList<SpyRow> spies)
    {
        var page = pages[OverviewModel.Espionage];
        if (spies.Count == 0)
        {
            page.AddChild(new EmptyState(theme, null, "You have no spies."));
            return;
        }
        var data = new DataTable([new DataColumn("Spy"), new DataColumn("Spy rank", true), new DataColumn("Location"), new DataColumn("Action")], actions: true) { Name = "Data_Spies" };
        data.SetRows(spies.Select(it => (IReadOnlyList<string>)[it.Name, it.Rank, it.Location.TrText(), it.Action.TrText()]).ToList(), index =>
        {
            var spy = spies[index];
            if (spy.Coup is not { } coup) return null;
            var button = CoupButton(theme, spy.Name, spy.CoupShort, spy.CoupLabel, danger: coup.Stage);
            button.Pressed += () => Fire(Send(coup));
            spyButtons[spy.Name + "/-1"] = button;
            return button;
        });
        if (!dataTables.TryGetValue(OverviewModel.Espionage, out var controls)) dataTables[OverviewModel.Espionage] = controls = [];
        controls.Add(data);
        page.AddChild(data);
        foreach (var spy in spies.Where(it => it.Moves.Count > 0))
        {
            var box = new VBoxContainer { Name = "Moves_" + spy.Name };
            box.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
            box.AddChild(new Label { Text = $"Move {spy.Name} to".TrText(), ThemeTypeVariation = UiVariants.LabelSecondary });
            var chips = new HFlowContainer();
            chips.AddThemeConstantOverride("h_separation", theme.Px(UiTokens.S1));
            chips.AddThemeConstantOverride("v_separation", theme.Px(UiTokens.S1));
            box.AddChild(chips);
            for (int i = 0; i < spy.Moves.Count; i++)
            {
                var move = spy.Moves[i];
                var button = new Button { Name = $"Spy_{spy.Name}_{i}", Text = move.Label.TrText(), ThemeTypeVariation = UiVariants.ButtonChip };
                button.Pressed += () => Fire(Send(move.Command));
                spyButtons[spy.Name + "/" + i] = button;
                chips.AddChild(button);
            }
            page.AddChild(box);
        }
    }

    /// <summary>The coup button of a spy row: the short text ("Coup 35%") with the whole sentence as its tooltip; staging a coup (the command stages it) is a danger button, calling it off a plain one. Clipped, so a long translation cannot widen the table.</summary>
    public static Button CoupButton(UiTheme theme, string spy, string text, string tooltip, bool danger) => new()
    {
        Name = $"Spy_{spy}_Coup", Text = text.TrText(), TooltipText = tooltip.TrText(), ClipText = true, ThemeTypeVariation = danger ? UiVariants.ButtonDanger : UiVariants.ButtonSecondary,
        CustomMinimumSize = new Vector2(theme.Px(80), 0f),
    };

    private async Task Send(GameCommand command)
    {
        var result = await session.SubmitAsync(command);
        if (!result.Accepted)
        {
            await RefreshAsync();
            SetMessage("Refused: " + result.Message);
            return;
        }
        CommandAccepted?.Invoke();
        await RefreshAsync();
    }

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
            box.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void SetMessage(string text)
    {
        message.Text = text;
        message.Visible = text.Length > 0;
    }
}
