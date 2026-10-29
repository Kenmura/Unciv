using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Assets;

/// <summary>
/// C062: builds the whole UI <see cref="Theme"/> from <see cref="UiTokens"/> (design/ui-redesign.md 2.1): the base controls plus the type variations of
/// <see cref="UiVariants"/>. Every size goes through <see cref="UiTokens.Scaled"/> (borders at least 1 px), so one scale reaches fonts, margins, radii and icons together.
/// <see cref="Populate"/> clears and refills a theme in place, which is how a UI scale change reaches live controls.
/// </summary>
public static class ThemeBuilder
{
    public static Theme Build(float scale = 1f, bool compact = false)
    {
        var theme = new Theme();
        Populate(theme, scale, compact);
        return theme;
    }

    /// <summary>Replaces everything in <paramref name="theme"/> by the tokens at <paramref name="scale"/>; <paramref name="compact"/> picks the tighter padding and control heights of the Compact breakpoint.</summary>
    public static void Populate(Theme theme, float scale, bool compact)
    {
        theme.Clear();
        var m = new Metrics(scale, compact);
        theme.DefaultFont = FontChain.Default;
        theme.DefaultFontSize = m.Px(UiTokens.FontSize);
        AddPanels(theme, m);
        AddButtons(theme, m);
        AddLabels(theme, m);
        AddControls(theme, m);
    }

    private readonly record struct Metrics(float Scale, bool Compact)
    {
        public int Px(int unscaled) => UiTokens.Scaled(unscaled, Scale);
        public int Border(int unscaled) => UiTokens.ScaledAtLeastOne(unscaled, Scale);
        public int PanelPad => Px(Compact ? UiTokens.S2 : UiTokens.S3);
        public int ControlHeight => Px(Compact ? UiTokens.CompactControlMinHeight : UiTokens.ControlMinHeight);

        /// <summary>Vertical padding that brings a one-line control of the body font up to <see cref="ControlHeight"/>.</summary>
        public int ButtonPadY => Math.Max(Px(UiTokens.S1), (ControlHeight - (int)MathF.Ceiling(FontChain.Default.GetHeight(Px(UiTokens.FontSize))) + 1) / 2);
    }

    private static Color C(Rgb rgb) => ProceduralAssets.ToColor(rgb);

    private static Color C(Rgb rgb, int percent) => C(rgb) with { A = percent / 100f };

    private static readonly Color Clear = new(0f, 0f, 0f, 0f);

    private sealed class Box
    {
        public Color Fill;
        public Color Border;
        public int Radius = UiTokens.RadiusControl;
        public int BorderWidth = UiTokens.BorderWidth;
        public int TopBorderWidth = -1;
        public int BottomBorderWidth = -1;
        public int PadX;
        public int PadY;
        public bool Shadow;
        public bool Ring;
    }

    private static StyleBoxFlat Make(Metrics m, Box b)
    {
        var box = new StyleBoxFlat { BgColor = b.Fill, BorderColor = b.Border, DrawCenter = !b.Ring };
        int w = b.BorderWidth == 0 ? 0 : m.Border(b.BorderWidth);
        box.SetBorderWidthAll(w);
        if (b.TopBorderWidth >= 0) box.BorderWidthTop = m.Border(b.TopBorderWidth);
        if (b.BottomBorderWidth >= 0) box.BorderWidthBottom = m.Border(b.BottomBorderWidth);
        box.SetCornerRadiusAll(m.Px(b.Radius));
        box.ContentMarginLeft = box.ContentMarginRight = b.PadX;
        box.ContentMarginTop = box.ContentMarginBottom = b.PadY;
        if (b.Shadow)
        {
            box.ShadowColor = C(UiTokens.Bg0, UiTokens.ShadowPercent);
            box.ShadowSize = m.Px(UiTokens.ShadowSize);
            box.ShadowOffset = new Vector2(0f, m.Px(UiTokens.ShadowOffsetY));
        }
        return box;
    }

    private static void Panel(Theme theme, Metrics m, string type, Box b)
    {
        theme.SetStylebox("panel", type, Make(m, b));
    }

    private static void AddPanels(Theme theme, Metrics m)
    {
        int pad = m.PanelPad;
        var basePanel = new Box { Fill = C(UiTokens.Surface1), Border = C(UiTokens.BorderStrong), Radius = UiTokens.RadiusPanel, PadX = pad, PadY = pad };
        Panel(theme, m, "PanelContainer", basePanel);
        Panel(theme, m, "Panel", basePanel);
        Panel(theme, m, "PopupPanel", basePanel);

        void Variation(string name, Box b)
        {
            theme.SetTypeVariation(name, "PanelContainer");
            Panel(theme, m, name, b);
        }
        Variation(UiVariants.PanelCard, new Box { Fill = C(UiTokens.Surface2), Border = C(UiTokens.BorderSubtle), Radius = UiTokens.RadiusCard, PadX = pad, PadY = pad });
        Variation(UiVariants.PanelInset, new Box { Fill = C(UiTokens.Inset), Border = C(UiTokens.BorderSubtle), Radius = UiTokens.RadiusCard, PadX = m.Px(UiTokens.S2), PadY = m.Px(UiTokens.S2) });
        Variation(UiVariants.PanelModal, new Box
        {
            Fill = C(UiTokens.Surface1), Border = C(UiTokens.BorderStrong), Radius = UiTokens.RadiusPanel, TopBorderWidth = UiTokens.HairlineWidth,
            PadX = m.Px(UiTokens.S4), PadY = m.Px(UiTokens.S4), Shadow = true,
        });
        var tooltip = new Box { Fill = C(UiTokens.Surface3), Border = C(UiTokens.BorderStrong), Radius = UiTokens.RadiusCard, PadX = m.Px(UiTokens.S3), PadY = m.Px(UiTokens.S2), Shadow = true };
        Variation(UiVariants.PanelTooltip, tooltip);
        Panel(theme, m, "TooltipPanel", tooltip);
        Variation(UiVariants.PanelHeader, new Box
        {
            Fill = C(UiTokens.Surface1), Border = C(UiTokens.BorderSubtle), Radius = 0, BorderWidth = 0, TopBorderWidth = UiTokens.HairlineWidth, BottomBorderWidth = UiTokens.BorderWidth,
            PadX = m.Px(UiTokens.S3), PadY = m.Px(UiTokens.S2),
        });
        // the HUD bottom dock: rounded on top, shadowed, 1 px border
        var dock = Make(m, new Box { Fill = C(UiTokens.Surface1), Border = C(UiTokens.BorderStrong), Radius = UiTokens.RadiusPanel, PadX = pad, PadY = pad, Shadow = true });
        dock.CornerRadiusBottomLeft = dock.CornerRadiusBottomRight = 0;
        theme.SetTypeVariation(UiVariants.PanelDock, "PanelContainer");
        theme.SetStylebox("panel", UiVariants.PanelDock, dock);
        // C065: the count pill of a Badge, the key chip of a KeyHint and the two zebra cells of a DataTable
        int pill = m.Px(UiTokens.S1);
        Variation(UiVariants.PanelBadge, new Box { Fill = C(UiTokens.Danger), Border = C(UiTokens.Danger), Radius = UiTokens.RadiusPill, BorderWidth = 0, PadX = pill, PadY = 0 });
        Variation(UiVariants.PanelBadgeAccent, new Box { Fill = C(UiTokens.Accent), Border = C(UiTokens.Accent), Radius = UiTokens.RadiusPill, BorderWidth = 0, PadX = pill, PadY = 0 });
        Variation(UiVariants.PanelKeyHint, new Box { Fill = C(UiTokens.Inset), Border = C(UiTokens.BorderSubtle), Radius = UiTokens.RadiusControl, PadX = pill, PadY = 0 });
        Variation(UiVariants.PanelCellEven, new Box { Fill = C(UiTokens.Surface1), Border = C(UiTokens.Surface1), Radius = 0, BorderWidth = 0, PadX = m.Px(UiTokens.S2), PadY = m.Px(UiTokens.S1) });
        Variation(UiVariants.PanelCellOdd, new Box { Fill = C(UiTokens.Surface2), Border = C(UiTokens.Surface2), Radius = 0, BorderWidth = 0, PadX = m.Px(UiTokens.S2), PadY = m.Px(UiTokens.S1) });
        Variation(UiVariants.PanelSegmentTrack, new Box { Fill = C(UiTokens.Inset), Border = C(UiTokens.BorderSubtle), Radius = UiTokens.RadiusControl, PadX = m.Border(UiTokens.RingWidth), PadY = m.Border(UiTokens.RingWidth) });
    }

    private readonly record struct ButtonLook(Box Normal, Box Hover, Box Pressed, Box Disabled, Rgb Text, Rgb TextHover, Rgb TextPressed, Rgb TextDisabled);

    private static void AddButtons(Theme theme, Metrics m)
    {
        int padX = m.Px(UiTokens.S3);
        int padY = m.ButtonPadY;
        Box Fill(Rgb fill, Rgb border, int radius = UiTokens.RadiusControl, int borderWidth = UiTokens.BorderWidth, bool padIcon = false) => new()
        {
            Fill = C(fill), Border = C(border), Radius = radius, BorderWidth = borderWidth, PadX = padIcon ? (m.ControlHeight - m.Px(UiTheme.IconSize)) / 2 : padX,
            PadY = padIcon ? (m.ControlHeight - m.Px(UiTheme.IconSize)) / 2 : padY,
        };

        // the default Button is the secondary style
        var secondary = new ButtonLook(
            Fill(UiTokens.Surface2, UiTokens.BorderSubtle), Fill(UiTokens.Surface3, UiTokens.BorderStrong), Fill(UiTokens.Inset, UiTokens.BorderStrong), Fill(UiTokens.Surface1, UiTokens.BorderSubtle),
            UiTokens.TextPrimary, UiTokens.AccentHi, UiTokens.Accent, UiTokens.TextDisabled);
        ApplyButton(theme, m, "Button", secondary);
        theme.SetTypeVariation(UiVariants.ButtonSecondary, "Button");
        ApplyButton(theme, m, UiVariants.ButtonSecondary, secondary);

        Variation(UiVariants.ButtonPrimary, new ButtonLook(
            Fill(UiTokens.Accent, UiTokens.Accent), Fill(UiTokens.AccentHi, UiTokens.AccentHi), Fill(UiTokens.AccentLo, UiTokens.AccentLo), Fill(UiTokens.Surface2, UiTokens.BorderSubtle),
            UiTokens.OnAccent, UiTokens.OnAccent, UiTokens.TextPrimary, UiTokens.TextDisabled));

        Box Transparent(Rgb? fill = null, Rgb? border = null, int radius = UiTokens.RadiusControl, int borderWidth = 0, bool padIcon = false)
        {
            var b = Fill(fill ?? UiTokens.Bg0, border ?? UiTokens.BorderSubtle, radius, borderWidth, padIcon);
            if (fill == null) b.Fill = Clear;
            return b;
        }
        var clear = Transparent();
        Variation(UiVariants.ButtonGhost, new ButtonLook(
            clear, Transparent(UiTokens.Surface2), Transparent(UiTokens.Inset), Transparent(),
            UiTokens.TextSecondary, UiTokens.TextPrimary, UiTokens.Accent, UiTokens.TextDisabled));
        Variation(UiVariants.ButtonDanger, new ButtonLook(
            Fill(UiTokens.Surface2, UiTokens.Danger), Fill(UiTokens.Danger, UiTokens.Danger), Fill(UiTokens.Danger.Scale(75), UiTokens.Danger), Fill(UiTokens.Surface1, UiTokens.BorderSubtle),
            UiTokens.DangerText, UiTokens.TextPrimary, UiTokens.TextPrimary, UiTokens.TextDisabled));
        Variation(UiVariants.ButtonIcon, new ButtonLook(
            Transparent(padIcon: true), Transparent(UiTokens.Surface2, padIcon: true), Transparent(UiTokens.Inset, padIcon: true), Transparent(padIcon: true),
            UiTokens.TextSecondary, UiTokens.AccentHi, UiTokens.Accent, UiTokens.TextDisabled));

        // tab: a 2 px accent underline when pressed
        Box Tab(Rgb? fill, Rgb underline, bool line)
        {
            var b = Transparent(fill, underline, 0, 0);
            if (line) b.BottomBorderWidth = UiTokens.RingWidth;
            return b;
        }
        Variation(UiVariants.ButtonTab, new ButtonLook(
            Tab(null, UiTokens.Accent, false), Tab(UiTokens.Surface2, UiTokens.Accent, false), Tab(null, UiTokens.Accent, true), Tab(null, UiTokens.Accent, false),
            UiTokens.TextSecondary, UiTokens.TextPrimary, UiTokens.TextPrimary, UiTokens.TextDisabled));

        // chip: a pill; toggled shows the accent tint and ring
        var chipOn = new Box { Fill = C(UiTokens.AccentLo, UiTokens.ChipTintPercent), Border = C(UiTokens.Accent), Radius = UiTokens.RadiusPill, PadX = padX, PadY = padY };
        Variation(UiVariants.ButtonChip, new ButtonLook(
            Fill(UiTokens.Surface2, UiTokens.BorderSubtle, UiTokens.RadiusPill), Fill(UiTokens.Surface3, UiTokens.BorderStrong, UiTokens.RadiusPill), chipOn, Fill(UiTokens.Surface1, UiTokens.BorderSubtle, UiTokens.RadiusPill),
            UiTokens.TextPrimary, UiTokens.TextPrimary, UiTokens.AccentHi, UiTokens.TextDisabled));
        // segment: chips joined in an Inset track, the selected one AccentLo
        Variation(UiVariants.ButtonSegment, new ButtonLook(
            Transparent(), Transparent(UiTokens.Surface2), Fill(UiTokens.AccentLo, UiTokens.AccentLo), Transparent(),
            UiTokens.TextSecondary, UiTokens.TextPrimary, UiTokens.TextPrimary, UiTokens.TextDisabled));

        // row: a list row; hover and the selected (pressed) state lift to Surface3, the selection bar is drawn by ListRow
        Variation(UiVariants.ButtonRow, new ButtonLook(
            Transparent(radius: 0), Transparent(UiTokens.Surface3, radius: 0), Transparent(UiTokens.Surface3, radius: 0), Transparent(radius: 0),
            UiTokens.TextPrimary, UiTokens.TextPrimary, UiTokens.TextPrimary, UiTokens.TextDisabled));

        // city tiles: the box is invisible, the hex is drawn by the tile from "hex_fill" and "hex_border"
        void Hex(string name, Rgb fill, Rgb border)
        {
            Variation(name, new ButtonLook(clear, clear, clear, clear, UiTokens.TextPrimary, UiTokens.TextPrimary, UiTokens.TextPrimary, UiTokens.TextDisabled));
            theme.SetStylebox("focus", name, new StyleBoxEmpty());
            theme.SetColor("hex_fill", name, C(fill));
            theme.SetColor("hex_border", name, C(border));
        }
        Hex(UiVariants.CityTileCenter, UiTokens.AccentLo, UiTokens.Accent);
        Hex(UiVariants.CityTileWorked, UiTokens.Success.Scale(50), UiTokens.Success);
        Hex(UiVariants.CityTileTaken, UiTokens.Surface3, UiTokens.BorderStrong);
        Hex(UiVariants.CityTileAvailable, UiTokens.Info.Scale(50), UiTokens.Info);
        Hex(UiVariants.CityTileUnavailable, UiTokens.Surface1, UiTokens.BorderSubtle);
        Hex(UiVariants.CityTileForeign, UiTokens.Danger.Scale(50), UiTokens.Danger);
        Hex(UiVariants.CityTileUnowned, UiTokens.Inset, UiTokens.BorderSubtle);

        // tech tree node cards: the content margin is the card's own, the pressed state is the selection (a lifted fill and a bright 2 px border)
        Box Node(Rgb fill, Rgb border, int borderWidth = UiTokens.BorderWidth) => new() { Fill = C(fill), Border = C(border), BorderWidth = borderWidth };
        void TechNode(string name, Rgb fill, Rgb border, Rgb text, int borderWidth = UiTokens.BorderWidth) =>
            Variation(name, new ButtonLook(
                Node(fill, border, borderWidth), Node(UiTokens.Surface3, border, borderWidth), Node(UiTokens.Surface3, UiTokens.AccentHi, UiTokens.RingWidth), Node(fill, border, borderWidth),
                text, text, text, text));
        TechNode(UiVariants.TechNodeResearched, UiTokens.Surface1, UiTokens.BorderSubtle, UiTokens.TextSecondary);
        TechNode(UiVariants.TechNodeCurrent, UiTokens.Surface2, UiTokens.Accent, UiTokens.TextPrimary, UiTokens.RingWidth);
        TechNode(UiVariants.TechNodeQueued, UiTokens.Surface2, UiTokens.AccentLo, UiTokens.TextPrimary);
        TechNode(UiVariants.TechNodeResearchable, UiTokens.Surface2, UiTokens.BorderStrong, UiTokens.TextPrimary);
        TechNode(UiVariants.TechNodeLocked, UiTokens.Surface1, UiTokens.BorderSubtle, UiTokens.TextMuted);

        // policy node cards (C071): same look as the tech cards; adopted rings in the accent, one that can be adopted now in the info colour
        TechNode(UiVariants.PolicyNodeAdopted, UiTokens.Surface2, UiTokens.Accent, UiTokens.TextPrimary, UiTokens.RingWidth);
        TechNode(UiVariants.PolicyNodeAvailable, UiTokens.Surface2, UiTokens.Info, UiTokens.TextPrimary, UiTokens.RingWidth);
        TechNode(UiVariants.PolicyNodeOpen, UiTokens.Surface2, UiTokens.BorderStrong, UiTokens.TextSecondary);
        TechNode(UiVariants.PolicyNodeLocked, UiTokens.Surface1, UiTokens.BorderSubtle, UiTokens.TextMuted);

        void Variation(string name, ButtonLook look)
        {
            theme.SetTypeVariation(name, "Button");
            ApplyButton(theme, m, name, look);
        }
    }

    /// <summary>C065: the filled part of a stat bar in <paramref name="colour"/> (a theme override of the bar's <c>fill</c>, so no view builds a stylebox).</summary>
    public static StyleBoxFlat BarFill(Rgb colour, bool labelled, float scale)
    {
        var m = new Metrics(scale, false);
        int height = labelled ? UiTokens.BarLabelled : UiTokens.BarThin;
        return Make(m, new Box { Fill = C(colour), Border = C(colour), Radius = height / 2, BorderWidth = 0 });
    }

    private static void ApplyButton(Theme theme, Metrics m, string type, ButtonLook look)
    {
        theme.SetStylebox("normal", type, Make(m, look.Normal));
        theme.SetStylebox("hover", type, Make(m, look.Hover));
        theme.SetStylebox("pressed", type, Make(m, look.Pressed));
        theme.SetStylebox("hover_pressed", type, Make(m, look.Pressed));
        theme.SetStylebox("disabled", type, Make(m, look.Disabled));
        // focus is always a 2 px accent ring that leaves the fill alone
        theme.SetStylebox("focus", type, Make(m, new Box
        {
            Border = C(UiTokens.Accent), Radius = look.Normal.Radius, BorderWidth = UiTokens.RingWidth, Ring = true, PadX = look.Normal.PadX, PadY = look.Normal.PadY,
        }));
        theme.SetColor("font_color", type, C(look.Text));
        theme.SetColor("font_hover_color", type, C(look.TextHover));
        theme.SetColor("font_hover_pressed_color", type, C(look.TextPressed));
        theme.SetColor("font_pressed_color", type, C(look.TextPressed));
        theme.SetColor("font_focus_color", type, C(look.Text));
        theme.SetColor("font_disabled_color", type, C(look.TextDisabled));
        theme.SetColor("icon_normal_color", type, C(look.Text));
        theme.SetColor("icon_hover_color", type, C(look.TextHover));
        theme.SetColor("icon_pressed_color", type, C(look.TextPressed));
        theme.SetColor("icon_disabled_color", type, C(look.TextDisabled));
        theme.SetConstant("h_separation", type, m.Px(UiTokens.S2));
        theme.SetConstant("outline_size", type, 0);
    }

    private static void AddLabels(Theme theme, Metrics m)
    {
        theme.SetColor("font_color", "Label", C(UiTokens.TextPrimary));
        void Style(string variation, TypeStyle style, Rgb colour)
        {
            theme.SetTypeVariation(variation, "Label");
            theme.SetFont("font", variation, FontChain.For(style));
            theme.SetFontSize("font_size", variation, m.Px(style.Size));
            theme.SetColor("font_color", variation, C(colour));
        }
        Style(UiVariants.LabelDisplay, UiTokens.Display, UiTokens.TextPrimary);
        Style(UiVariants.LabelTitle, UiTokens.Title, UiTokens.TextPrimary);
        Style(UiVariants.LabelHeading, UiTokens.Heading, UiTokens.TextPrimary);
        Style(UiVariants.LabelBodyStrong, UiTokens.BodyStrong, UiTokens.TextPrimary);
        Style(UiVariants.LabelNumber, UiTokens.Number, UiTokens.TextPrimary);
        Style(UiVariants.LabelSmall, UiTokens.Small, UiTokens.TextSecondary);
        Style(UiVariants.LabelOverline, UiTokens.Overline, UiTokens.TextMuted);
        // tones keep the default face and size
        foreach (var (variation, colour) in new[] { (UiVariants.LabelSecondary, UiTokens.TextSecondary), (UiVariants.LabelMuted, UiTokens.TextMuted) })
        {
            theme.SetTypeVariation(variation, "Label");
            theme.SetColor("font_color", variation, C(colour));
        }
    }

    private static void AddControls(Theme theme, Metrics m)
    {
        // text fields: an inset well, the accent ring when focused
        int fieldPadX = m.Px(UiTokens.S2);
        int fieldPadY = Math.Max(m.Px(UiTokens.S1), (m.ControlHeight - (int)MathF.Ceiling(FontChain.Default.GetHeight(m.Px(UiTokens.FontSize))) + 1) / 2);
        theme.SetStylebox("normal", "LineEdit", Make(m, new Box { Fill = C(UiTokens.Inset), Border = C(UiTokens.BorderSubtle), PadX = fieldPadX, PadY = fieldPadY }));
        theme.SetStylebox("read_only", "LineEdit", Make(m, new Box { Fill = C(UiTokens.Surface1), Border = C(UiTokens.BorderSubtle), PadX = fieldPadX, PadY = fieldPadY }));
        theme.SetStylebox("focus", "LineEdit", Make(m, new Box { Fill = C(UiTokens.Inset), Border = C(UiTokens.Accent), BorderWidth = UiTokens.RingWidth, PadX = fieldPadX, PadY = fieldPadY }));
        theme.SetColor("font_color", "LineEdit", C(UiTokens.TextPrimary));
        theme.SetColor("font_placeholder_color", "LineEdit", C(UiTokens.TextMuted));
        theme.SetColor("font_uneditable_color", "LineEdit", C(UiTokens.TextSecondary));
        theme.SetColor("caret_color", "LineEdit", C(UiTokens.AccentHi));
        theme.SetColor("selection_color", "LineEdit", C(UiTokens.AccentLo, 70));

        foreach (string type in new[] { "CheckBox", "CheckButton" })
        {
            theme.SetColor("font_color", type, C(UiTokens.TextPrimary));
            theme.SetColor("font_hover_color", type, C(UiTokens.AccentHi));
            theme.SetColor("font_pressed_color", type, C(UiTokens.TextPrimary));
            theme.SetColor("font_hover_pressed_color", type, C(UiTokens.AccentHi));
            theme.SetColor("font_disabled_color", type, C(UiTokens.TextDisabled));
            foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            {
                theme.SetStylebox(state, type, new StyleBoxEmpty());
            }
            theme.SetStylebox("focus", type, Make(m, new Box { Border = C(UiTokens.Accent), BorderWidth = UiTokens.RingWidth, Ring = true, PadX = m.Px(UiTokens.S1), PadY = m.Px(UiTokens.S1) }));
        }
        AddSwitch(theme, m);

        foreach (var (name, height, fill) in new[] { (UiVariants.StatBarThin, UiTokens.BarThin, false), (UiVariants.StatBarLabelled, UiTokens.BarLabelled, true) })
        {
            theme.SetTypeVariation(name, "ProgressBar");
            int h = m.Px(height);
            var track = Make(m, new Box { Fill = C(UiTokens.Inset), Border = C(UiTokens.BorderSubtle), Radius = height / 2, BorderWidth = fill ? UiTokens.BorderWidth : 0 });
            track.ContentMarginTop = track.ContentMarginBottom = fill ? 0 : h / 2f;
            theme.SetStylebox("background", name, track);
            theme.SetStylebox("fill", name, Make(m, new Box { Fill = C(UiTokens.Accent), Border = C(UiTokens.Accent), Radius = height / 2, BorderWidth = 0 }));
            theme.SetColor("font_color", name, C(UiTokens.TextPrimary));
            theme.SetFontSize("font_size", name, m.Px(UiTokens.Overline.Size));
        }

        var line = new StyleBoxLine { Color = C(UiTokens.BorderSubtle), Thickness = m.Border(UiTokens.BorderWidth) };
        theme.SetStylebox("separator", "HSeparator", line);
        theme.SetStylebox("separator", "VSeparator", new StyleBoxLine { Color = C(UiTokens.BorderSubtle), Thickness = m.Border(UiTokens.BorderWidth), Vertical = true });
        theme.SetConstant("separation", "HSeparator", m.Px(UiTokens.S2));
        theme.SetConstant("separation", "VSeparator", m.Px(UiTokens.S2));
    }

    /// <summary>The check button as a switch: a pill track with a round knob, drawn here at the scaled size (grey off, accent on).</summary>
    private static void AddSwitch(Theme theme, Metrics m)
    {
        theme.SetTypeVariation(UiVariants.ToggleSwitch, "CheckButton");
        int w = m.Px(UiTokens.SwitchWidth), h = m.Px(UiTokens.SwitchHeight);
        var on = Pill(w, h, UiTokens.Accent, UiTokens.OnAccent, true);
        var off = Pill(w, h, UiTokens.Surface3, UiTokens.TextSecondary, false);
        var onDisabled = Pill(w, h, UiTokens.AccentLo, UiTokens.TextDisabled, true);
        var offDisabled = Pill(w, h, UiTokens.Surface2, UiTokens.TextDisabled, false);
        foreach (string suffix in new[] { "", "_mirrored" })
        {
            theme.SetIcon("checked" + suffix, UiVariants.ToggleSwitch, on);
            theme.SetIcon("unchecked" + suffix, UiVariants.ToggleSwitch, off);
            theme.SetIcon("checked_disabled" + suffix, UiVariants.ToggleSwitch, onDisabled);
            theme.SetIcon("unchecked_disabled" + suffix, UiVariants.ToggleSwitch, offDisabled);
        }
    }

    private static ImageTexture Pill(int w, int h, Rgb track, Rgb knob, bool knobRight)
    {
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        float radius = h / 2f, inset = Math.Max(2f, h * 0.18f), knobRadius = radius - inset;
        float knobX = knobRight ? w - radius : radius;
        Color trackColour = C(track), knobColour = C(knob);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                // distance to the pill's core segment decides the track coverage, distance to the knob centre the knob's
                float cx = Math.Clamp(px, radius, w - radius);
                float trackCover = Math.Clamp(radius - MathF.Sqrt((px - cx) * (px - cx) + (py - radius) * (py - radius)) + 0.5f, 0f, 1f);
                float knobCover = Math.Clamp(knobRadius - MathF.Sqrt((px - knobX) * (px - knobX) + (py - radius) * (py - radius)) + 0.5f, 0f, 1f);
                var colour = trackColour.Lerp(knobColour, knobCover);
                image.SetPixel(x, y, colour with { A = trackCover });
            }
        }
        return ImageTexture.CreateFromImage(image);
    }
}
