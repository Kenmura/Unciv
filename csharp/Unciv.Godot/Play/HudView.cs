using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Queries.Empire;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C011: the heads-up display of the game screen. A top bar (gold, science, culture, happiness, faith, golden age, resources, turn and year; the entries are
/// <see cref="HudModel.TopBar"/>), a banner shown when a turn starts, and the next-action button at the bottom right. The button's text and meaning come from
/// the engine's pending-decision list (<see cref="HudModel.NextAction"/>); while only non-blocking decisions are pending a second "Next turn" button
/// next to it ends the turn anyway. The view shows what it is given and raises <see cref="ActionRequested"/> / <see cref="EndTurnRequested"/>; it computes nothing.
/// </summary>
public partial class HudView : Control
{
    public const int BarHeight = 36;
    private const float Margin = 12f;
    private const int BannerFrames = 150; // counted in frames, not seconds, so a scenario's captures do not depend on how fast the machine renders

    private readonly PanelContainer bar = new() { Name = "TopBar", MouseFilter = MouseFilterEnum.Stop };
    private readonly HBoxContainer row = new() { Name = "Row" };
    private readonly Label banner = new() { Name = "Banner", Visible = false, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Button actionButton = new() { Name = "NextAction" };
    private readonly Button endTurnButton = new() { Name = "EndTurn", Text = HudModel.EndTurnLabel.TrText(), Visible = false };
    private readonly Dictionary<string, Control> entries = [];
    private UiTheme theme = null!; // JUSTIFY: C011 assigned by Init before the HUD is updated
    private IReadOnlyList<HudStat> stats = [];
    private int bannerLeft;

    public HudView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        bar.AddChild(row);
        AddChild(bar);
        AddChild(banner);
        actionButton.Pressed += () => ActionRequested?.Invoke(Action);
        endTurnButton.Pressed += () => EndTurnRequested?.Invoke();
        AddChild(endTurnButton);
        AddChild(actionButton);
    }

    /// <summary>What the next-action button currently means.</summary>
    public HudAction Action { get; private set; } = new(HudActionKind.EndTurn, HudModel.EndTurnLabel, null, true);

    public IReadOnlyList<HudStat> Stats => stats;

    public string ActionText => actionButton.Text;

    public bool EndTurnShown => endTurnButton.Visible;

    public string BannerText => banner.Text;

    public bool BannerVisible => banner.Visible;

    /// <summary>Keeps the banner up until <see cref="HideBanner"/> (for scenarios, which must not depend on wall time).</summary>
    public bool HoldBanner { get; set; }

    public Rect2 BarRect => bar.GetGlobalRect();

    /// <summary>The text of one top bar entry by its <see cref="HudStat.Id"/>, null when the bar has no such entry.</summary>
    public string? StatText(string id) => stats.FirstOrDefault(it => it.Id == id)?.Text;

    public event Action<HudAction>? ActionRequested;

    public event Action? EndTurnRequested;

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
        banner.AddThemeFontSizeOverride("font_size", uiTheme.Px(36));
        banner.AddThemeColorOverride("font_outline_color", ToColour(PlaceholderPalette.Unexplored));
        banner.AddThemeConstantOverride("outline_size", uiTheme.Px(8));
        actionButton.CustomMinimumSize = new Vector2(uiTheme.Px(180), uiTheme.Px(44));
        endTurnButton.CustomMinimumSize = new Vector2(uiTheme.Px(110), uiTheme.Px(44));
        uiTheme.ScaleChanged += () => LayoutIn(Size);
    }

    /// <summary>Positions the parts inside a screen of <paramref name="screen"/> pixels.</summary>
    public void LayoutIn(Vector2 screen)
    {
        Size = screen;
        float barHeight = theme?.Px(BarHeight) ?? BarHeight;
        bar.Position = Vector2.Zero;
        bar.Size = new Vector2(screen.X, barHeight);
        actionButton.Position = screen - actionButton.CustomMinimumSize - new Vector2(Margin, Margin);
        actionButton.Size = actionButton.CustomMinimumSize;
        endTurnButton.Size = endTurnButton.CustomMinimumSize;
        endTurnButton.Position = actionButton.Position - new Vector2(endTurnButton.CustomMinimumSize.X + Margin, 0f);
        banner.Size = new Vector2(screen.X, banner.GetCombinedMinimumSize().Y);
        banner.Position = new Vector2(0f, screen.Y * 0.28f);
    }

    /// <summary>Height of the top bar in pixels (the minimap sits below it).</summary>
    public float TopBarHeight => theme?.Px(BarHeight) ?? BarHeight;

    public void Update(EmpireTopBarResult topBar, HudAction action, bool canEndTurn)
    {
        stats = HudModel.TopBar(topBar);
        Rebuild();
        Action = action;
        actionButton.Text = action.Label.TrText();
        actionButton.Disabled = !action.Enabled;
        endTurnButton.Visible = action.Kind != HudActionKind.EndTurn && canEndTurn && action.Enabled;
    }

    public void ShowBanner(string text)
    {
        banner.Text = text.TrText();
        banner.Visible = true;
        bannerLeft = BannerFrames;
        LayoutIn(Size);
    }

    public void HideBanner()
    {
        HoldBanner = false;
        banner.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!banner.Visible || HoldBanner) return;
        bannerLeft--;
        if (bannerLeft <= 0) banner.Visible = false;
    }

    private void Rebuild()
    {
        foreach (var child in row.GetChildren())
        {
            row.RemoveChild(child); // at once: a queued free would leave the old entries in the row (and its width) until the frame ends
            child.QueueFree();
        }
        entries.Clear();
        foreach (var stat in stats)
        {
            if (stat.Id == "Turn") row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
            Control item = stat.Icon is { } icon ? new IconLabel(theme, icon, stat.Text) : new Label { Text = stat.Text };
            item.Name = stat.Id.Replace(':', '_');
            item.TooltipText = stat.Tooltip.TrText();
            row.AddChild(item);
            entries[stat.Id] = item;
        }
        row.AddThemeConstantOverride("separation", theme.Px(20));
    }

    private static Color ToColour(Rgb rgb) => new(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);
}
