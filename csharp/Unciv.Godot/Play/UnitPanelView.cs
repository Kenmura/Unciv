using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Settings;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C012/C068: the selected unit's panel at the bottom left. A header (unit icon, name, standing order, the promote chip), stat chips (strength, moves, experience), a health
/// bar, the promotions, and the action menu as a grid of icon buttons (two rows; the primary one-off actions lead, the rest follow, and what does not fit is behind a "More"
/// popover), one button per <see cref="MenuEntry"/>. A disabled entry stays visible with its reason as tooltip, so the menu is discoverable. It shows what it is given and
/// raises <see cref="EntryChosen"/>; a chosen entry that needs confirming is asked here inline (<see cref="AskConfirmation"/>, danger-styled) before it is raised again as <see cref="Confirmed"/>.
/// <see cref="Entries"/> and <see cref="ButtonFor"/> list every entry, whether or not its button is in the grid.
/// </summary>
public partial class UnitPanelView : PanelContainer
{
    private const int Width = 244;
    private const int Columns = 5;
    private const int Rows = 2;
    private const int CellSize = 40;
    private const float Margin = 12f;

    private readonly VBoxContainer column = new() { Name = "Column" };
    private readonly HBoxContainer header = new() { Name = "Header" };
    private readonly VBoxContainer headerText = new() { Name = "HeaderText", SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Label title = new() { Name = "Title", ThemeTypeVariation = UiVariants.LabelHeading, ClipText = true };
    private readonly Label order = new() { Name = "Order", ThemeTypeVariation = UiVariants.LabelSmall };
    private readonly HBoxContainer chips = new() { Name = "Chips" };
    private readonly Button groupToggle = new() { Name = "GroupToggle", Text = "Group select".Tr(), ToggleMode = true, Visible = false, TooltipText = "While on, tapping a tile with your units adds them to the selection (Shift+click on a desktop)".Tr() };
    private readonly Control healthSlot = new() { Name = "HealthSlot" };
    private readonly VBoxContainer healthBox = new() { Name = "Health" };
    private readonly Label promotions = new() { Name = "Promotions", ThemeTypeVariation = UiVariants.LabelSmall, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly GridContainer grid = new() { Name = "Menu", Columns = Columns };
    private readonly PanelContainer popover = new() { Name = "MorePopover", Visible = false, TopLevel = true, ThemeTypeVariation = UiVariants.PanelCard };
    private readonly VBoxContainer popoverBox = new() { Name = "MoreBox" };
    private readonly VBoxContainer overflowBox = new() { Name = "Overflow" };
    private readonly VBoxContainer promoteBox = new() { Name = "PromotionChoices" };
    private readonly Label promoteHeading = new() { Text = "Promotions".Tr(), ThemeTypeVariation = UiVariants.LabelOverline };
    private readonly VBoxContainer confirmBox = new() { Name = "Confirm", Visible = false };
    private readonly Label question = new() { Name = "Question", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button yes = new() { Name = "ConfirmYes", ThemeTypeVariation = UiVariants.ButtonDanger };
    private readonly Button no = new() { Name = "ConfirmNo", Text = "Cancel".Tr(), ThemeTypeVariation = UiVariants.ButtonGhost };
    private readonly List<(MenuEntry Entry, Button Button)> buttons = [];
    private IconBadge? icon;
    private Button? more;
    private Button? promoteChip;
    private UiTheme? theme;
    private MenuEntry? asking;

    public UnitPanelView()
    {
        Name = "UnitPanel";
        Visible = false;
        ThemeTypeVariation = UiVariants.PanelCard;
        MouseFilter = MouseFilterEnum.Stop; // clicks on the panel must not reach the map behind it
        column.AddThemeConstantOverride("separation", UiTokens.S2);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerText.AddChild(title);
        headerText.AddChild(order);
        headerText.AddThemeConstantOverride("separation", 0);
        header.AddThemeConstantOverride("separation", UiTokens.S2);
        header.AddChild(headerText);
        chips.AddThemeConstantOverride("separation", UiTokens.S3);
        healthSlot.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        healthBox.AddChild(healthSlot);
        grid.AddThemeConstantOverride("h_separation", UiTokens.S1);
        grid.AddThemeConstantOverride("v_separation", UiTokens.S1);
        confirmBox.AddChild(question);
        var answers = new HBoxContainer();
        answers.AddThemeConstantOverride("separation", UiTokens.S2);
        answers.AddChild(yes);
        answers.AddChild(no);
        confirmBox.AddChild(answers);
        column.AddChild(header);
        column.AddChild(chips);
        column.AddChild(groupToggle);
        column.AddChild(healthBox);
        column.AddChild(promotions);
        column.AddChild(grid);
        column.AddChild(confirmBox);
        AddChild(column);
        popoverBox.AddChild(overflowBox);
        popoverBox.AddChild(promoteHeading);
        popoverBox.AddChild(promoteBox);
        popover.AddChild(popoverBox);
        AddChild(popover);
        yes.Pressed += () =>
        {
            if (asking is { } entry) Confirmed?.Invoke(entry);
            EndConfirmation();
        };
        no.Pressed += EndConfirmation;
    }

    /// <summary>The entries of the menu as shown, in order.</summary>
    public IReadOnlyList<MenuEntry> Entries => buttons.Select(it => it.Entry).ToList();

    public string TitleText => title.Text;

    public string StatsText { get; private set; } = "";

    public bool IsAsking => confirmBox.Visible;

    public string QuestionText => question.Text;

    /// <summary>The entries whose buttons are in the grid (the others are in the popover), in grid order.</summary>
    public IReadOnlyList<MenuEntry> GridEntries => [.. grid.GetChildren().OfType<Button>().Select(it => buttons.FirstOrDefault(b => b.Button == it).Entry).Where(it => it != null)];

    /// <summary>The "More" button, or null when every action fits the grid.</summary>
    public Button? MoreButton => more;

    /// <summary>The stat chips row (the harness taps a chip to show its tip).</summary>
    public Control ChipsRow => chips;

    public bool PopoverOpen => popover.Visible;

    /// <summary>The grid's rectangle (the harness checks that it has at most two rows).</summary>
    public Rect2 GridRect => grid.GetGlobalRect();

    public int GridRows => (grid.GetChildCount() + Columns - 1) / Columns;

    public Rect2 PopoverRect => popover.GetGlobalRect();

    /// <summary>C100: the touch stand-in for Shift+click: while on, a tap on a tile with own units adds them to (or takes them off) the selected group. Only offered on a touch build, for an own unit.</summary>
    public bool GroupMode => groupToggle.Visible && groupToggle.ButtonPressed;

    /// <summary>The group select toggle (null never; it is hidden unless a touch build shows an own unit).</summary>
    public Button GroupToggle => groupToggle;

    public event Action<MenuEntry>? EntryChosen;

    public event Action<MenuEntry>? Confirmed;

    public Button? ButtonFor(string label) => buttons.FirstOrDefault(it => it.Entry.Label == label).Button;

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        legible.Clear();
        Theme = uiTheme.Theme;
        uiTheme.ScaleChanged += () => LayoutIn(GetViewportRect().Size);
        MinimumSizeChanged += KeepAtBottom;
    }

    private Vector2 lastScreen = Vector2.Zero;

    /// <summary>C079: the content can grow after <see cref="LayoutIn"/> measured it (a scale or breakpoint change restyles the children afterwards): keep the panel's bottom edge on the margin.</summary>
    private void KeepAtBottom()
    {
        if (!Visible || lastScreen == Vector2.Zero) return;
        float wantedTop = lastScreen.Y - Margin - GetCombinedMinimumSize().Y;
        if (Math.Abs(Position.Y - wantedTop) > 0.5f) CallDeferred(nameof(Relayout));
    }

    private void Relayout() => LayoutIn(lastScreen);

    /// <summary>Hosts the world screen's Promote button as the panel's chip (it keeps its owner, name and handler; the panel only places it).</summary>
    public void SetPromoteChip(Button chip)
    {
        promoteChip = chip;
        chip.ThemeTypeVariation = UiVariants.ButtonChip;
        chip.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        header.AddChild(chip);
    }

    /// <summary>Shows <paramref name="info"/> and the menu, replacing what was there.</summary>
    public void Show(UnitPanelInfo info, IReadOnlyList<MenuEntry> menu)
    {
        EndConfirmation();
        ClosePopover();
        title.Text = info.Title.TrText();
        order.Text = info.Order.Length > 0 ? info.Order.TrText() : "";
        order.Visible = info.Order.Length > 0;
        var lines = new List<string> { info.Strength, info.Moves, info.Health, info.Xp };
        if (info.Order.Length > 0) lines.Add("Order: " + info.Order);
        if (info.Promotions.Count > 0) lines.Add("Promotions: " + string.Join(", ", info.Promotions));
        StatsText = string.Join("\n", lines.Where(it => it.Length > 0).Select(it => it.TrText()));
        promotions.Text = info.Promotions.Count > 0 ? string.Join(", ", info.Promotions.Select(it => it.TrText())) : "";
        promotions.Visible = info.Promotions.Count > 0;
        BuildHeaderIcon(info);
        BuildChips(info);
        groupToggle.Visible = DisplayProfile.Current.Touch && info.IsOwn;
        if (!groupToggle.Visible) groupToggle.SetPressedNoSignal(false);
        BuildHealth(info);
        BuildMenu(menu);
        Visible = true;
        LayoutIn(GetViewportRect().Size);
    }

    public void Clear()
    {
        groupToggle.SetPressedNoSignal(false);
        EndConfirmation();
        ClosePopover();
        Visible = false;
    }

    /// <summary>Replaces the menu by a question with a danger-styled confirm button (named <paramref name="confirmText"/>) and Cancel.</summary>
    public void AskConfirmation(MenuEntry entry, string questionText, string confirmText)
    {
        ClosePopover();
        asking = entry;
        question.Text = questionText.TrText();
        yes.Text = (confirmText.Length > 0 ? confirmText : "Yes").TrText();
        grid.Visible = false;
        confirmBox.Visible = true;
        if (promoteChip != null) promoteChip.MouseFilter = MouseFilterEnum.Ignore; // no promotion picker over a pending question
        LayoutIn(GetViewportRect().Size);
    }

    public override void _Input(InputEvent @event)
    {
        // a press anywhere outside the popover and the buttons that open it closes it; the press itself still goes on to its target
        if (!popover.Visible || @event is not InputEventMouseButton { Pressed: true } click) return;
        var point = click.GlobalPosition;
        if (popover.GetGlobalRect().HasPoint(point) || (more?.GetGlobalRect().HasPoint(point) ?? false)) return;
        ClosePopover();
    }

    private void BuildHeaderIcon(UnitPanelInfo info)
    {
        if (icon != null)
        {
            header.RemoveChild(icon);
            icon.QueueFree();
            icon = null;
        }
        if (theme == null) return;
        icon = new IconBadge(theme, AssetKey.Of(RulesetAssetKeys.UnitIcon, info.Title), ProceduralAssets.ToColor(UiTokens.Surface3), ProceduralAssets.ToColor(UiTokens.TextPrimary), 36);
        header.AddChild(icon);
        header.MoveChild(icon, 0);
    }

    private void BuildChips(UnitPanelInfo info)
    {
        Clear(chips);
        if (theme == null) return;
        if (info.Combat > 0 || info.Ranged > 0)
        {
            string text = info.Ranged > 0 ? $"{Int(info.Ranged)} r{Int(info.Range)}" : Int(info.Combat);
            AddChip("Shield", text, info.Strength);
        }
        if (info.IsOwn)
        {
            string moves = info.MovesMax > 0 ? $"{Float(info.MovesLeft)}/{Int(info.MovesMax)}" : Float(info.MovesLeft);
            AddChip(AssetKey.Of(RulesetAssetKeys.ActionIcon, "MoveTo"), moves, info.Moves);
            if (info.XpValue >= 0) AddChip("Star", Int(info.XpValue), info.Xp);
        }
    }

    private void AddChip(string uiIcon, string text, string tooltip) => AddChip(AssetKey.Of(RulesetAssetKeys.UiIcon, uiIcon), text, tooltip);

    private void AddChip(AssetKey key, string text, string tooltip)
    {
        var chip = new IconLabel(theme!, key, text) { TooltipText = tooltip.TrText() };
        chips.AddChild(chip);
    }

    private void BuildHealth(UnitPanelInfo info)
    {
        foreach (var child in healthSlot.GetChildren())
        {
            healthSlot.RemoveChild(child);
            child.QueueFree();
        }
        if (theme == null) return;
        int percent = (int)(info.Fraction * 100 + 0.5);
        string caption = info.IsOwn ? Int(percent) : $"{Int(percent)}%";
        var bar = new StatBar(theme, "Food", info.Fraction, 1.0, labelled: false, caption: caption) { TooltipText = info.Health.TrText() };
        bar.Fill();
        healthSlot.AddChild(bar);
        healthSlot.CustomMinimumSize = bar.GetCombinedMinimumSize();
    }

    private void BuildMenu(IReadOnlyList<MenuEntry> menu)
    {
        Clear(grid);
        Clear(overflowBox);
        Clear(promoteBox);
        if (more != null)
        {
            more.QueueFree();
            more = null;
        }
        buttons.Clear();
        // grid order: the primary actions lead, the rest keep the menu's order, Disband is last; promotions never take a grid cell (the header promote chip is their way in; their buttons sit in the popover and stay reachable through ButtonFor)
        var indices = Enumerable.Range(0, menu.Count).ToList();
        var shown = indices.Where(i => menu[i].Kind != MenuEntryKind.Promote && menu[i].Kind != MenuEntryKind.Disband).OrderBy(i => menu[i].Primary ? 0 : 1).ToList();
        shown.AddRange(indices.Where(i => menu[i].Kind == MenuEntryKind.Disband));
        int capacity = Columns * Rows;
        bool overflows = shown.Count > capacity;
        int inGrid = overflows ? capacity - 1 : shown.Count;
        foreach (var entry in menu) buttons.Add((entry, MakeButton(entry, inGrid: false)));
        // by position, not by entry: two entries may compare equal as records
        for (int i = 0; i < shown.Count; i++)
        {
            var button = buttons[shown[i]].Button;
            if (i < inGrid)
            {
                Restyle(button, buttons[shown[i]].Entry, inGrid: true);
                grid.AddChild(button);
            }
            else overflowBox.AddChild(button);
        }
        for (int i = 0; i < menu.Count; i++)
            if (menu[i].Kind == MenuEntryKind.Promote) promoteBox.AddChild(buttons[i].Button);
        promoteHeading.Visible = promoteBox.GetChildCount() > 0;
        promoteBox.Visible = promoteBox.GetChildCount() > 0;
        overflowBox.Visible = overflowBox.GetChildCount() > 0;
        if (!overflows) return;
        more = theme == null ? new Button { Name = "MoreButton", Text = "More".Tr() } : new IconButton(theme, AssetKey.Of(RulesetAssetKeys.ActionIcon, "ShowMore"), "More") { Name = "MoreButton" };
        more.CustomMinimumSize = new Vector2(Cell, Cell);
        more.Pressed += TogglePopover;
        grid.AddChild(more);
    }

    private readonly Dictionary<Texture2D, Texture2D> legible = [];

    /// <summary>
    /// The action, improvement and promotion icons are dark silhouettes, invisible on the dark panel: a mostly dark icon is returned as a light silhouette of the same shape
    /// (colour glyphs and the registry's generated badges are kept). Cached per source texture.
    /// </summary>
    private Texture2D? Legible(Texture2D? source)
    {
        if (source == null) return null;
        if (legible.TryGetValue(source, out var known)) return known;
        var image = source.GetImage();
        Texture2D result = source;
        if (image != null && !image.IsEmpty())
        {
            image.Convert(Image.Format.Rgba8);
            double dark = 0, opaque = 0;
            for (int y = 0; y < image.GetHeight(); y++)
                for (int x = 0; x < image.GetWidth(); x++)
                {
                    var c = image.GetPixel(x, y);
                    if (c.A < 0.5f) continue;
                    opaque++;
                    if (c.R + c.G + c.B < 0.9f) dark++;
                }
            if (opaque > 0 && dark / opaque > 0.8)
            {
                var light = ProceduralAssets.ToColor(UiTokens.TextPrimary);
                for (int y = 0; y < image.GetHeight(); y++)
                    for (int x = 0; x < image.GetWidth(); x++)
                        image.SetPixel(x, y, light with { A = image.GetPixel(x, y).A });
                result = ImageTexture.CreateFromImage(image);
            }
        }
        legible[source] = result;
        return result;
    }

    private Func<HotkeyAction, string>? keyOf = action => Hotkeys.Defaults[action];

    /// <summary>C078: the bound keys the grid tooltips and the popover rows show; takes effect when the menu is next built (a selection or a refresh).</summary>
    public void SetKeys(Func<HotkeyAction, string> keys) => keyOf = keys;

    /// <summary>The key shown for an entry, or null when its action has none.</summary>
    public string? KeyFor(MenuEntry entry) => keyOf != null && Hotkeys.ForEntry(entry) is { } action ? keyOf(action) : null;

    private float Cell => theme?.Px(CellSize) ?? CellSize;

    private Button MakeButton(MenuEntry entry, bool inGrid)
    {
        Button button;
        if (theme != null)
        {
            var key = entry.IconKey ?? AssetKey.Of(RulesetAssetKeys.ActionIcon, "Star");
            button = new IconButton(theme, key, entry.Label, entry.Label.TrText());
            button.Icon = Legible(button.Icon);
        }
        else button = new Button { Text = entry.Label.TrText() };
        button.Disabled = !entry.Enabled;
        button.Pressed += () =>
        {
            ClosePopover();
            EntryChosen?.Invoke(entry);
        };
        Restyle(button, entry, inGrid);
        return button;
    }

    // a grid cell shows the icon alone (the label is the tooltip); a popover row shows icon and text
    private void Restyle(Button button, MenuEntry entry, bool inGrid)
    {
        button.Text = inGrid ? "" : entry.Label.TrText();
        button.Alignment = HorizontalAlignment.Left;
        button.ThemeTypeVariation = inGrid
            ? entry.Kind == MenuEntryKind.Disband ? UiVariants.ButtonDanger : entry.Primary ? UiVariants.ButtonPrimary : UiVariants.ButtonIcon
            : UiVariants.ButtonRow;
        button.CustomMinimumSize = inGrid ? new Vector2(Cell, Cell) : Vector2.Zero;
        button.ExpandIcon = false;
        string tip = UiText.TooltipFor(entry.Label.TrText(), KeyFor(entry));
        button.TooltipText = entry.Reason is { Length: > 0 } reason ? $"{tip}: {reason.TrText()}" : tip;
        if (button.GetNodeOrNull("KeyHint") is { } old)
        {
            button.RemoveChild(old);
            old.QueueFree();
        }
        if (!inGrid && theme != null && KeyFor(entry) is { } key)
        {
            // a popover row: the key chip sits at its right end; the row is made wide enough for both
            var hint = new KeyHint(key) { Name = "KeyHint" };
            hint.SetAnchorsPreset(LayoutPreset.CenterRight);
            hint.GrowHorizontal = GrowDirection.Begin;
            hint.GrowVertical = GrowDirection.Both;
            hint.OffsetRight = -theme.Px(UiTokens.S2);
            button.AddChild(hint);
            var font = button.GetThemeFont("font");
            float text = font.GetStringSize(entry.Label.TrText(), HorizontalAlignment.Left, -1, button.GetThemeFontSize("font_size")).X;
            button.CustomMinimumSize = new Vector2(text + hint.GetCombinedMinimumSize().X + theme.Px(UiTokens.S6 * 2) + theme.Px(UiTheme.IconSize), 0f);
        }
        if (inGrid) button.Alignment = HorizontalAlignment.Center;
    }

    private void TogglePopover()
    {
        if (popover.Visible)
        {
            ClosePopover();
            return;
        }
        if (more == null) return;
        popover.Visible = true;
        popover.Size = Vector2.Zero;
        var size = popover.GetCombinedMinimumSize();
        var anchor = more.GetGlobalRect();
        var screen = GetViewportRect().Size;
        float x = Math.Clamp(anchor.Position.X, 0f, Math.Max(0f, screen.X - size.X));
        float y = Math.Max(0f, anchor.Position.Y - size.Y - Margin / 3f);
        popover.GlobalPosition = new Vector2(x, y);
        popover.Size = size;
    }

    private void ClosePopover() => popover.Visible = false;

    private void EndConfirmation()
    {
        asking = null;
        confirmBox.Visible = false;
        grid.Visible = true;
        if (promoteChip != null) promoteChip.MouseFilter = MouseFilterEnum.Stop;
    }

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child); // at once: the tests and the layout read the box in the same frame
            child.QueueFree();
        }
    }

    /// <summary>Places the panel at the bottom left of a screen of <paramref name="screen"/> pixels.</summary>
    public void LayoutIn(Vector2 screen)
    {
        lastScreen = screen;
        float width = theme?.Px(Width) ?? Width;
        question.CustomMinimumSize = new Vector2(width - (theme?.Px(UiTokens.S6) ?? UiTokens.S6), 0f);
        promotions.CustomMinimumSize = new Vector2(width - (theme?.Px(UiTokens.S6) ?? UiTokens.S6), 0f); // C055: an autowrapping label measured at width 0 is as tall as it has characters
        CustomMinimumSize = new Vector2(width, 0f);
        Size = Vector2.Zero; // shrink to the content
        Position = new Vector2(Margin, screen.Y - Margin - GetCombinedMinimumSize().Y);
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Float(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
