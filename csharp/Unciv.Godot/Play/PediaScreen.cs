using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Models.Ruleset;
using Unciv.Models.Translations;
using Unciv.UI.Screens.Civilopedia;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C027: the Civilopedia. A row of category buttons, the category's entries on the left (with icons where the registry has them) and the selected entry's page on the right.
/// Everything shown is <see cref="PediaModel"/>, i.e. the engine's own text for the ruleset objects; a link inside a page opens the linked entry, Back returns to the previous one.
/// The screen reads only the static <see cref="Ruleset"/> of the session and sends nothing.
/// </summary>
public partial class PediaScreen : PanelContainer
{
    private readonly Ruleset ruleset;
    private readonly UiTheme theme;
    private readonly TooltipHost tooltips;
    private readonly HBoxContainer categoryRow = new() { Name = "PediaCategories" };
    private readonly HBoxContainer body = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly RichTextLabel page = new() { Name = "PediaPage", BbcodeEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, SelectionEnabled = true };
    private readonly Button back = new() { Name = "PediaBack", Text = "Back".Tr(), Disabled = true };
    private readonly List<PediaEntry> shown = [];
    private readonly Stack<PediaEntry> history = new();
    private readonly Dictionary<CivilopediaCategories, Button> categoryButtons = [];
    private ListPanel? entryList;
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

        var column = new VBoxContainer();
        AddChild(column);
        var head = new HBoxContainer();
        column.AddChild(head);
        var heading = new Label { Text = "Civilopedia".Tr(), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddThemeFontSizeOverride("font_size", uiTheme.Px(20));
        head.AddChild(heading);
        back.Pressed += Back;
        head.AddChild(back);
        var close = new Button { Name = "PediaClose", Text = "Close".Tr() };
        close.Pressed += Close;
        head.AddChild(close);
        var categoryScroll = new ScrollContainer { VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, CustomMinimumSize = new Vector2(0, uiTheme.Px(40)) };
        categoryScroll.AddChild(categoryRow);
        column.AddChild(categoryScroll);
        column.AddChild(body);
        page.MetaClicked += meta => OpenLink(meta.AsString());
        page.CustomMinimumSize = new Vector2(uiTheme.Px(360), 0);
        foreach (var category in PediaModel.Categories(ruleset))
        {
            var button = new Button { Name = "PediaCategory_" + category.Category, Text = category.Label.Tr(), ToggleMode = true };
            var chosen = category.Category;
            button.Pressed += () => ShowCategory(chosen);
            categoryButtons[chosen] = button;
            categoryRow.AddChild(button);
        }
    }

    public CivilopediaCategories? Category { get; private set; }

    /// <summary>The entries of the category now shown, in list order.</summary>
    public IReadOnlyList<PediaEntry> Entries => shown;

    /// <summary>The entry whose page is shown, or null.</summary>
    public PediaEntry? Current => current;

    /// <summary>The BBCode of the shown page (before the label turns it into plain text).</summary>
    public string PageBbcode { get; private set; } = "";

    /// <summary>The visible text of the shown page.</summary>
    public string PageText => page.GetParsedText();

    public IReadOnlyCollection<CivilopediaCategories> CategoryKeys => categoryButtons.Keys;

    public bool CanGoBack => history.Count > 0;

    public event Action? Closed;

    public Button? CategoryButton(CivilopediaCategories category) => categoryButtons.GetValueOrDefault(category);

    public Button? BackButton => back;

    public Button? EntryButton(string name)
    {
        int index = shown.FindIndex(it => it.Name == name);
        return index < 0 ? null : entryList?.ItemAt(index);
    }

    /// <summary>The label that draws the page (tests click its links through <see cref="OpenLink"/>).</summary>
    public RichTextLabel PageLabel => page;

    /// <summary>Shows the screen on a category (the first with entries when null) or, with <paramref name="link"/>, on that entry.</summary>
    public void Open(string? link = null)
    {
        Visible = true;
        history.Clear();
        back.Disabled = true;
        if (link != null && PediaModel.Resolve(ruleset, link) is { } target) Show(target.Entry, remember: false);
        else if (current == null) ShowCategory(categoryButtons.Keys.First());
    }

    public void Close()
    {
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
        if (index >= 0) entryList?.ItemAt(index)?.SetPressedNoSignal(true);
        var content = PediaModel.Page(ruleset, entry.Category, entry.Name);
        PageBbcode = content == null ? "" : string.Join('\n', content.Lines.Select(it => PediaModel.ToBbcode(it, text => text.Tr())));
        page.Text = PageBbcode;
        page.ScrollToLine(0);
    }

    private void ShowList(CivilopediaCategories category)
    {
        Category = category;
        foreach (var (key, button) in categoryButtons) button.SetPressedNoSignal(key == category);
        shown.Clear();
        shown.AddRange(PediaModel.Entries(ruleset, category));
        if (entryList != null)
        {
            body.RemoveChild(entryList);
            entryList.QueueFree();
        }
        entryList = new ListPanel(theme, category.Label(), new Vector2(theme.Px(240), 0)) { Name = "PediaEntries" };
        string? subCategory = null;
        for (int i = 0; i < shown.Count; i++)
        {
            var entry = shown[i];
            if (entry.SubCategory != null && entry.SubCategory != subCategory)
            {
                subCategory = entry.SubCategory;
                entryList.AddRow(new Label { Text = subCategory.Tr() });
            }
            entryList.AddItem(entry.Name.Tr(), entry.Icon);
            if (TipOf(entry) is { } tip && entryList.ItemAt(i) is { } item) tooltips.Attach(item, () => tip);
        }
        entryList.Selected += index => Show(shown[index], remember: true);
        body.AddChild(entryList);
        body.MoveChild(entryList, 0);
        if (body.GetChildCount() == 1) body.AddChild(page);
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
