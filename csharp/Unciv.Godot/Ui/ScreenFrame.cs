using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// C066: the frame of a full-screen screen (design/ui-redesign.md 2.5): a header bar with the brass hairline on top (the title on the left, an optional strip such as a
/// <see cref="TabBar"/>'s tabs in the middle, a close button on the right), the content that fills the rest, and an optional footer bar of right-aligned actions with the
/// primary one right-most. The close button is an <see cref="IconButton"/> labelled "Close" with hotkey Esc; it raises <see cref="CloseRequested"/> and the screen decides what
/// closing means (Escape itself goes through <see cref="ScreenManager.Back"/> or the screen's own handler).
/// </summary>
public partial class ScreenFrame : VBoxContainer
{
    private readonly AccentPanel header = new() { Name = "FrameHeader", ThemeTypeVariation = UiVariants.PanelHeader };
    private readonly HBoxContainer headerRow = new();
    private readonly Label title = new() { Name = "FrameTitle", ThemeTypeVariation = UiVariants.LabelTitle, VerticalAlignment = VerticalAlignment.Center, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f };
    private readonly Control strip = new() { Name = "FrameStrip", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2f, Visible = false };
    private readonly MarginContainer content = new() { Name = "FrameContent", SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly PanelContainer footer = new() { Name = "FrameFooter", ThemeTypeVariation = UiVariants.PanelHeader, Visible = false };
    private readonly HBoxContainer footerRow = new() { Alignment = BoxContainer.AlignmentMode.End };
    private readonly IconButton close;

    public ScreenFrame(UiTheme theme, string titleText, bool closeButton = true, string closeLabel = "Close")
    {
        Name = "ScreenFrame";
        header.Bind(theme, 0);
        AddThemeConstantOverride("separation", 0);
        title.Text = titleText.TrText();
        headerRow.AddThemeConstantOverride("separation", theme.Px(UiTokens.S3));
        headerRow.AddChild(title);
        headerRow.AddChild(strip);
        close = new IconButton(theme, AssetKey.Of(RulesetAssetKeys.UiIcon, "Close"), closeLabel, hotkey: "Esc") { Visible = closeButton };
        close.Pressed += () => CloseRequested?.Invoke();
        headerRow.AddChild(close);
        header.AddChild(headerRow);
        AddChild(header);
        int margin = theme.Px(theme.Breakpoint == UiBreakpoint.Compact ? UiTokens.S2 : UiTokens.S4);
        foreach (string side in new[] { "left", "top", "right", "bottom" }) content.AddThemeConstantOverride("margin_" + side, margin);
        AddChild(content);
        footerRow.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        footer.AddChild(footerRow);
        AddChild(footer);
    }

    /// <summary>The close button was pressed.</summary>
    public event Action? CloseRequested;

    public string TitleText => title.Text;

    /// <summary>The title label (the harness checks that it is not squeezed out of the header).</summary>
    public Label TitleLabel => title;

    public IconButton CloseButton => close;

    /// <summary>The header bar with its hairline.</summary>
    public AccentPanel Header => header;

    /// <summary>The footer bar; visible only once <see cref="SetFooter"/> gave it actions.</summary>
    public Control Footer => footer;

    /// <summary>The footer buttons in layout order (left to right).</summary>
    public IReadOnlyList<Button> FooterActions => footerRow.GetChildren().OfType<Button>().ToList();

    /// <summary>The content area's own rectangle (below the header, above the footer).</summary>
    public Rect2 ContentRect => content.GetGlobalRect();

    /// <summary>Sets the screen's content, filling the area between header and footer. A control the frame already holds is freed.</summary>
    public void SetContent(Control page)
    {
        foreach (var child in content.GetChildren())
        {
            content.RemoveChild(child);
            child.QueueFree();
        }
        page.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        page.SizeFlagsVertical = SizeFlags.ExpandFill;
        content.AddChild(page);
    }

    /// <summary>Puts a control (a tab strip, a stepper) in the middle of the header: it gets two thirds of the room between the title and the close button, the title the rest (and is ellipsized when it does not fit).</summary>
    public void SetHeaderStrip(Control control)
    {
        foreach (var child in strip.GetChildren()) child.QueueFree();
        control.Fill();
        strip.AddChild(control);
        strip.Visible = true;
    }

    /// <summary>Sets the footer actions (the previous ones are freed): ghost and secondary first, the primary one right-most. No actions hides the footer.</summary>
    public void SetFooter(IReadOnlyList<(Button Button, ChoiceRole Role)> actions)
    {
        foreach (var child in footerRow.GetChildren())
        {
            footerRow.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var (button, role) in actions) button.ThemeTypeVariation = Modal.VariationOf(role);
        foreach (var (button, role) in actions.OrderBy(it => Modal.Order(it.Role))) footerRow.AddChild(button);
        footer.Visible = actions.Count > 0;
    }
}
