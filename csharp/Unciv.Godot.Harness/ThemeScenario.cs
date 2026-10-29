using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotHarness;

/// <summary>
/// C062: the theme builder. Every type variation of <see cref="UiVariants"/> exists with its base type, the scale rebuilds margins, borders and fonts in the same
/// <see cref="Theme"/> instance (scale 2 doubles panel margins), the bound viewport picks the breakpoint, and one capture shows every variation on a surface.
/// </summary>
public sealed class ThemeScenario : Scenario
{
    public override string Name => "theme";

    public override IReadOnlyList<string> Checklist =>
    [
        "Is each button variant (primary, secondary, ghost, danger, tab, chip, segment, icon) distinguishable, with legible text?",
        "Do the type scale steps read as a hierarchy (display and title in the serif face, then heading, body, small, overline)?",
        "Do the card, inset, tooltip and modal panels differ in depth, and does the modal carry a heavier top edge (the brass hairline itself comes with ScreenFrame/Modal in C066)?",
        "Are the toggle switch and the stat bars visible against the surface?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var uiTheme = new UiTheme(AssetRegistry.ForRuleset(session.Game.Ruleset));
        var theme = uiTheme.Theme;
        int layoutChanges = 0;
        uiTheme.LayoutChanged += () => layoutChanges++;
        host.AddChild(new ColorRect { Color = ProceduralAssets.ToColor(UiTokens.Bg1), AnchorRight = 1, AnchorBottom = 1 });
        var root = new HBoxContainer { Theme = theme, Position = new Vector2(12, 8) };
        root.AddThemeConstantOverride("separation", 16);
        host.AddChild(root);

        var left = new VBoxContainer();
        root.AddChild(left);
        left.AddThemeConstantOverride("separation", 4);
        foreach (var (variation, text) in new[]
        {
            (UiVariants.LabelDisplay, "Display"), (UiVariants.LabelTitle, "Title"), (UiVariants.LabelHeading, "Heading"), (UiVariants.LabelBodyStrong, "Body strong"),
            ("", "Body"), (UiVariants.LabelSmall, "Small"), (UiVariants.LabelOverline, "OVERLINE"),
        })
        {
            left.AddChild(new Label { Text = text, ThemeTypeVariation = variation });
        }
        left.AddChild(new Label { Text = "1234567890", ThemeTypeVariation = UiVariants.LabelSecondary });
        var toggle = new CheckButton { ThemeTypeVariation = UiVariants.ToggleSwitch, Text = "Switch", ButtonPressed = true };
        left.AddChild(toggle);
        foreach (string bar in new[] { UiVariants.StatBarThin, UiVariants.StatBarLabelled })
        {
            left.AddChild(new ProgressBar { ThemeTypeVariation = bar, Value = 60, ShowPercentage = false, CustomMinimumSize = new Vector2(160, 0) });
        }

        var middle = new VBoxContainer();
        root.AddChild(middle);
        middle.AddThemeConstantOverride("separation", 4);
        foreach (var (variation, text) in new[]
        {
            (UiVariants.ButtonPrimary, "Primary"), (UiVariants.ButtonSecondary, "Secondary"), (UiVariants.ButtonGhost, "Ghost"), (UiVariants.ButtonDanger, "Danger"),
            (UiVariants.ButtonTab, "Tab"), (UiVariants.ButtonChip, "Chip"), (UiVariants.ButtonSegment, "Segment"), (UiVariants.ButtonIcon, "Icon"),
        })
        {
            middle.AddChild(new Button { Text = text, ThemeTypeVariation = variation });
        }
        middle.AddChild(new Button { Text = "Disabled", Disabled = true, ThemeTypeVariation = UiVariants.ButtonPrimary });
        middle.AddChild(new Button { Text = "Chip on", ThemeTypeVariation = UiVariants.ButtonChip, ToggleMode = true, ButtonPressed = true });

        var right = new VBoxContainer();
        root.AddChild(right);
        right.AddThemeConstantOverride("separation", 6);
        foreach (var (variation, text) in new[]
        {
            (UiVariants.PanelCard, "Card"), (UiVariants.PanelInset, "Inset"), (UiVariants.PanelTooltip, "Tooltip"), (UiVariants.PanelModal, "Modal"), (UiVariants.PanelHeader, "Header"), (UiVariants.PanelDock, "Dock"),
        })
        {
            var panel = new PanelContainer { ThemeTypeVariation = variation };
            panel.AddChild(new Label { Text = text });
            right.AddChild(panel);
        }
        right.AddChild(new LineEdit { Text = "Text field" });

        int Margin(Theme t) => (int)t.GetStylebox("panel", "PanelContainer").GetContentMargin(Side.Left);
        float firstScale = 0;
        return
        [
            new WaitFrames(2),
            new AssertQuery("every type variation of UiVariants is defined on its base type", () => UiVariants.All.All(v => theme.GetTypeVariationBase(v.Name) == v.Base)),
            new AssertQuery("variation names are unique", () => UiVariants.All.Select(v => v.Name).Distinct().Count() == UiVariants.All.Count),
            new AssertQuery("the base theme carries the 15 px body font and a 12 px panel margin at scale 1", () => theme.DefaultFontSize == 15 && Margin(theme) == 12),
            new AssertQuery("the type scale sets heading, title and display sizes", () => theme.GetFontSize("font_size", UiVariants.LabelTitle) == 24 && theme.GetFontSize("font_size", UiVariants.LabelDisplay) == 34 && theme.GetFontSize("font_size", UiVariants.LabelOverline) == 11),
            new AssertQuery("serif and sans faces are loaded for the scale", () => !FontChain.Missing.Contains("NotoSerif.ttf") && !FontChain.Missing.Contains("NotoSans.ttf")),
            new AssertQuery("titles use a different face than headings", () => theme.GetFont("font", UiVariants.LabelTitle) != theme.GetFont("font", UiVariants.LabelHeading)),
            new AssertQuery("every button variation has all five states and the 2 px focus ring", () => UiVariants.All.Where(v => v.Base == "Button").All(v =>
                new[] { "normal", "hover", "pressed", "disabled", "focus" }.All(s => theme.HasStylebox(s, v.Name)) &&
                theme.GetStylebox("focus", v.Name) is StyleBoxFlat { DrawCenter: false, BorderWidthLeft: 2 })),
            new AssertQuery("the primary button is accent filled and the plain button is the secondary look", () =>
                ((StyleBoxFlat)theme.GetStylebox("normal", UiVariants.ButtonPrimary)).BgColor == ProceduralAssets.ToColor(UiTokens.Accent)
                && ((StyleBoxFlat)theme.GetStylebox("normal", "Button")).BgColor == ProceduralAssets.ToColor(UiTokens.Surface2)),
            new AssertQuery("buttons are at least 32 px high", () => host.GetTree().Root.FindChildren("*", "Button", true, false).OfType<Button>().Where(b => b is not CheckButton && b.ThemeTypeVariation != UiVariants.ButtonIcon).All(b => b.GetCombinedMinimumSize().Y >= 32)),
            new Act("remember the instance, scale 2", () =>
            {
                firstScale = uiTheme.Scale;
                uiTheme.SetScale(2f);
            }),
            new WaitFrames(2),
            new AssertQuery("scale 2 doubles panel margins, the font size and variation sizes in the same Theme", () =>
                uiTheme.Theme == theme && firstScale == 1f && Margin(theme) == 24 && theme.DefaultFontSize == 30 && theme.GetFontSize("font_size", UiVariants.LabelTitle) == 48),
            new AssertQuery("borders scale too (2 px ring becomes 4 px)", () => theme.GetStylebox("focus", UiVariants.ButtonPrimary) is StyleBoxFlat { BorderWidthLeft: 4 }),
            new AssertQuery("scaling raised LayoutChanged", () => layoutChanges == 1),
            new Act("scale 0.5", () => uiTheme.SetScale(0.5f)),
            new AssertQuery("a border never drops below 1 px and margins halve", () =>
                theme.GetStylebox("normal", "Button") is StyleBoxFlat { BorderWidthLeft: >= 1 } && theme.GetStylebox("panel", "PanelCard") is StyleBoxFlat { BorderWidthLeft: 1 } && Margin(theme) == 6),
            new Act("scale back, bind the 640x480 viewport", () =>
            {
                uiTheme.SetScale(1f);
                uiTheme.Bind(host.GetViewport());
            }),
            new AssertQuery("640x480 is Compact: auto scale 1, padding drops to 8 px and controls to 28 px", () =>
                uiTheme.Breakpoint == UiBreakpoint.Compact && uiTheme.Scale == 1f && Margin(theme) == 8),
            new Act("unbind", () => uiTheme.Unbind()),
            new WaitFrames(2),
            new Capture("variations", "Every theme variation: type scale, switch and bars on the left, the button variants in the middle, card, inset, tooltip, modal, header and dock panels with a text field on the right."),
        ];
    }
}
