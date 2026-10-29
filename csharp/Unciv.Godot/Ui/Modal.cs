using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>How a modal's button looks and where it sits: <see cref="Primary"/> is the one the player is expected to press (right-most), <see cref="Danger"/> an irreversible one.</summary>
public enum ChoiceRole
{
    Secondary,
    Primary,
    Ghost,
    Danger,
}

/// <summary>
/// C066: the one frame of every popup (design/ui-redesign.md 2.5). A scrim, then a centred panel with the brass hairline on top: the title (and an optional subtitle), the
/// <see cref="Body"/> the owner fills, and a footer of buttons laid out by <see cref="SetActions"/>: in one right-aligned row with the primary one right-most, or stacked
/// when there are more than <see cref="MaxRowActions"/>. Escape runs <see cref="OnCancel"/>; a popup whose owner can be cancelled overrides it. The panel is
/// <see cref="MinWidth"/> to <see cref="MaxWidth"/> wide but never more than <see cref="ViewportShare"/> of the window, whatever the UI scale.
/// </summary>
public partial class Modal : Control
{
    public const int MinWidth = 360;
    public const int MaxWidth = 560;
    public const float ViewportShare = 0.9f;
    public const int MaxRowActions = 3;

    private readonly ColorRect scrim = new() { Name = "Scrim", MouseFilter = MouseFilterEnum.Ignore };
    private readonly AccentPanel panel = new() { Name = "ModalPanel", ThemeTypeVariation = UiVariants.PanelModal };
    private readonly VBoxContainer column = new() { Name = "ModalColumn" };
    private readonly HBoxContainer headerRow = new() { Name = "ModalHeader" };
    private readonly TextureRect headerIcon = new() { Name = "ModalIcon", Visible = false, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label title = new() { Name = "ModalTitle", ThemeTypeVariation = UiVariants.LabelHeading, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
    private readonly Label subtitle = new() { Name = "ModalSubtitle", ThemeTypeVariation = UiVariants.LabelSecondary, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
    private readonly VBoxContainer body = new() { Name = "ModalBody" };
    private readonly VBoxContainer footer = new() { Name = "ModalFooter" };
    private UiTheme? theme;
    private bool opaque;

    public Modal()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        this.Fill();
        scrim.Fill();
        AddChild(scrim);
        var centre = new CenterContainer { Name = "ModalCentre", MouseFilter = MouseFilterEnum.Ignore };
        centre.Fill();
        AddChild(centre);
        centre.AddChild(panel);
        panel.AddChild(column);
        headerRow.AddChild(headerIcon);
        headerRow.AddChild(title);
        column.AddChild(headerRow);
        column.AddChild(subtitle);
        column.AddChild(body);
        column.AddChild(footer);
    }

    /// <summary>The UI theme the modal was initialised with.</summary>
    public UiTheme Ui => theme ?? throw new InvalidOperationException("Init first");

    /// <summary>The heading label (<see cref="UiVariants.LabelHeading"/>).</summary>
    public Label TitleLabel => title;

    public Label SubtitleLabel => subtitle;

    /// <summary>The vertical box between the header and the footer that the owner fills.</summary>
    public VBoxContainer Body => body;

    /// <summary>The framed panel (the harness reads its rectangle and hairline).</summary>
    public AccentPanel Frame => panel;

    /// <summary>The buttons of the footer, in the order they are laid out (left to right, or top to bottom when stacked).</summary>
    public IReadOnlyList<Button> Actions => ActionHost.GetChildren().OfType<Button>().ToList();

    /// <summary>True when the footer buttons are stacked in a column instead of one row.</summary>
    public bool Stacked { get; private set; }

    /// <summary>The hairline rectangle in global coordinates.</summary>
    public Rect2 HairlineRect => new(panel.GlobalPosition + panel.LineRect.Position, panel.LineRect.Size);

    private Control ActionHost => footer.GetChildCount() > 0 && footer.GetChild(0) is HFlowContainer row ? row : footer;

    public virtual void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
        panel.Bind(uiTheme, UiTokens.RadiusPanel);
        column.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S3));
        headerRow.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S3));
        footer.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        ApplyScrim();
    }

    /// <summary>A fully opaque scrim, for a screen that must not let the game show through (the hotseat hand-over).</summary>
    public bool Opaque
    {
        get => opaque;
        set
        {
            opaque = value;
            ApplyScrim();
        }
    }

    public void LayoutIn(Vector2 screen) => Size = screen;

    /// <summary>Sets the header: the title and, when <paramref name="subtitleText"/> is given, a smaller line under it. Both are translated. With <paramref name="icon"/> a 40 px icon sits left of the title (C067).</summary>
    public void SetHeader(string titleText, string? subtitleText = null, AssetKey? icon = null)
    {
        headerIcon.Visible = icon != null && theme != null;
        if (icon is { } key && theme != null)
        {
            headerIcon.Texture = theme.Icon(key);
            headerIcon.CustomMinimumSize = new Vector2(theme.Px(40), theme.Px(40));
        }
        title.Text = titleText.TrText();
        subtitle.Text = (subtitleText ?? "").TrText();
        subtitle.Visible = !string.IsNullOrEmpty(subtitleText);
    }

    /// <summary>
    /// Sizes the panel for <paramref name="preferredWidth"/> unscaled pixels (clamped to <see cref="MinWidth"/>..<see cref="MaxWidth"/> and to the window) and returns the width
    /// the owner's body text can wrap at. Call before filling <see cref="Body"/>, while the modal is in the tree.
    /// </summary>
    public float Fit(int preferredWidth)
    {
        var ui = theme ?? throw new InvalidOperationException("Init first");
        float window = IsInsideTree() ? GetViewportRect().Size.X : 640f;
        float width = Math.Min(ui.Px(Math.Clamp(preferredWidth, MinWidth, MaxWidth)), window * ViewportShare);
        panel.CustomMinimumSize = new Vector2(width, 0);
        return Math.Max(ui.Px(120), width - 2 * ui.Px(UiTokens.S4) - 2 * ui.Px(UiTokens.BorderWidth));
    }

    /// <summary>C077: sizes a scroll area that holds <paramref name="content"/> in the body to the content's height, but at most <paramref name="share"/> of the window, so a tall body scrolls and the footer stays on screen. Call after the content is filled.</summary>
    public void FitScroll(ScrollContainer scroll, Control content, float share = 0.55f)
    {
        float window = IsInsideTree() ? GetViewportRect().Size.Y : 480f;
        scroll.CustomMinimumSize = new Vector2(scroll.CustomMinimumSize.X, Math.Min(content.GetCombinedMinimumSize().Y, window * share));
    }

    /// <summary>Replaces the footer buttons (the previous ones are freed, so a caller passes each button once). They are laid out by role (ghost, secondary, danger, primary: the primary one right-most) in one right-aligned row that wraps when too narrow, or in a column in the given order when there are more than <see cref="MaxRowActions"/>.</summary>
    public void SetActions(IReadOnlyList<(Button Button, ChoiceRole Role)> actions)
    {
        foreach (var child in footer.GetChildren())
        {
            footer.RemoveChild(child);
            child.QueueFree();
        }
        Stacked = actions.Count > MaxRowActions;
        foreach (var (button, role) in actions) button.ThemeTypeVariation = VariationOf(role);
        if (Stacked)
        {
            foreach (var (button, _) in actions)
            {
                button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                footer.AddChild(button);
            }
            return;
        }
        var row = new HFlowContainer { Name = "ModalActions", Alignment = FlowContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("h_separation", theme?.Px(UiTokens.S2) ?? UiTokens.S2);
        row.AddThemeConstantOverride("v_separation", theme?.Px(UiTokens.S2) ?? UiTokens.S2);
        foreach (var (button, _) in actions.OrderBy(it => Order(it.Role))) row.AddChild(button);
        footer.AddChild(row);
    }

    /// <summary>Escape. Returns whether the modal handled it (the key is then consumed); the default does nothing so the key goes on to the screen below.</summary>
    protected virtual bool OnCancel() => false;

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } && IsVisibleInTree() && OnCancel()) GetViewport().SetInputAsHandled();
    }

    /// <summary>The theme variation a button of <paramref name="role"/> takes.</summary>
    public static string VariationOf(ChoiceRole role) => role switch
    {
        ChoiceRole.Primary => UiVariants.ButtonPrimary,
        ChoiceRole.Ghost => UiVariants.ButtonGhost,
        ChoiceRole.Danger => UiVariants.ButtonDanger,
        _ => UiVariants.ButtonSecondary,
    };

    /// <summary>The place of a role in a row, left to right: ghost, secondary, danger, primary.</summary>
    internal static int Order(ChoiceRole role) => role switch
    {
        ChoiceRole.Ghost => 0,
        ChoiceRole.Secondary => 1,
        ChoiceRole.Danger => 2,
        _ => 3,
    };

    private void ApplyScrim() => scrim.Color = ProceduralAssets.ToColor(UiTokens.Scrim) with { A = opaque ? 1f : UiTokens.ScrimPercent / 100f };
}
