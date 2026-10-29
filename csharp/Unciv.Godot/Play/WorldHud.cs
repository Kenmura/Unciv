using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>Where a button of the world HUD's nav strip leads (C067).</summary>
public enum NavTarget
{
    Diplomacy,
    Technology,
    Policies,
    Trade,
    Espionage,
    Civilopedia,
    Overview,
    Menu,
    Players,
}

/// <summary>
/// C067: the layout of the world screen's heads-up display (design/ui-redesign.md 3.1). It owns the top bar, the nav strip with the research pill, the notification column,
/// the minimap dock, the next-action dock and the turn toast, and places them with containers and anchors only (no absolute position): the top bar along the top, the
/// nav row under it (research and policy pills left, icon buttons right), the notification column on the right, the rival stats ribbon (C087) at the left under the nav row, and the minimap above the next-action dock at the bottom right.
/// The bottom left is left to the unit panel. The map stays uncovered between the bar and the docks. The content comes from <see cref="HudView"/>,
/// <see cref="NotificationListView"/> and <see cref="MinimapView"/> (<see cref="Mount"/>); pressing a nav button raises <see cref="Requested"/>.
/// The structure is rebuilt when the UI theme's breakpoint changes (<see cref="UiTheme.LayoutChanged"/>), not every frame.
/// </summary>
public partial class WorldHud : Control
{
    public const int NavHeight = 40;
    private const int RegularMinimapWidth = 200;
    private const int RegularMinimapHeight = 140;
    private const int CompactMinimapWidth = 140;
    private const int CompactMinimapHeight = 100;

    private readonly VBoxContainer column = new() { Name = "HudColumn", MouseFilter = MouseFilterEnum.Ignore };
    private readonly MarginContainer body = new() { Name = "HudBody", MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly VBoxContainer bodyColumn = new() { Name = "HudBodyColumn", MouseFilter = MouseFilterEnum.Ignore };
    private readonly HBoxContainer navRow = new() { Name = "NavRow", MouseFilter = MouseFilterEnum.Ignore };
    private readonly HBoxContainer navLeft = new() { Name = "ResearchPills", MouseFilter = MouseFilterEnum.Ignore };
    private readonly HBoxContainer navRight = new() { Name = "NavStrip", MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
    private readonly MarginContainer notificationLayer = new() { Name = "NotificationLayer", MouseFilter = MouseFilterEnum.Ignore };
    private readonly VBoxContainer notificationSlot = new() { Name = "NotificationColumn", MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkEnd, SizeFlagsVertical = SizeFlags.ShrinkBegin };
    private readonly HBoxContainer bottom = new() { Name = "HudBottom", MouseFilter = MouseFilterEnum.Ignore };
    private readonly VBoxContainer rightDock = new() { Name = "RightDock", MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
    private readonly PanelContainer minimapDock = new() { Name = "MinimapDock", MouseFilter = MouseFilterEnum.Ignore, ThemeTypeVariation = UiVariants.PanelCard, SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
    private readonly Control toastLayer = new() { Name = "ToastLayer", MouseFilter = MouseFilterEnum.Ignore };
    private HudView hud = null!; // JUSTIFY: C067 assigned by Mount before any use
    private NotificationListView notifications = null!; // JUSTIFY: C067 assigned by Mount before any use
    private MinimapView minimap = null!; // JUSTIFY: C067 assigned by Mount before any use
    private UiTheme theme = null!; // JUSTIFY: C067 assigned by Init before the layout is read
    private UiBreakpoint builtFor;
    private float builtScale;
    private bool playersShown;
    private bool technologyAsking;

    public WorldHud()
    {
        Name = "WorldHud";
        MouseFilter = MouseFilterEnum.Ignore;
        this.Fill();
        column.Fill();
        AddChild(column);
        toastLayer.Fill();
        AddChild(toastLayer);
        // the notification column is a layer of its own above the docks: its drop-down list floats over them and takes their clicks
        notificationLayer.Fill();
        notificationLayer.AddChild(notificationSlot);
        AddChild(notificationLayer);
        column.AddChild(body);
        body.AddChild(bodyColumn);
        navRow.AddChild(navLeft);
        navRow.AddChild(new Control { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        navRow.AddChild(navRight);
        bodyColumn.AddChild(navRow);
        // the middle of the map stays free: only the rival ribbon sits in its left edge, under the research pills
        var middle = new HBoxContainer { Name = "HudMiddle", MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ExpandFill };
        middle.AddChild(Rivals);
        middle.AddChild(new Control { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        bodyColumn.AddChild(middle);
        bottom.AddChild(new Control { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        bottom.AddChild(rightDock);
        bodyColumn.AddChild(bottom);
    }

    /// <summary>C087: the collapsible rival stats ribbon, under the research pills.</summary>
    public RivalRibbonView Rivals { get; } = new();

    public IconButton Diplomacy { get; private set; } = null!; // JUSTIFY: C067 built by Init

    public IconButton Technology { get; private set; } = null!; // JUSTIFY: C067 built by Init

    public IconButton Policies { get; private set; } = null!; // JUSTIFY: C067 built by Init

    public IconButton Trade { get; private set; } = null!; // JUSTIFY: C067 built by Init

    public IconButton Espionage { get; private set; } = null!; // JUSTIFY: C067 built by Init

    public IconButton Civilopedia { get; private set; } = null!; // JUSTIFY: C067 built by Init

    public IconButton Overview { get; private set; } = null!; // JUSTIFY: C067 built by Init

    public IconButton Menu { get; private set; } = null!; // JUSTIFY: C067 built by Init

    public IconButton Players { get; private set; } = null!; // JUSTIFY: C067 built by Init

    /// <summary>A nav button was pressed.</summary>
    public event Action<NavTarget>? Requested;

    /// <summary>The minimap's slot size in pixels at the current breakpoint (never below 140x100).</summary>
    public Vector2 MinimapSize => theme == null ? default : new Vector2(theme.Px(Compact ? CompactMinimapWidth : RegularMinimapWidth), theme.Px(Compact ? CompactMinimapHeight : RegularMinimapHeight));

    private bool Compact => theme.Breakpoint == UiBreakpoint.Compact;

    /// <summary>C101: the nav row's height in pixels; on a touch build never below the 48 dp target, so what sits under the row (notification column, toast) starts below the buttons.</summary>
    private int NavPx => theme.MinTargetPx > 0 ? Math.Max(Math.Max(theme.Px(NavHeight), theme.MinTargetPx), (int)MathF.Ceiling(NavButtonsHeight())) : theme.Px(NavHeight);

    /// <summary>The tallest nav button's minimum height (a button's padding can make it taller than the 48 dp floor), so the row and what hangs under it are sized from the buttons themselves.</summary>
    private float NavButtonsHeight() => navLeft.GetChildren().Concat(navRight.GetChildren()).OfType<Control>().Where(it => it.Visible).Select(it => it.GetCombinedMinimumSize().Y).DefaultIfEmpty(0f).Max();

    private float Gap => theme.Px(UiTokens.S2);

    /// <summary>Global y of the bottom edge of the top bar (what tooltips stay below).</summary>
    public float BarBottom => hud.BarRect.End.Y;

    /// <summary>Global y just under the nav row, where the screen's other top-anchored panels (tutorial card, turn bar) start.</summary>
    public float NavBottom => theme == null ? 0f : hud.TopBarHeight + Gap + NavPx;

    /// <summary>Hands the layout the widgets it places. Call once, before <see cref="Init"/>.</summary>
    public void Mount(HudView content, NotificationListView notificationList, MinimapView minimapView)
    {
        hud = content;
        notifications = notificationList;
        minimap = minimapView;
        AddChild(hud); // not a visual child: it owns the widgets and needs the tree for its frame counter
        column.AddChild(hud.Bar);
        column.MoveChild(hud.Bar, 0);
        toastLayer.AddChild(hud.Toast);
        notificationSlot.AddChild(notifications);
        minimapDock.AddChild(minimap);
        rightDock.AddChild(minimapDock);
        rightDock.AddChild(hud.Dock);
    }

    /// <summary>(Re)builds everything that depends on the theme: sizes, the nav buttons, the structure for the breakpoint. Call again to rebuild after the theme changed.</summary>
    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
        builtFor = uiTheme.Breakpoint;
        builtScale = uiTheme.Scale;
        hud.Init(uiTheme);
        notifications.Init(uiTheme);
        Rivals.Init(uiTheme);
        int gap = theme.Px(UiTokens.S2);
        foreach (string side in new[] { "left", "top", "right", "bottom" }) body.AddThemeConstantOverride("margin_" + side, side == "top" ? 0 : gap);
        bodyColumn.AddThemeConstantOverride("separation", gap);
        BuildNav(); // before the sizes below: they reckon with the buttons' own height (NavPx)
        notificationLayer.AddThemeConstantOverride("margin_top", (int)NavBottom + gap);
        notificationLayer.AddThemeConstantOverride("margin_right", gap);
        notificationLayer.AddThemeConstantOverride("margin_left", gap);
        navRow.CustomMinimumSize = new Vector2(0f, NavPx);
        navLeft.AddThemeConstantOverride("separation", gap);
        navRight.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        rightDock.AddThemeConstantOverride("separation", gap);
        notificationSlot.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        minimap.SetSlot(MinimapSize);
        toastLayer.Theme = uiTheme.Theme;
        hud.Toast.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        hud.Toast.GrowHorizontal = GrowDirection.Both;
        hud.Toast.OffsetTop = hud.TopBarHeight + 2 * gap + NavPx;
    }

    /// <summary>Rebuilds when the breakpoint or the UI scale changed since the last build (a window resize across 1000 logical pixels, or to a size with another automatic scale, C079: the pixel sizes the build reckoned with are stale then); any other resize needs nothing, the containers follow.</summary>
    public void OnLayoutChanged()
    {
        if (theme != null && (theme.Breakpoint != builtFor || theme.Scale != builtScale)) Init(theme);
    }

    /// <summary>Shows the Players button (multiplayer games with a roster).</summary>
    public void ShowPlayers(bool shown)
    {
        playersShown = shown;
        Players.Visible = shown;
    }

    /// <summary>
    /// The rectangle of every HUD element that is showing, by name: the harness checks that none overlaps another (at 640x480 and at 1280x720) and that all of them stay on screen.
    /// The turn toast and an open notification list are transient overlays and are not part of the set.
    /// </summary>
    public IReadOnlyList<(string Name, Rect2 Rect)> Rects()
    {
        var rects = new List<(string, Rect2)> { ("TopBar", hud.BarRect) };
        void Add(string name, Control? control)
        {
            if (control is { } c && c.IsVisibleInTree()) rects.Add((name, c.GetGlobalRect()));
        }
        foreach (var button in new[] { Technology, Policies, Diplomacy, Trade, Espionage, Civilopedia, Overview, Players }) Add(button.Label, button);
        Add("Notifications", notifications.ToggleButton);
        Add("ReviewAlerts", notifications.ReviewButton);
        Add("Rivals", Rivals);
        Add("Minimap", minimapDock);
        Add("AutoPlay", hud.AutoPlayButton);
        Add("NextTurn", hud.EndTurnButton);
        Add("NextAction", hud.ActionButton);
        return rects;
    }

    private void BuildNav()
    {
        foreach (var holder in new Container[] { navLeft, navRight, hud.Leading })
            foreach (var child in holder.GetChildren())
            {
                holder.RemoveChild(child);
                child.QueueFree();
            }
        bool compact = Compact;
        IconButton Make(NavTarget target, string icon, string label, string? text = null, string? hotkey = null, string variation = UiVariants.ButtonIcon)
        {
            var button = new IconButton(theme, AssetKey.Of(RulesetAssetKeys.UiIcon, icon), label, text, hotkey, variation) { Name = label + "Button" };
            button.Pressed += () => Requested?.Invoke(target);
            return button;
        }
        Menu = Make(NavTarget.Menu, "Options", "Menu", hotkey: "Esc");
        hud.Leading.AddChild(Menu);
        Technology = Make(NavTarget.Technology, "Increase", "Technology", "Technology", variation: UiVariants.ButtonSecondary);
        Policies = Make(NavTarget.Policies, "Policies", "Policies", compact ? null : "Policies", variation: compact ? UiVariants.ButtonIcon : UiVariants.ButtonSecondary);
        navLeft.AddChild(Technology);
        navLeft.AddChild(Policies);
        Diplomacy = Make(NavTarget.Diplomacy, "Nations", "Diplomacy");
        Trade = Make(NavTarget.Trade, "Resources", "Trade");
        Espionage = Make(NavTarget.Espionage, "Espionage", "Espionage");
        Civilopedia = Make(NavTarget.Civilopedia, "Wonders", "Civilopedia");
        Overview = Make(NavTarget.Overview, "Score", "Overview");
        Players = Make(NavTarget.Players, "Multiplayer", "Players");
        Players.Visible = playersShown;
        foreach (var button in new Control[] { Diplomacy, Trade, Espionage, Civilopedia, Overview, Players }) navRight.AddChild(button);
        foreach (var button in new[] { Technology, Policies, Diplomacy, Trade, Espionage, Civilopedia, Overview, Players, Menu }) button.Theme = theme.Theme;
        Technology.SetBadge(technologyAsking ? 1 : 0, accent: true); // a rebuild (breakpoint change) keeps the dot
        ApplyKeys();
    }

    private Func<HotkeyAction, string>? keyOf = action => Hotkeys.Defaults[action];

    /// <summary>C078: shows the bound keys in the nav buttons' tooltips ("Technology (F6)"); a rebind or a rebuilt nav calls it again.</summary>
    public void SetKeys(Func<HotkeyAction, string> keys)
    {
        keyOf = keys;
        if (Technology != null) ApplyKeys();
    }

    private void ApplyKeys()
    {
        if (keyOf == null) return;
        Technology.Hotkey = keyOf(HotkeyAction.OpenTech);
        Policies.Hotkey = keyOf(HotkeyAction.OpenPolicies);
        Diplomacy.Hotkey = keyOf(HotkeyAction.OpenDiplomacy);
        Civilopedia.Hotkey = keyOf(HotkeyAction.OpenPedia);
        Overview.Hotkey = keyOf(HotkeyAction.OpenOverview);
    }

    /// <summary>The Accent dot on the Technology pill while the engine asks for a technology.</summary>
    public void SetTechnologyAvailable(bool asking)
    {
        technologyAsking = asking;
        Technology?.SetBadge(asking ? 1 : 0, accent: true);
    }
}
