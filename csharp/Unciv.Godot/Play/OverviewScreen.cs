using Godot;
using Unciv.GodotClient.Localization;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Ui;
using Unciv.Logic.City;
using Unciv.Models.Translations;
using TabBar = Unciv.GodotClient.Ui.TabBar;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C023, redesigned in C074: the empire overview in a <see cref="ScreenFrame"/>. Tabs for stats, cities, units, resources, ranking, religion, espionage and politics. The stats page is a grid of
/// cards; cities, units, resources, ranking, known civilizations, politics and the spies are sortable <see cref="DataTable"/>s with typed (right-aligned) number columns. Cities and units are the
/// viewer's own entries of its snapshot and have a trailing "Go to" icon button; every other number is the engine's answer to a query (<see cref="OverviewModel"/> only words it). A tab whose query
/// the game refuses (rankings hidden, espionage or religion off) says so instead of showing an empty table. The only changes this screen makes are the spy command and, since C092, the ordinary city
/// commands of the bulk actions of the cities tab (rows are selectable; set focus, queue an item, buy the current production and apply a named build template to the selected cities, one command per city,
/// see <see cref="CityBulkModel"/>), which it sends through the <see cref="IGameSession"/>; the templates live in the client settings.
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
    private string messageText = "";
    private int generation;
    private ClientSettingsService? settings;
    private readonly HashSet<string> selectedCities = [];
    private IReadOnlyDictionary<string, BulkCity> bulkCities = new Dictionary<string, BulkCity>();
    private int gold;
    private string bulkResult = "";
    private string bulkItem = "";
    private string bulkTemplate = "";
    private string bulkTemplateName = "";
    private CityFocus bulkFocus = CityFocus.ProductionFocus;
    private bool bulkBusy;

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
        jumpButtons.Clear(); // the controls of the old frame are gone; the next refresh fills them again
        spyButtons.Clear();
        tables.Clear();
        dataTables.Clear();
        notices.Clear();
        frame = new ScreenFrame(theme, "Overview", closeLabel: "Close overview");
        frame.CloseRequested += Close;
        AddChild(frame);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        message = new Label { Name = "Message", ThemeTypeVariation = UiVariants.LabelSecondary, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, Text = messageText, Visible = messageText.Length > 0 };
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

    /// <summary>C092: the client settings that hold the build templates; without them the template controls are disabled.</summary>
    public void BindSettings(ClientSettingsService clientSettings) => settings = clientSettings;

    /// <summary>C092: the names of the cities selected in the cities table.</summary>
    public IReadOnlyCollection<string> SelectedCities => selectedCities;

    /// <summary>C092: the text the last bulk action reported.</summary>
    public string BulkResultText => bulkResult;

    /// <summary>C092: a control of the bulk actions panel by name (BulkCount, BulkSelectAll, BulkClear, BulkFocus, BulkFocusApply, BulkItem, BulkQueue, BulkBuy, BulkTemplate, BulkTemplateApply, BulkTemplateDelete, BulkTemplateName, BulkTemplateSave, BulkResult).</summary>
    public Control? BulkControl(string name) => pages[OverviewModel.Cities].FindChild(name, true, false) as Control;

    /// <summary>The name of the tab now shown.</summary>
    public string SelectedTab => OverviewModel.Tabs[Math.Max(0, tabs.SelectedIndex)];

    public string MessageText => messageText;

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
        var bulk = new Dictionary<string, BulkCity>();
        foreach (var city in snapshot.Cities.Where(it => it.Owner == snapshot.You && it.Own != null))
            if (await queries.CityPanel(city.Own!.Id) is { } panel && await queries.ConstructionList(city.Own.Id) is { } list) bulk[city.Name] = new BulkCity(panel, list);
        if (ticket != generation) return; // closed or refreshed meanwhile
        jumpButtons.Clear();
        spyButtons.Clear();
        tables.Clear();
        dataTables.Clear();
        notices.Clear();
        foreach (var page in pages.Values) Clear(page);
        bulkCities = bulk;
        gold = overview?.Gold ?? 0;

        if (overview == null) ShowNotice(OverviewModel.Stats, "The engine would not show the empire overview.");
        else
        {
            AddCards(OverviewModel.Stats, OverviewModel.StatsCards(overview));
            AddTable(OverviewModel.Stats, OverviewModel.KnownCivs(overview), "Known civilizations");
        }
        BuildCities(OverviewModel.CityTable(snapshot));
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

    /// <summary>C092: the cities tab: the table, whose rows toggle on a click, and below it the bulk actions panel (when there are cities).</summary>
    private void BuildCities(OverviewTable table)
    {
        var page = pages[OverviewModel.Cities];
        var box = BuildTable(OverviewModel.Cities, table, null, name => CityChosen?.Invoke(name), multiSelect: true);
        if (table.Count > 0 && DataTables(OverviewModel.Cities).Count > 0)
        {
            var data = DataTables(OverviewModel.Cities)[0];
            selectedCities.IntersectWith(table.Rows.Select(it => it.Key));
            data.SetSelectedRows(Enumerable.Range(0, table.Count).Where(i => selectedCities.Contains(table.Rows[i].Key)));
            page.AddChild(box);
            page.AddChild(BuildBulkPanel(table, data));
            return;
        }
        page.AddChild(box);
    }

    private IReadOnlyList<BulkCity> SelectedBulk(OverviewTable table) =>
        table.Rows.Where(it => selectedCities.Contains(it.Key)).Select(it => bulkCities.GetValueOrDefault(it.Key)).OfType<BulkCity>().ToList();

    private Control BuildBulkPanel(OverviewTable table, DataTable data)
    {
        var panel = new PanelContainer { Name = "BulkPanel", ThemeTypeVariation = UiVariants.PanelCard, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        box.AddChild(new SectionHeader("Bulk actions"));
        var all = new BulkRow(theme);
        var count = new Label { Name = "BulkCount", VerticalAlignment = VerticalAlignment.Center };
        var selectAll = new Button { Name = "BulkSelectAll", Text = "Select all".Tr(), ThemeTypeVariation = UiVariants.ButtonChip };
        var clear = new Button { Name = "BulkClear", Text = "Clear selection".Tr(), ThemeTypeVariation = UiVariants.ButtonChip };
        selectAll.Pressed += () => data.SetSelectedRows(Enumerable.Range(0, data.RowCount), notify: true);
        clear.Pressed += () => data.SetSelectedRows([], notify: true);
        all.Add(count, selectAll, clear);
        box.AddChild(all);
        var cities = bulkCities.Values.ToList();

        var focusRow = new BulkRow(theme);
        var focus = new OptionButton { Name = "BulkFocus" };
        var focuses = CityFocusExtensions.Entries.Where(it => it.TableEnabled()).ToList();
        foreach (var option in focuses) focus.AddItem(CityScreenModel.FocusLabel(option).Tr().TrimStart('¤', '~'));
        if (!focuses.Contains(bulkFocus)) bulkFocus = focuses[0];
        focus.Selected = focuses.IndexOf(bulkFocus);
        focus.ItemSelected += index => bulkFocus = focuses[(int)index];
        var focusApply = new Button { Name = "BulkFocusApply", Text = "Set focus".Tr() };
        focusApply.Pressed += () => RunBulk(CityBulkModel.SetFocus(SelectedBulk(table), bulkFocus));
        focusRow.Add(new Label { Text = "Citizen focus".Tr(), VerticalAlignment = VerticalAlignment.Center }, focus, focusApply);
        box.AddChild(focusRow);

        var itemRow = new BulkRow(theme);
        var choices = CityBulkModel.QueueChoices(cities);
        var item = new OptionButton { Name = "BulkItem" };
        foreach (string choice in choices) item.AddItem(choice.TrText());
        if (choices.Count == 0) item.AddItem("Nothing to queue".Tr());
        item.Disabled = choices.Count == 0;
        if (!choices.Contains(bulkItem)) bulkItem = choices.Count > 0 ? choices[0] : "";
        item.Selected = Math.Max(0, choices.ToList().IndexOf(bulkItem));
        item.ItemSelected += index => bulkItem = choices[(int)index];
        var queue = new Button { Name = "BulkQueue", Text = "Add to queue".Tr() };
        queue.Pressed += () => RunBulk(CityBulkModel.Queue(SelectedBulk(table), bulkItem));
        var buy = new Button { Name = "BulkBuy", Text = "Buy current production".Tr() };
        buy.Pressed += () => RunBulk(CityBulkModel.BuyCurrent(SelectedBulk(table), gold));
        itemRow.Add(new Label { Text = "Build item".Tr(), VerticalAlignment = VerticalAlignment.Center }, item, queue, buy);
        box.AddChild(itemRow);

        var templates = settings?.Current.BuildTemplates ?? [];
        var templateRow = new BulkRow(theme);
        var template = new OptionButton { Name = "BulkTemplate" };
        foreach (var each in templates) template.AddItem(each.Name);
        if (templates.Count == 0) template.AddItem("No build templates".Tr());
        template.Disabled = templates.Count == 0;
        if (templates.All(it => it.Name != bulkTemplate)) bulkTemplate = templates.Count > 0 ? templates[0].Name : "";
        template.Selected = Math.Max(0, templates.ToList().FindIndex(it => it.Name == bulkTemplate));
        template.ItemSelected += index => bulkTemplate = templates[(int)index].Name;
        var apply = new Button { Name = "BulkTemplateApply", Text = "Apply template".Tr(), Disabled = templates.Count == 0 };
        apply.Pressed += () =>
        {
            if (templates.FirstOrDefault(it => it.Name == bulkTemplate) is { } chosen) RunBulk(CityBulkModel.ApplyTemplate(SelectedBulk(table), chosen));
            else SetBulkResult("Template not found.");
        };
        var delete = new Button { Name = "BulkTemplateDelete", Text = "Delete template".Tr(), ThemeTypeVariation = UiVariants.ButtonDanger, Disabled = templates.Count == 0 };
        delete.Pressed += () => ChangeTemplates(list => CityBulkModel.WithoutTemplate(list, bulkTemplate), $"Deleted the template {bulkTemplate}.");
        templateRow.Add(new Label { Text = "Build template".Tr(), VerticalAlignment = VerticalAlignment.Center }, template, apply, delete);
        box.AddChild(templateRow);

        var saveRow = new BulkRow(theme);
        var name = new LineEdit { Name = "BulkTemplateName", PlaceholderText = "Template name".Tr(), Text = bulkTemplateName, MaxLength = BuildTemplate.MaxNameLength, CustomMinimumSize = new Vector2(theme.Px(160), 0f), Editable = settings != null };
        name.TextChanged += text => bulkTemplateName = text;
        var save = new Button { Name = "BulkTemplateSave", Text = "Save queue as template".Tr(), TooltipText = "Saves the production queue of the first selected city under this name.".Tr(), Disabled = settings == null };
        save.Pressed += () =>
        {
            var from = SelectedBulk(table).FirstOrDefault();
            var made = from == null ? null : CityBulkModel.FromQueue(from.Panel, bulkTemplateName);
            if (made == null) SetBulkResult(from == null ? "Select a city whose queue to save." : bulkTemplateName.Trim().Length == 0 ? "Type a name for the template." : "That city has nothing in its queue.");
            else if (templates.Count >= BuildTemplate.MaxTemplates && templates.All(it => it.Name != made.Name)) SetBulkResult($"At most {BuildTemplate.MaxTemplates.ToString(System.Globalization.CultureInfo.InvariantCulture)} templates: delete one first.");
            else
            {
                bulkTemplate = made.Name;
                ChangeTemplates(list => CityBulkModel.WithTemplate(list, made), $"{(templates.Any(it => it.Name == made.Name) ? "Replaced" : "Saved")} the template {made.Name}.");
            }
        };
        saveRow.Add(name, save);
        box.AddChild(saveRow);

        var result = new Label { Name = "BulkResult", ThemeTypeVariation = UiVariants.LabelSecondary, AutowrapMode = TextServer.AutowrapMode.WordSmart, Text = bulkResult, Visible = bulkResult.Length > 0 };
        box.AddChild(result);
        void Update()
        {
            selectedCities.Clear();
            foreach (int row in data.SelectedRows) selectedCities.Add(table.Rows[row].Key);
            count.Text = $"{"Selected".Tr()}: {selectedCities.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            bool any = selectedCities.Count > 0;
            focusApply.Disabled = !any;
            queue.Disabled = !any || choices.Count == 0;
            buy.Disabled = !any;
            apply.Disabled = !any || templates.Count == 0;
            save.Disabled = !any || settings == null;
        }
        data.SelectionChanged += Update;
        Update();
        panel.AddChild(box);
        return panel;
    }

    /// <summary>A wrapping row of the bulk panel.</summary>
    private sealed partial class BulkRow : HFlowContainer
    {
        public BulkRow(UiTheme theme)
        {
            AddThemeConstantOverride("h_separation", theme.Px(UiTokens.S2));
            AddThemeConstantOverride("v_separation", theme.Px(UiTokens.S1));
        }

        public void Add(params Control[] controls)
        {
            foreach (var control in controls) AddChild(control);
        }
    }

    private void SetBulkResult(string text)
    {
        bulkResult = text;
        if (BulkControl("BulkResult") is Label label)
        {
            label.Text = text;
            label.Visible = text.Length > 0;
        }
    }

    private void ChangeTemplates(Func<IReadOnlyList<BuildTemplate>, List<BuildTemplate>> change, string done)
    {
        if (settings == null || bulkBusy) return;
        settings.Update(s => s with { BuildTemplates = change(s.BuildTemplates) });
        bulkResult = done;
        Fire(RefreshAsync());
    }

    /// <summary>C092: sends the commands of a plan one after the other, as the buttons of the city screen would; the engine validates each and a refused one changes nothing. Then the screen shows what was done and what was not, and asks the engine again.</summary>
    private void RunBulk(BulkPlan plan) => Fire(RunBulkAsync(plan));

    private async Task RunBulkAsync(BulkPlan plan)
    {
        if (bulkBusy)
        {
            SetBulkResult("Working...");
            return;
        }
        if (plan.Steps.Count == 0)
        {
            SetBulkResult("Select a city first.");
            return;
        }
        bulkBusy = true;
        try
        {
            int applied = 0;
            var problems = plan.Skips.Select(it => $"{it.City} ({it.What}): {it.Skipped}").ToList();
            foreach (var step in plan.Commands)
            {
                var result = await session.SubmitAsync(step.Command!);
                if (result.Accepted) applied++;
                else problems.Add($"{step.City} ({step.What}): {result.Message}");
            }
            bulkResult = new BulkOutcome(applied, problems).Text();
            if (applied > 0) CommandAccepted?.Invoke();
            await RefreshAsync();
        }
        finally
        {
            bulkBusy = false;
        }
    }

    private void AddTable(string tab, OverviewTable table, string? title, Action<string>? jump = null) => pages[tab].AddChild(BuildTable(tab, table, title, jump));

    private Control BuildTable(string tab, OverviewTable table, string? title, Action<string>? jump, bool multiSelect = false)
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
        var data = new DataTable(table.Columns.Select(it => new DataColumn(it.Heading, it.Numeric)).ToList(), actions: jump != null, multiSelect: multiSelect) { Name = "Data_" + (title ?? tab) };
        data.SetRows(table.Rows.Select(it => (IReadOnlyList<string>)it.Cells).ToList(), jump == null ? null : index =>
        {
            string key = table.Rows[index].Key;
            var button = new IconButton(theme, AssetKey.Of(RulesetAssetKeys.UiIcon, "ArrowRight"), "Go to") { Name = $"Jump_{tab}_{key}", CustomMinimumSize = new Vector2(theme.MinTargetPx, theme.MinTargetPx) };
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
        CustomMinimumSize = new Vector2(theme.Px(112), 0f),
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
        messageText = text;
        message.Text = text;
        message.Visible = text.Length > 0;
    }
}
