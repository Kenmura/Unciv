using Godot;
using Unciv.GodotClient.Localization;
using Unciv.Models.Translations;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Ui;
using TabBar = Unciv.GodotClient.Ui.TabBar;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C023: the empire overview. Tabs for stats, cities, units, resources, ranking, religion and espionage. Cities and units are the viewer's own entries of its snapshot and
/// have a jump button; every other number is the engine's answer to a query (<see cref="OverviewModel"/> only words it). A tab whose query the game refuses (rankings hidden,
/// espionage or religion off) says so instead of showing an empty table. The only change this screen makes is the spy command it sends through the <see cref="IGameSession"/>.
/// </summary>
public partial class OverviewScreen : PanelContainer
{
    private readonly IGameSession session;
    private readonly GameQueries queries;
    private readonly Label message = new() { Name = "Message" };
    private readonly TabBar tabs = new() { Name = "OverviewTabs", SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Dictionary<string, VBoxContainer> pages = [];
    private readonly Dictionary<string, List<OverviewTable>> tables = [];
    private readonly Dictionary<string, Button> jumpButtons = [];
    private readonly Dictionary<string, Button> spyButtons = [];
    private readonly Dictionary<string, string> notices = [];
    private int generation;

    public OverviewScreen(IGameSession gameSession, UiTheme uiTheme)
    {
        session = gameSession;
        queries = new GameQueries(gameSession);
        Name = "OverviewPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        Theme = uiTheme.Theme;

        var column = new VBoxContainer();
        AddChild(column);
        var head = new HBoxContainer();
        column.AddChild(head);
        var heading = new Label { Text = "Overview".Tr(), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddThemeFontSizeOverride("font_size", uiTheme.Px(20));
        head.AddChild(heading);
        message.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        message.ClipText = true;
        message.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(message);
        var close = new Button { Name = "OverviewClose", Text = "Close overview".Tr() };
        close.Pressed += Close;
        head.AddChild(close);
        column.AddChild(tabs);
        foreach (string tab in OverviewModel.Tabs)
        {
            var scroll = new ScrollContainer { Name = "Scroll_" + tab, HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, SizeFlagsVertical = SizeFlags.ExpandFill };
            var page = new VBoxContainer { Name = "Page_" + tab, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            page.AddThemeConstantOverride("separation", 4);
            scroll.AddChild(page);
            pages[tab] = page;
            tabs.AddTab(tab, scroll);
        }
    }

    /// <summary>The name of the tab now shown.</summary>
    public string SelectedTab => OverviewModel.Tabs[Math.Max(0, tabs.SelectedIndex)];

    public string MessageText => message.Text;

    /// <summary>The tables of a page as drawn (the ranking tab has one per column); empty when the page shows a notice instead.</summary>
    public IReadOnlyList<OverviewTable> Tables(string tab) => tables.TryGetValue(tab, out var list) ? list : [];

    /// <summary>The notice a page shows instead of data (a refused query), or null.</summary>
    public string? Notice(string tab) => notices.GetValueOrDefault(tab);

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
        notices.Clear();
        foreach (var page in pages.Values) Clear(page);

        if (overview == null) ShowNotice(OverviewModel.Stats, "The engine would not show the empire overview.");
        else
        {
            AddLines(OverviewModel.Stats, OverviewModel.StatsLines(overview));
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
            pages[OverviewModel.Ranking].AddChild(ranking);
            foreach (var (title, table) in OverviewModel.RankingTables(rankings)) ranking.AddChild(BuildTable(OverviewModel.Ranking, table, title, null));
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
        AddLines(tab, [text]);
    }

    private void AddLines(string tab, IEnumerable<string> lines)
    {
        foreach (string line in lines) pages[tab].AddChild(new Label { Text = line.TrText(), AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    private void AddTable(string tab, OverviewTable table, string? title, Action<string>? jump = null) => pages[tab].AddChild(BuildTable(tab, table, title, jump));

    private Control BuildTable(string tab, OverviewTable table, string? title, Action<string>? jump)
    {
        if (!tables.TryGetValue(tab, out var list)) tables[tab] = list = [];
        list.Add(table);
        var box = new VBoxContainer { Name = "Table_" + (title ?? tab) };
        if (title != null) box.AddChild(new Label { Text = title.Tr() });
        if (table.Count == 0)
        {
            box.AddChild(new Label { Text = table.Empty.Tr() });
            return box;
        }
        var grid = new GridContainer { Columns = table.Columns.Count + (jump != null ? 1 : 0) };
        grid.AddThemeConstantOverride("h_separation", 14);
        box.AddChild(grid);
        foreach (string heading in table.Columns) grid.AddChild(new Label { Text = heading.Tr() });
        if (jump != null) grid.AddChild(new Label { Text = "" });
        foreach (var row in table.Rows)
        {
            foreach (string cell in row.Cells) grid.AddChild(new Label { Text = cell });
            if (jump == null) continue;
            var button = new Button { Name = $"Jump_{tab}_{row.Key}", Text = "Go to".Tr() };
            string key = row.Key;
            button.Pressed += () => jump(key);
            jumpButtons[tab + "/" + key] = button;
            grid.AddChild(button);
        }
        return box;
    }

    private void BuildSpies(IReadOnlyList<SpyRow> spies)
    {
        var page = pages[OverviewModel.Espionage];
        if (spies.Count == 0)
        {
            page.AddChild(new Label { Text = "You have no spies.".Tr() });
            return;
        }
        foreach (var spy in spies)
        {
            page.AddChild(new Label { Text = spy.Summary.TrText() });
            var buttons = new HFlowContainer();
            page.AddChild(buttons);
            for (int i = 0; i < spy.Moves.Count; i++)
            {
                var move = spy.Moves[i];
                var button = new Button { Name = $"Spy_{spy.Name}_{i}", Text = "Move to ".Tr() + move.Label };
                button.Pressed += () => Fire(Send(move.Command));
                spyButtons[spy.Name + "/" + i] = button;
                buttons.AddChild(button);
            }
            if (spy.Coup is { } coup)
            {
                var button = new Button { Name = $"Spy_{spy.Name}_Coup", Text = spy.CoupLabel.TrText() };
                button.Pressed += () => Fire(Send(coup));
                spyButtons[spy.Name + "/-1"] = button;
                buttons.AddChild(button);
            }
        }
    }

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

    private void SetMessage(string text) => message.Text = text;
}
