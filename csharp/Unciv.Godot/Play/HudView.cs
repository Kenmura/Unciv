using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Queries.Empire;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C011 / C067: the content of the heads-up display: the top bar (stat chips, updated in place), the turn banner (a toast) and the next-action dock. The widgets are
/// built here and <see cref="WorldHud"/> places them (it owns the layout; this class draws no absolute position). The next-action button's text and meaning come from
/// the engine's pending-decision list (<see cref="HudModel.NextAction"/>); while only non-blocking decisions are pending a second "Next turn" button next to it ends
/// the turn anyway. The view shows what it is given and raises <see cref="ActionRequested"/> / <see cref="EndTurnRequested"/>; it computes nothing.
/// </summary>
public partial class HudView : Control
{
    public const int BarHeight = 40;
    public const int CompactBarHeight = 36;
    private const int BannerFrames = 150; // counted in frames, not seconds, so a scenario's captures do not depend on how fast the machine renders

    private readonly PanelContainer bar = new() { Name = "TopBar", MouseFilter = MouseFilterEnum.Stop, ThemeTypeVariation = UiVariants.PanelHeader };
    private readonly HBoxContainer row = new() { Name = "Row", MouseFilter = MouseFilterEnum.Ignore };
    private readonly HBoxContainer leading = new() { Name = "Leading", MouseFilter = MouseFilterEnum.Ignore };
    private readonly PanelContainer bannerPanel = new() { Name = "BannerToast", Visible = false, MouseFilter = MouseFilterEnum.Ignore, ThemeTypeVariation = UiVariants.PanelCard };
    private readonly Label banner = new() { Name = "Banner", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, ThemeTypeVariation = UiVariants.LabelDisplay };
    private readonly VBoxContainer dock = new() { Name = "NextActionDock", MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
    private readonly HBoxContainer dockRow = new() { Name = "NextActionRow", MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
    private readonly Button actionButton = new() { Name = "NextAction", ThemeTypeVariation = UiVariants.ButtonPrimary };
    private readonly Dictionary<string, Control> entries = [];
    private Button autoPlayButton = null!; // JUSTIFY: C067 assigned by Init before the HUD is used
    private IconButton endTurnButton = null!; // JUSTIFY: C067 assigned by Init before the HUD is used
    private UiTheme theme = null!; // JUSTIFY: C011 assigned by Init before the HUD is updated
    private IReadOnlyList<HudStat> stats = [];
    private int bannerLeft;
    private bool endTurnWanted;
    private bool autoPlayWanted;

    public HudView()
    {
        Name = "HudContent";
        MouseFilter = MouseFilterEnum.Ignore;
        bar.AddChild(row);
        row.AddChild(leading);
        bannerPanel.AddChild(banner);
        actionButton.Pressed += () => ActionRequested?.Invoke(Action);
        dockRow.AddChild(actionButton);
        dock.AddChild(dockRow);
    }

    /// <summary>C047: whether a run is going; the next-action button stays disabled meanwhile (Kotlin disables the next turn button during AutoPlay).</summary>
    public bool AutoPlayRunning { get; private set; }

    public Button AutoPlayButton => autoPlayButton;

    /// <summary>The top bar panel (placed along the top edge by <see cref="WorldHud"/>).</summary>
    public Control Bar => bar;

    /// <summary>The slot at the left end of the top bar (the menu button).</summary>
    public Container Leading => leading;

    /// <summary>The turn banner toast.</summary>
    public Control Toast => bannerPanel;

    /// <summary>The next-action dock: AutoPlay above, End turn and the primary action below it.</summary>
    public Control Dock => dock;

    public Control ActionButton => actionButton;

    public Control EndTurnButton => endTurnButton;

    public event Action? AutoPlayRequested;

    /// <summary>C047: shows or hides the AutoPlay button (the "Show AutoPlay button" option).</summary>
    public void ShowAutoPlayButton(bool shown)
    {
        autoPlayWanted = shown;
        autoPlayButton.Visible = shown;
    }

    /// <summary>C047: words the AutoPlay button for the run (null = idle) and locks the next-action button while it runs.</summary>
    public void SetAutoPlay(AutoPlayRun? run)
    {
        AutoPlayRunning = run != null;
        autoPlayButton.Text = AutoPlayModel.ButtonText(run).TrText();
        actionButton.Disabled = AutoPlayRunning || !Action.Enabled;
        if (AutoPlayRunning) SetEndTurnShown(false);
    }

    /// <summary>What the next-action button currently means.</summary>
    public HudAction Action { get; private set; } = new(HudActionKind.EndTurn, HudModel.EndTurnLabel, null, true);

    public IReadOnlyList<HudStat> Stats => stats;

    public string ActionText => actionButton.Text;

    public bool ActionDisabled => actionButton.Disabled;

    public bool EndTurnShown => endTurnButton.Visible;

    public string BannerText => banner.Text;

    public bool BannerVisible => bannerPanel.Visible;

    /// <summary>Keeps the banner up until <see cref="HideBanner"/> (for scenarios, which must not depend on wall time).</summary>
    public bool HoldBanner { get; set; }

    public Rect2 BarRect => bar.GetGlobalRect();

    /// <summary>The text of one top bar entry by its <see cref="HudStat.Id"/>, null when the bar has no such entry.</summary>
    public string? StatText(string id) => stats.FirstOrDefault(it => it.Id == id)?.Text;

    /// <summary>The top bar's controls by stat id (the harness checks them for overlap and clipping).</summary>
    public IReadOnlyDictionary<string, Control> Entries => entries;

    public event Action<HudAction>? ActionRequested;

    public event Action? EndTurnRequested;

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
        foreach (var node in new Control[] { bar, bannerPanel, dock }) node.Theme = uiTheme.Theme;
        if (endTurnButton != null) dockRow.RemoveChild(endTurnButton);
        endTurnButton?.QueueFree();
        endTurnButton = new IconButton(uiTheme, AssetKey.Of(RulesetAssetKeys.UiIcon, "ForwardArrow"), HudModel.EndTurnLabel, variation: UiVariants.ButtonGhost) { Name = "EndTurn", Visible = false };
        endTurnButton.Pressed += () => EndTurnRequested?.Invoke();
        dockRow.AddChild(endTurnButton);
        dockRow.MoveChild(endTurnButton, 0);
        if (autoPlayButton != null) dock.RemoveChild(autoPlayButton);
        autoPlayButton?.QueueFree();
        autoPlayButton = new Button { Name = "AutoPlayButton", Text = AutoPlayModel.ButtonLabel.TrText(), Visible = false, ThemeTypeVariation = UiVariants.ButtonGhost };
        autoPlayButton.Pressed += () => AutoPlayRequested?.Invoke();
        dock.AddChild(autoPlayButton);
        dock.MoveChild(autoPlayButton, 0);
        bool compact = uiTheme.Breakpoint == UiBreakpoint.Compact;
        banner.ThemeTypeVariation = compact ? UiVariants.LabelTitle : UiVariants.LabelDisplay;
        var barStyle = (StyleBox)uiTheme.Theme.GetStylebox("panel", UiVariants.PanelHeader).Duplicate();
        barStyle.ContentMarginTop = barStyle.ContentMarginBottom = uiTheme.Px(UiTokens.S1);
        bar.AddThemeStyleboxOverride("panel", barStyle);
        bar.CustomMinimumSize = new Vector2(0f, uiTheme.Px(compact ? CompactBarHeight : BarHeight));
        actionButton.CustomMinimumSize = new Vector2(uiTheme.Px(compact ? 150 : 200), uiTheme.Px(compact ? 40 : 48));
        endTurnButton.CustomMinimumSize = new Vector2(uiTheme.Px(40), uiTheme.Px(compact ? 40 : 48));
        dock.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S1));
        dockRow.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        row.AddThemeConstantOverride("separation", uiTheme.Px(compact ? UiTokens.S2 : UiTokens.S4));
        endTurnButton.Visible = endTurnWanted;
        autoPlayButton.Visible = autoPlayWanted;
        Rebuild(force: true);
    }

    /// <summary>Height of the top bar in pixels (what <see cref="WorldHud"/> reserves for it).</summary>
    public float TopBarHeight => theme?.Px(theme.Breakpoint == UiBreakpoint.Compact ? CompactBarHeight : BarHeight) ?? BarHeight;

    public void Update(EmpireTopBarResult topBar, HudAction action, bool canEndTurn)
    {
        stats = HudModel.TopBar(topBar);
        Rebuild(force: false);
        Action = action;
        actionButton.Text = action.Label.TrText();
        actionButton.Disabled = !action.Enabled || AutoPlayRunning;
        SetEndTurnShown(action.Kind != HudActionKind.EndTurn && canEndTurn && action.Enabled && !AutoPlayRunning);
    }

    private void SetEndTurnShown(bool shown)
    {
        endTurnWanted = shown;
        endTurnButton.Visible = shown;
    }

    public void ShowBanner(string text)
    {
        banner.Text = text.TrText();
        bannerPanel.Visible = true;
        bannerLeft = BannerFrames;
    }

    public void HideBanner()
    {
        HoldBanner = false;
        bannerPanel.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!bannerPanel.Visible || HoldBanner) return;
        bannerLeft--;
        if (bannerLeft <= 0) bannerPanel.Visible = false;
    }

    /// <summary>Updates the existing chips in place; the row is rebuilt only when the set of entries changed (a resource appeared or went) or <paramref name="force"/>.</summary>
    private void Rebuild(bool force)
    {
        if (!force && entries.Keys.SequenceEqual(stats.Select(it => it.Id)))
        {
            foreach (var stat in stats) Apply(entries[stat.Id], stat);
            return;
        }
        foreach (var child in row.GetChildren().Where(it => it != leading).ToList())
        {
            row.RemoveChild(child); // at once: a queued free would leave the old entries in the row (and its width) until the frame ends
            child.QueueFree();
        }
        entries.Clear();
        foreach (var stat in stats)
        {
            if (stat.Id == "Turn") row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
            Control item = stat.Id == "Turn" ? new Label { ThemeTypeVariation = UiVariants.LabelNumber, VerticalAlignment = VerticalAlignment.Center }
                : stat.Icon is { Category: RulesetAssetKeys.StatIcon } icon ? new StatChip(theme, icon.Name, stat.Text)
                : stat.Icon is { } other ? new IconLabel(theme, other, stat.Text)
                : new Label { VerticalAlignment = VerticalAlignment.Center };
            item.Name = stat.Id.Replace(':', '_');
            item.MouseFilter = MouseFilterEnum.Stop; // a tooltip needs the pointer
            row.AddChild(item);
            entries[stat.Id] = item;
            Apply(item, stat);
        }
    }

    private static void Apply(Control item, HudStat stat)
    {
        switch (item)
        {
            case StatChip chip: chip.Value = stat.Text; break;
            case IconLabel label: label.Text = stat.Text; break;
            case Label label: label.Text = stat.Text; break;
        }
        item.TooltipText = stat.Tooltip.TrText();
    }
}
