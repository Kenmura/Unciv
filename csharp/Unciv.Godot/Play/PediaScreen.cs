using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Ruleset;
using Unciv.Models.Translations;
using Unciv.UI.Screens.Civilopedia;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C027: the Civilopedia; C076: on a <see cref="ScreenFrame"/> with the search box in the header. A sidebar of categories (icon and name; icons only at the Compact breakpoint),
/// the category's entries as list rows (with icons where the registry has them), and the selected entry's page: a header card (icon, name, category, cost chips) and the engine's
/// own text, its section headings as <see cref="SectionHeader"/>s and its links in the info colour (underlined on hover). A link inside a page opens the linked entry, Back returns
/// to the previous one. Everything shown is <see cref="PediaModel"/>, i.e. the engine's own text for the ruleset objects; the screen reads only the static
/// <see cref="Ruleset"/> of the session and sends nothing.
/// </summary>
public partial class PediaScreen : PanelContainer
{
    private readonly Ruleset ruleset;
    private readonly UiTheme theme;
    private readonly TooltipHost tooltips;
    private readonly LineEdit search = new() { Name = "PediaSearch", PlaceholderText = "Search".Tr(), ClearButtonEnabled = true };
    private readonly Button back = new() { Name = "PediaBack", Text = "Back".Tr(), Disabled = true, ThemeTypeVariation = UiVariants.ButtonGhost, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
    private readonly List<PediaEntry> shown = [];
    private readonly Stack<PediaEntry> history = new();
    private readonly Dictionary<CivilopediaCategories, Button> categoryButtons = [];
    private readonly List<PediaEntry> found = [];
    private readonly List<ListRow> entryRows = [];
    private readonly List<ListRow> searchRows = [];
    private readonly List<RichTextLabel> labels = [];
    private readonly List<SectionHeader> headers = [];
    private ScreenFrame frame = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private ScrollContainer entryScroll = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private ScrollContainer searchScroll = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private ScrollContainer pageScroll = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private VBoxContainer sidebar = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private VBoxContainer entryColumn = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private VBoxContainer searchColumn = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private VBoxContainer pageColumn = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private PanelContainer card = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private TextureRect cardIcon = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private Label cardName = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private Label cardCategory = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private HFlowContainer cardChips = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private VBoxContainer body = null!; // JUSTIFY: C076 assigned by BuildFrame in the constructor
    private PediaEntry? current;

    public PediaScreen(Ruleset gameRuleset, UiTheme uiTheme, TooltipHost tooltipHost)
    {
        ruleset = gameRuleset;
        tooltips = tooltipHost;
        theme = uiTheme;
        Name = "PediaPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        Theme = uiTheme.Theme;
        search.TextChanged += ShowSearch;
        search.TextSubmitted += _ => OpenFirstResult();
        search.GuiInput += OnSearchInput;
        back.Pressed += Back;
        BuildFrame();
    }

    public override void _EnterTree() => theme.LayoutChanged += OnLayoutChanged;

    public override void _ExitTree() => theme.LayoutChanged -= OnLayoutChanged;

    /// <summary>Sizes are fixed when a view is built, so a change of scale or breakpoint builds the frame again and shows the same entry.</summary>
    private void OnLayoutChanged()
    {
        BuildFrame();
        var at = current;
        if (at == null) return;
        current = null;
        Show(at, remember: false);
        if (search.Text.Length > 0) ShowSearch(search.Text);
    }

    private void BuildFrame()
    {
        if (frame != null)
        {
            search.GetParent()?.RemoveChild(search);
            back.GetParent()?.RemoveChild(back);
            RemoveChild(frame);
            frame.QueueFree();
        }
        categoryButtons.Clear();
        entryRows.Clear();
        searchRows.Clear();
        labels.Clear();
        headers.Clear();
        Category = null;
        bool compact = theme.Breakpoint == UiBreakpoint.Compact;
        int gap = theme.Px(UiTokens.S3);
        frame = new ScreenFrame(theme, "Civilopedia", closeLabel: "Close");
        frame.CloseRequested += Close;
        search.CustomMinimumSize = new Vector2(theme.Px(120), 0);
        frame.SetHeaderStrip(search);
        AddChild(frame);

        var middle = new HBoxContainer();
        middle.AddThemeConstantOverride("separation", gap);

        var sideScroll = new ScrollContainer { Name = "PediaSidebar", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(theme.Px(compact ? 44 : 180), 0) };
        sidebar = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sidebar.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        sideScroll.AddChild(sidebar);
        middle.AddChild(sideScroll);
        var group = new ButtonGroup();
        foreach (var category in PediaModel.Categories(ruleset))
        {
            var chosen = category.Category;
            var icon = PediaModel.CategoryIcon(chosen);
            Button button;
            if (compact)
            {
                button = new Button { Name = "PediaCategory_" + chosen, ToggleMode = true, ButtonGroup = group, ThemeTypeVariation = UiVariants.ButtonIcon, Icon = theme.Icon(icon), TooltipText = category.Label.Tr() };
                button.AddThemeConstantOverride("icon_max_width", theme.Px(UiTheme.IconSize));
                button.SetMeta(IconButton.LabelMeta, category.Label);
            }
            else
            {
                button = new ListRow(theme, category.Label, icon: icon, group: group) { Name = "PediaCategory_" + chosen };
            }
            button.Pressed += () => ShowCategory(chosen);
            categoryButtons[chosen] = button;
            sidebar.AddChild(button);
        }

        var listWidth = new Vector2(theme.Px(compact ? 170 : 230), 0);
        entryScroll = new ScrollContainer { Name = "PediaEntries", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = listWidth };
        entryColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        entryColumn.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        entryScroll.AddChild(entryColumn);
        searchScroll = new ScrollContainer { Name = "PediaSearchResults", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = listWidth, Visible = false };
        searchColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        searchColumn.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        searchScroll.AddChild(searchColumn);
        middle.AddChild(entryScroll);
        middle.AddChild(searchScroll);

        pageScroll = new ScrollContainer { Name = "PediaPageScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        pageColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        pageColumn.AddThemeConstantOverride("separation", gap);
        pageScroll.AddChild(pageColumn);
        middle.AddChild(pageScroll);
        pageColumn.AddChild(back);

        card = new PanelContainer { Name = "PediaCard", ThemeTypeVariation = UiVariants.PanelCard, Visible = false };
        var cardRow = new HBoxContainer();
        cardRow.AddThemeConstantOverride("separation", gap);
        cardIcon = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.LinearWithMipmaps, CustomMinimumSize = new Vector2(theme.Px(compact ? 48 : 64), theme.Px(compact ? 48 : 64)), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        cardRow.AddChild(cardIcon);
        var texts = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        texts.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        cardName = new Label { Name = "PediaName", ThemeTypeVariation = UiVariants.LabelHeading, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        cardCategory = new Label { Name = "PediaCategoryLabel", ThemeTypeVariation = UiVariants.LabelSecondary };
        cardChips = new HFlowContainer();
        cardChips.AddThemeConstantOverride("h_separation", gap);
        texts.AddChild(cardName);
        texts.AddChild(cardCategory);
        texts.AddChild(cardChips);
        cardRow.AddChild(texts);
        card.AddChild(cardRow);
        pageColumn.AddChild(card);
        body = new VBoxContainer { Name = "PediaBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        pageColumn.AddChild(body);
        frame.SetContent(middle);
    }

    public CivilopediaCategories? Category { get; private set; }

    /// <summary>The entries of the category now shown, in list order.</summary>
    public IReadOnlyList<PediaEntry> Entries => shown;

    /// <summary>The entry whose page is shown, or null.</summary>
    public PediaEntry? Current => current;

    /// <summary>The BBCode of the shown page, every line of it (the body below the header card is drawn from the same lines).</summary>
    public string PageBbcode { get; private set; } = "";

    /// <summary>The visible text of the shown page: the header card's name, then the section headings and text blocks in order.</summary>
    public string PageText => string.Join('\n', new[] { cardName.Text }.Concat(body.GetChildren().Select(it => it switch
    {
        RichTextLabel label => label.GetParsedText(),
        SectionHeader header => header.ShownText,
        _ => "",
    })));

    /// <summary>C076: the section headings of the shown page, as drawn.</summary>
    public IReadOnlyList<SectionHeader> SectionHeaders => headers;

    /// <summary>C076: the text blocks of the shown page, as drawn.</summary>
    public IReadOnlyList<RichTextLabel> TextBlocks => labels;

    /// <summary>C076: the header card of the shown page (icon, name, category, chips).</summary>
    public Control HeaderCard => card;

    public string CardName => cardName.Text;

    public string CardCategory => cardCategory.Text;

    /// <summary>The stats of the card's chips with their shown values, in order.</summary>
    public IReadOnlyList<(string Stat, string Value)> CardChips => cardChips.GetChildren().OfType<StatChip>().Select(it => (it.Stat, it.Value)).ToList();

    public IReadOnlyCollection<CivilopediaCategories> CategoryKeys => categoryButtons.Keys;

    /// <summary>C055: the search box (type to list the entries whose name contains the text, over every category).</summary>
    public LineEdit SearchBox => search;

    /// <summary>The entries the search lists now, best first (empty when the box is empty).</summary>
    public IReadOnlyList<PediaEntry> SearchResults => found;

    public Button? SearchResultButton(string name)
    {
        int index = found.FindIndex(it => it.Name == name);
        return index < 0 || index >= searchRows.Count ? null : searchRows[index];
    }

    public bool CanGoBack => history.Count > 0;

    public event Action? Closed;

    public Button? CategoryButton(CivilopediaCategories category) => categoryButtons.GetValueOrDefault(category);

    public Button? BackButton => back;

    public Button? EntryButton(string name)
    {
        int index = shown.FindIndex(it => it.Name == name);
        return index < 0 || index >= entryRows.Count ? null : entryRows[index];
    }

    /// <summary>The frame's own close button and title, for the harness.</summary>
    public ScreenFrame Frame => frame;

    /// <summary>Shows the screen on a category (the first with entries when null) or, with <paramref name="link"/>, on that entry.</summary>
    public void Open(string? link = null)
    {
        Visible = true;
        history.Clear();
        back.Disabled = true;
        if (link != null && PediaModel.Resolve(ruleset, link) is { } target) Show(target.Entry, remember: false);
        else if (current == null) ShowCategory(categoryButtons.Keys.First());
    }

    /// <summary>C055: sets the search text as typing would and lists the matches in place of the category's entries.</summary>
    public void SearchFor(string text)
    {
        search.Text = text;
        ShowSearch(text);
    }

    private void ShowSearch(string text)
    {
        found.Clear();
        found.AddRange(PediaModel.Search(ruleset, text));
        searchRows.Clear();
        foreach (var child in searchColumn.GetChildren())
        {
            searchColumn.RemoveChild(child);
            child.QueueFree();
        }
        bool searching = text.Trim().Length != 0;
        entryScroll.Visible = !searching;
        searchScroll.Visible = searching;
        if (!searching) return;
        if (found.Count == 0)
        {
            searchColumn.AddChild(new EmptyState(theme, null, "No results"));
            return;
        }
        var group = new ButtonGroup();
        for (int i = 0; i < found.Count; i++)
        {
            var entry = found[i];
            // the category tells same-named entries apart
            var row = new ListRow(theme, entry.Name, entry.Category.Label(), icon: entry.Icon, group: group);
            row.Pressed += () => OpenResult(entry);
            searchRows.Add(row);
            searchColumn.AddChild(row);
        }
    }

    private void OpenFirstResult()
    {
        if (found.Count > 0) OpenResult(found[0]);
    }

    private void OpenResult(PediaEntry entry)
    {
        search.Text = "";
        ShowSearch("");
        Show(entry, remember: true);
    }

    /// <summary>Escape in the box clears it, or closes the Civilopedia when it is already empty (the line edit would otherwise swallow the key).</summary>
    private void OnSearchInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Keycode: Key.Escape }) return;
        search.AcceptEvent();
        if (search.Text.Length > 0) SearchFor("");
        else Close();
    }

    public void Close()
    {
        if (search.Text.Length > 0) SearchFor("");
        search.ReleaseFocus();
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

    /// <summary>Follows a page link (<c>Category/Name</c>); anything that is not a pedia entry (an external address, a removed entry) is ignored.</summary>
    public void OpenLink(string link)
    {
        if (PediaModel.Resolve(ruleset, link) is { } target) Show(target.Entry, remember: true);
    }

    public void Back()
    {
        if (history.Count == 0) return;
        Show(history.Pop(), remember: false);
        back.Disabled = history.Count == 0;
    }

    public void ShowCategory(CivilopediaCategories category)
    {
        var entries = PediaModel.Entries(ruleset, category);
        if (entries.Count == 0) return;
        Show(entries[0], remember: false);
    }

    private void Show(PediaEntry entry, bool remember)
    {
        if (remember && current != null && current.Link != entry.Link)
        {
            history.Push(current);
            back.Disabled = false;
        }
        if (Category != entry.Category) ShowList(entry.Category);
        current = entry;
        int index = shown.FindIndex(it => it.Link == entry.Link);
        if (index >= 0 && index < entryRows.Count) entryRows[index].SetPressedNoSignal(true);
        var content = PediaModel.Page(ruleset, entry.Category, entry.Name);
        PageBbcode = content == null ? "" : string.Join('\n', content.Lines.Select(it => PediaModel.ToBbcode(it, text => text.Tr())));
        ShowCard(entry);
        ShowBody(content);
        pageScroll.ScrollVertical = 0;
    }

    private void ShowCard(PediaEntry entry)
    {
        var header = PediaModel.Header(ruleset, entry);
        card.Visible = true;
        cardIcon.Texture = theme.Icon(header.Icon ?? PediaModel.CategoryIcon(entry.Category));
        cardName.Text = header.Name.TrText();
        cardCategory.Text = header.CategoryLabel.TrText();
        foreach (var chip in cardChips.GetChildren())
        {
            cardChips.RemoveChild(chip);
            chip.QueueFree();
        }
        foreach (var chip in header.Chips) cardChips.AddChild(new StatChip(theme, chip.Stat, chip.Text));
    }

    /// <summary>The page's lines below the card: a heading line (that is not a link) becomes a section header, a run of other lines one text block. The page's own title line is the card's name.</summary>
    private void ShowBody(PediaPage? page)
    {
        foreach (var child in body.GetChildren())
        {
            body.RemoveChild(child);
            child.QueueFree();
        }
        labels.Clear();
        headers.Clear();
        if (page == null) return;
        var run = new List<string>();
        void Flush()
        {
            if (run.Count == 0) return;
            AddBlock(string.Join('\n', run));
            run.Clear();
        }
        for (int i = 0; i < page.Lines.Count; i++)
        {
            var line = page.Lines[i];
            if (i == 0 && line.Header > 0 && line.Text == page.Entry.Name) continue;
            if (line.Header > 0 && line.Link.Length == 0 && line.Text.Length > 0 && i > 0)
            {
                Flush();
                var header = new SectionHeader(line.Text);
                headers.Add(header);
                body.AddChild(header);
            }
            else
            {
                run.Add(PediaModel.ToBbcode(line, text => text.Tr()));
            }
        }
        Flush();
    }

    private void AddBlock(string bbcode)
    {
        var label = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SelectionEnabled = true, MetaUnderlined = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        label.Text = bbcode;
        label.MetaClicked += meta => OpenLink(meta.AsString());
        label.MetaHoverStarted += _ => label.MetaUnderlined = true;
        label.MetaHoverEnded += _ => label.MetaUnderlined = false;
        labels.Add(label);
        body.AddChild(label);
    }

    private void ShowList(CivilopediaCategories category)
    {
        Category = category;
        foreach (var (key, button) in categoryButtons) button.SetPressedNoSignal(key == category);
        shown.Clear();
        shown.AddRange(PediaModel.Entries(ruleset, category));
        entryRows.Clear();
        foreach (var child in entryColumn.GetChildren())
        {
            entryColumn.RemoveChild(child);
            child.QueueFree();
        }
        var group = new ButtonGroup();
        string? subCategory = null;
        for (int i = 0; i < shown.Count; i++)
        {
            var entry = shown[i];
            if (entry.SubCategory != null && entry.SubCategory != subCategory)
            {
                subCategory = entry.SubCategory;
                entryColumn.AddChild(new SectionHeader(subCategory));
            }
            var row = new ListRow(theme, entry.Name, icon: entry.Icon, group: group);
            row.Pressed += () => Show(entry, remember: true);
            if (TipOf(entry) is { } tip) tooltips.Attach(row, () => tip);
            entryRows.Add(row);
            entryColumn.AddChild(row);
        }
    }

    /// <summary>The hover text of a list entry: units and buildings (wonders too) show their cost and the ruleset's short description; other categories have none.</summary>
    private string? TipOf(PediaEntry entry)
    {
        var lines = entry.Category switch
        {
            CivilopediaCategories.Unit => TooltipModel.Unit(ruleset, entry.Name),
            CivilopediaCategories.Building or CivilopediaCategories.Wonder => TooltipModel.Building(ruleset, entry.Name),
            _ => null,
        };
        return lines == null ? null : TooltipModel.ToBbcode(lines, text => text.Tr());
    }
}
