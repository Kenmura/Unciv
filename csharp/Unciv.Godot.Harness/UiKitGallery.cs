using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Ui;
using TabBar = Unciv.GodotClient.Ui.TabBar;

namespace Unciv.GodotHarness;

/// <summary>C065: the component pages and steps of the ui-kit capture; every page builds fresh controls and the properties point at the latest ones.</summary>
internal sealed class UiKitGallery(UiTheme theme, List<string> log)
{
    public IEnumerable<ScenarioStep> Steps(ScreenManager manager, Control host) =>
    [
        new Act("UI scale back to 100%, push the controls page", () =>
        {
            theme.SetScale(1f);
            log.Clear();
            manager.Push(ControlsPage());
        }),
        new WaitFrames(3),
        new AssertQuery("badges: 5 shows 5, 120 shows 99+, 0 hides, 12 shows 12", BadgeChecks),
        new AssertQuery("icon buttons carry their label and a Label (Key) tooltip", () => Close.Label == "Close" && Close.TooltipText == "Close (Esc)" && Next.TooltipText == "Next unit" && Close.Text == ""),
        new Press("Close"),
        new AssertQuery("an icon-only button is pressed by its Label", () => log.SequenceEqual(["close"])),
        new AssertQuery("stepper starts inside its range with both buttons enabled", () => Players.Value == 3 && !Players.Minus.Disabled && !Players.Plus.Disabled && Cities.Minus.Disabled),
        new Press("More players"),
        new Press("More players"),
        new Press("More players"),
        new AssertQuery("stepper clamps at its maximum and disables plus", () => Players.Value == 6 && Players.Plus.Disabled && PlayersChanged.SequenceEqual([4, 5, 6])),
        new Act("assign far outside the range, both ways", () =>
        {
            Players.Value = 99;
            Players.Value = -5;
        }),
        new AssertQuery("above the range changes nothing, below clamps to the minimum", () => Players.Value == 2 && Players.Minus.Disabled && PlayersChanged.SequenceEqual([4, 5, 6, 2]) && Players.ShownText == "2"),
        new Act("back to three players for the capture", () => Players.Value = 3),
        new Press("Medium"),
        new AssertQuery("segmented control reports and shows its choice", () => Size.Selected == "Medium" && SizeChosen.SequenceEqual(["Medium"])),
        new AssertQuery("chip deltas follow their sign: +5 green, -2 red, 0 muted", DeltaChecks),
        new AssertQuery("stat bars: thin 60% and labelled 30% with their captions and coloured fills", BarChecks),
        new AssertQuery("section header upper-cases, stat glyphs become icons, key hints show their key", TextChecks),
        new AssertQuery("a tab bar wider than its room shows scroll arrows and keeps the selected tab visible", () => Tabs.Overflowing && Tabs.SelectedIndex == 0),
        new Capture("components", "Every control component: icon buttons (plain, with a red count badge, primary with a gold count badge, disabled), two steppers (the second at its minimum), a segmented control, stat chips with signed deltas, thin and labelled stat bars, inline stat icons, key hints and a scrolling tab bar."),
        new Act("swap to the data page", () =>
        {
            manager.Pop();
            manager.Push(DataPage());
        }),
        new WaitFrames(3),
        new AssertQuery("list rows: the disabled one is greyed with its reason in place of the subtitle", RowChecks),
        new AssertQuery("data table starts in input order, numbers right-aligned on one edge, text left", TableStartsRight),
        new Capture("components-data", "List rows (selected with the accent bar, a subtitle and trailing value, disabled with its reason), a data table with right-aligned tabular numbers and zebra rows, and an empty state with its action."),
        new Press("Settler"),
        new AssertQuery("pressing a row selects it and deselects the other", () => Settler.ButtonPressed && !Warrior.ButtonPressed && RowsSelected.SequenceEqual(["Settler"])),
        new Press("City"),
        new AssertQuery("a text header sorts ascending", () => Table.SortColumn == 0 && Table.SortAscending && Table.Order.SequenceEqual([1, 2, 0])),
        new Press("City ▲"),
        new AssertQuery("the same header again sorts descending", () => !Table.SortAscending && Table.Order.SequenceEqual([0, 2, 1])),
        new Press("Pop"),
        new AssertQuery("a number header sorts by value, not by text (8, 12, 20)", () => Table.SortColumn == 1 && Table.Order.SequenceEqual([1, 0, 2]) && Table.CellText(2, 1) == "20"),
        new Press("Net"),
        new AssertQuery("signed numbers sort by value (-2, +4, +30) and stay right-aligned", () => Table.Order.SequenceEqual([2, 1, 0]) && TableNumbersAligned()),
        new Press("Save game"),
        new AssertQuery("the empty state's action is pressed", () => log.SequenceEqual(["close", "save"])),
        new Act("wide window: both pages side by side at 1280x720", () =>
        {
            manager.Pop();
            host.GetWindow().Size = new Vector2I(1280, 720);
        }),
        new WaitFrames(3),
        new Act("push the wide page", () => manager.Push(WidePage())),
        new WaitFrames(3),
        new AssertQuery("the viewport is 1280x720 and the tab bar still overflows only when it must", () => host.GetViewport().GetVisibleRect().Size == new Vector2(1280, 720) && Tabs.Overflowing),
        new Capture("wide", "The same components at 1280x720: the control page and the data page side by side."),
    ];

    private IconButton? close;
    public IconButton Close => close ?? throw new InvalidOperationException("page not built");

    private IconButton? notifications;
    public IconButton Notifications => notifications ?? throw new InvalidOperationException("page not built");

    private IconButton? next;
    public IconButton Next => next ?? throw new InvalidOperationException("page not built");

    private Stepper? players;
    public Stepper Players => players ?? throw new InvalidOperationException("page not built");

    private Stepper? cities;
    public Stepper Cities => cities ?? throw new InvalidOperationException("page not built");

    private Segmented? size;
    public Segmented Size => size ?? throw new InvalidOperationException("page not built");

    private StatChip? gold;
    public StatChip Gold => gold ?? throw new InvalidOperationException("page not built");

    private StatChip? science;
    public StatChip Science => science ?? throw new InvalidOperationException("page not built");

    private StatChip? culture;
    public StatChip Culture => culture ?? throw new InvalidOperationException("page not built");

    private StatBar? thin;
    public StatBar Thin => thin ?? throw new InvalidOperationException("page not built");

    private StatBar? labelled;
    public StatBar Labelled => labelled ?? throw new InvalidOperationException("page not built");

    private SectionHeader? header;
    public SectionHeader Header => header ?? throw new InvalidOperationException("page not built");

    private StatText? glyphs;
    public StatText Glyphs => glyphs ?? throw new InvalidOperationException("page not built");

    private KeyHint? hint;
    public KeyHint Hint => hint ?? throw new InvalidOperationException("page not built");

    private TabBar? tabs;
    public TabBar Tabs => tabs ?? throw new InvalidOperationException("page not built");

    private ListRow? warrior;
    public ListRow Warrior => warrior ?? throw new InvalidOperationException("page not built");

    private ListRow? settler;
    public ListRow Settler => settler ?? throw new InvalidOperationException("page not built");

    private ListRow? galleon;
    public ListRow Galleon => galleon ?? throw new InvalidOperationException("page not built");

    private DataTable? table;
    public DataTable Table => table ?? throw new InvalidOperationException("page not built");

    public List<int> PlayersChanged { get; } = [];

    public List<string> SizeChosen { get; } = [];

    public List<string> RowsSelected { get; } = [];

    private static Color Tone(Rgb rgb) => ProceduralAssets.ToColor(rgb);

    private static AssetKey Ui(string name) => AssetKey.Of(RulesetAssetKeys.UiIcon, name);

    private MarginContainer Page(Control content)
    {
        var page = new MarginContainer();
        foreach (string side in new[] { "left", "top", "right", "bottom" }) page.AddThemeConstantOverride("margin_" + side, theme.Px(UiTokens.S2));
        page.AddChild(content);
        return page;
    }

    private static HBoxContainer Row(params Control[] children)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        foreach (var child in children) row.AddChild(child);
        return row;
    }

    public Control ControlsPage() => Page(ControlsColumn());

    public Control DataPage() => Page(DataColumn());

    public Control WidePage()
    {
        var both = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        both.AddThemeConstantOverride("separation", theme.Px(UiTokens.S6));
        var controls = ControlsColumn();
        controls.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var data = DataColumn();
        data.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        both.AddChild(controls);
        both.AddChild(data);
        return Page(both);
    }

    private VBoxContainer ControlsColumn()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        column.AddChild(new SectionHeader("Buttons"));
        close = new IconButton(theme, Ui("Close"), "Close", hotkey: "Esc");
        Close.Pressed += () => log.Add("close");
        notifications = new IconButton(theme, Ui("Notifications"), "Notifications");
        Notifications.SetBadge(5);
        next = new IconButton(theme, Ui("ArrowRight"), "Next unit", "Next unit", variation: UiVariants.ButtonPrimary);
        Next.SetBadge(120, accent: true);
        var disabled = new IconButton(theme, Ui("Undo"), "Undo") { Disabled = true };
        column.AddChild(Row(Close, Notifications, disabled, Next));

        column.AddChild(new SectionHeader("Inputs"));
        players = new Stepper(theme, 2, 6, 3, decreaseLabel: "Fewer players", increaseLabel: "More players");
        PlayersChanged.Clear();
        Players.Changed += PlayersChanged.Add;
        cities = new Stepper(theme, 0, 8, 0, decreaseLabel: "Fewer city-states", increaseLabel: "More city-states");
        size = new Segmented(["Tiny", "Small", "Medium"], "Small");
        SizeChosen.Clear();
        Size.Chosen += SizeChosen.Add;
        column.AddChild(Row(Players, Cities, Size));

        column.AddChild(new SectionHeader("Stats"));
        gold = new StatChip(theme, "Gold", "500", 5);
        science = new StatChip(theme, "Science", "12", -2);
        culture = new StatChip(theme, "Culture", "3", 0);
        column.AddChild(Row(Gold, Science, Culture));
        thin = new StatBar(theme, "Food", 12, 20, caption: "5 turns");
        labelled = new StatBar(theme, "Production", 30, 100, labelled: true, caption: "30/100");
        column.AddChild(Thin);
        column.AddChild(Labelled);
        glyphs = new StatText(theme, "3 ¤ +2 ⚙");
        hint = new KeyHint("T");
        column.AddChild(Row(Glyphs, Hint, new KeyHint("Esc")));

        header = new SectionHeader("Production queue");
        column.AddChild(Header);
        tabs = new TabBar { CustomMinimumSize = new Vector2(theme.Px(240), 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        foreach (string tab in new[] { "Overview", "Cities", "Units", "Policies", "Diplomacy", "Trade", "Espionage", "Civilopedia" }) Tabs.AddTab(tab, new Label { Text = "Tab content" });
        column.AddChild(Tabs);
        return column;
    }

    private VBoxContainer DataColumn()
    {
        var column = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        column.AddChild(new SectionHeader("Units"));
        var group = new ButtonGroup();
        RowsSelected.Clear();
        warrior = new ListRow(theme, "Warrior", "Strength 6", "40", AssetKey.Of(RulesetAssetKeys.UnitIcon, "Warrior"), group: group) { ButtonPressed = true };
        settler = new ListRow(theme, "Settler", "Founds a city", "89", AssetKey.Of(RulesetAssetKeys.UnitIcon, "Settler"), group: group);
        galleon = new ListRow(theme, "Galleon", trailing: "120", icon: AssetKey.Of(RulesetAssetKeys.UnitIcon, "Galleon"), disabledReason: "Requires Navigation", group: group);
        foreach (var row in new[] { Warrior, Settler, Galleon })
        {
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.Pressed += () => RowsSelected.Add(row.Title);
            column.AddChild(row);
        }
        column.AddChild(new SectionHeader("Cities"));
        table = new DataTable([new DataColumn("City"), new DataColumn("Pop", true), new DataColumn("Net", true)]);
        Table.SetRows([["Rome", "12", "+30"], ["Athens", "8", "+4"], ["Babylon", "20", "-2"]]);
        column.AddChild(Table);
        var empty = new EmptyState(theme, Ui("Load"), "No saves yet.", "Save game") { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        empty.ActionPressed += () => log.Add("save");
        column.AddChild(empty);
        return column;
    }

    public bool BadgeChecks()
    {
        bool shown = Notifications.CountBadge is { Text: "5", Visible: true } && Next.CountBadge is { Text: "99+", Visible: true };
        Next.SetBadge(0);
        bool hidden = Next.CountBadge is { Visible: false, Text: "" };
        Next.SetBadge(12, accent: true);
        return shown && hidden && Next.CountBadge is { Text: "12", Visible: true };
    }

    public bool DeltaChecks() =>
        Gold.DeltaText == "+5" && Gold.DeltaColour == Tone(UiTokens.Success)
        && Science.DeltaText == "-2" && Science.DeltaColour == Tone(UiTokens.DangerText)
        && Culture.DeltaText == "0" && Culture.DeltaColour == Tone(UiTokens.TextMuted);

    public bool BarChecks() =>
        Math.Abs(Thin.Fraction - 0.6) < 1e-9 && Thin.Caption == "5 turns" && Thin.Bar.HasThemeStyleboxOverride("fill")
        && Math.Abs(Labelled.Fraction - 0.3) < 1e-9 && Labelled.Caption == "30/100" && Labelled.Bar.ThemeTypeVariation == UiVariants.StatBarLabelled
        && Thin.Bar.ThemeTypeVariation == UiVariants.StatBarThin;

    public bool TextChecks() =>
        Header.ShownText == "PRODUCTION QUEUE" && Glyphs.Stats.SequenceEqual(["Gold", "Production"]) && Hint.Key == "T";

    public bool RowChecks() =>
        Galleon.Disabled && Galleon.DisabledReason == "Requires Navigation" && Galleon.ShownSubtitle == "Requires Navigation" && Galleon.ShownTrailing == "120"
        && Warrior.ShownSubtitle == "Strength 6" && Warrior.ShownTrailing == "40" && Warrior.ButtonPressed && !Warrior.Disabled;

    public bool TableStartsRight()
    {
        if (!Table.Order.SequenceEqual([0, 1, 2]) || Table.SortColumn != -1) return false;
        return Table.Cell(0, 0).HorizontalAlignment == HorizontalAlignment.Left && TableNumbersAligned();
    }

    /// <summary>Every numeric cell is right-aligned and the cells of one column end on the same x.</summary>
    public bool TableNumbersAligned()
    {
        foreach (int column in new[] { 1, 2 })
        {
            float edge = Table.Cell(0, column).GetGlobalRect().End.X;
            for (int row = 0; row < Table.RowCount; row++)
            {
                var cell = Table.Cell(row, column);
                if (cell.HorizontalAlignment != HorizontalAlignment.Right || Math.Abs(cell.GetGlobalRect().End.X - edge) > 0.5f) return false;
            }
        }
        return true;
    }
}
