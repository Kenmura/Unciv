using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C070: one technology of the tree as a card (design/ui-redesign.md 3.3): the tech icon on a badge, the name (ellipsized, the full name in the tooltip), the queue position, a row of
/// small icons for what it unlocks, the turns it needs and a thin progress bar. The research state shows in the border and the badge, not in a fill colour: the card takes one of the
/// <c>TechNode*</c> theme variations, and the pressed state is the selection. A double click raises <see cref="Activated"/>, a Shift+click <see cref="QueueRequested"/>.
/// </summary>
public partial class TechNodeCard : Button
{
    /// <summary>Unlock icons shown on the card; the rest is a "+N".</summary>
    public const int MaxUnlockIcons = 4;

    private readonly MarginContainer content = new() { Name = "Content", MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label nameLabel = new() { Name = "NodeName", ThemeTypeVariation = UiVariants.LabelBodyStrong, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
    private readonly UiTheme theme;
    private readonly bool researched;

    public TechNodeCard(UiTheme uiTheme, TechNode node, int width, int height)
    {
        theme = uiTheme;
        Node = node;
        researched = node.State == TechScreenModel.Researched;
        Name = "Tech_" + node.Name;
        ThemeTypeVariation = UiVariants.TechNode(node.State, node.IsCurrent);
        ToggleMode = true;
        ActionMode = ActionModeEnum.Release;
        Alignment = HorizontalAlignment.Left;
        CustomMinimumSize = new Vector2(width, height);
        Size = new Vector2(width, height);
        TooltipText = Tooltip(node);
        FocusMode = FocusModeEnum.All;

        int pad = uiTheme.Px(UiTokens.S2);
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        content.AddThemeConstantOverride("margin_left", pad + (researched ? uiTheme.Px(UiTokens.S1) : 0));
        content.AddThemeConstantOverride("margin_right", pad);
        content.AddThemeConstantOverride("margin_top", uiTheme.Px(UiTokens.S1));
        content.AddThemeConstantOverride("margin_bottom", uiTheme.Px(UiTokens.S1));
        AddChild(content);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", uiTheme.Px(2));
        content.AddChild(column);

        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        var fill = researched ? UiTokens.Success.Scale(45) : node.State == TechScreenModel.Locked ? UiTokens.Surface3 : UiTokens.Info.Scale(60);
        top.AddChild(new IconBadge(uiTheme, node.IconKey, ProceduralAssets.ToColor(fill), ProceduralAssets.ToColor(UiTokens.TextPrimary), 24));
        nameLabel.Text = node.Name.TrText();
        if (node.State == TechScreenModel.Locked) nameLabel.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(UiTokens.TextMuted));
        else if (researched) nameLabel.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(UiTokens.TextSecondary));
        top.AddChild(nameLabel);
        if (node.QueuePosition is { } position) top.AddChild(new Badge(position + 1, accent: true));
        else if (researched) top.AddChild(Glyph("Checkmark", 16, UiTokens.Success));
        column.AddChild(top);

        var bottom = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        bottom.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S1));
        foreach (var unlock in node.Unlocks.Take(MaxUnlockIcons)) bottom.AddChild(Glyph(unlock.Icon, 16, null));
        if (node.Unlocks.Count > MaxUnlockIcons) bottom.AddChild(Small("+" + (node.Unlocks.Count - MaxUnlockIcons).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        bottom.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        if (!researched && node.Turns is { } turns) bottom.AddChild(Small(CityProductionModel.TurnsText(turns).TrText()));
        column.AddChild(bottom);

        if (!researched && node.Progress > 0)
        {
            var bar = new StatBar(uiTheme, "Science", node.Progress, node.Cost) { MouseFilter = MouseFilterEnum.Ignore };
            IgnoreMouse(bar);
            column.AddChild(bar);
        }
        else column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, uiTheme.Px(6)), MouseFilter = MouseFilterEnum.Ignore });
    }

    public TechNode Node { get; }

    /// <summary>The tech's name label (ellipsized when it does not fit; the card's tooltip holds the full name).</summary>
    public Label NameLabel => nameLabel;

    /// <summary>The card's own content, laid out over the whole card: its minimum width must stay within the card's width.</summary>
    public Control Content => content;

    /// <summary>The theme variation the state picked.</summary>
    public string Variation => ThemeTypeVariation;

    /// <summary>How many unlock icons the card shows (at most <see cref="MaxUnlockIcons"/>).</summary>
    public int ShownUnlockIcons => Math.Min(Node.Unlocks.Count, MaxUnlockIcons);

    /// <summary>Dims the card (a search that does not match it).</summary>
    public bool Dimmed
    {
        get => Modulate.A < 1f;
        set
        {
            var colour = Modulate;
            colour.A = value ? 0.3f : 1f;
            Modulate = colour;
        }
    }

    /// <summary>A double click: research this tech now.</summary>
    public event Action? Activated;

    /// <summary>A Shift+click: add this tech to the research queue.</summary>
    public event Action? QueueRequested;

    /// <summary>T283: a drag that starts on the card moves the tree (the cards cover most of it, and a button swallows the drag the scroll container would otherwise take for a finger).</summary>
    public Action<Vector2>? Pan { get; set; }

    /// <summary>True from a drag of more than a few pixels until the next press: the release that ends it is a pan, not a selection.</summary>
    public bool Dragged { get; private set; }

    private Vector2 dragTravel;

    /// <summary>Forgets the drag once its release has been handled.</summary>
    public void ClearDrag()
    {
        Dragged = false;
        dragTravel = Vector2.Zero;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Dragged = false;
            dragTravel = Vector2.Zero;
        }
        if (@event is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } motion)
        {
            dragTravel += motion.Relative;
            if (dragTravel.Length() > theme.Px(6)) Dragged = true;
            if (Dragged) Pan?.Invoke(motion.Relative);
        }
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
        {
            if (click.ShiftPressed) QueueRequested?.Invoke();
            else if (click.DoubleClick) Activated?.Invoke();
        }
    }

    public override void _Draw()
    {
        // a researched tech has a green bar on its left edge
        if (researched) DrawRect(new Rect2(0f, 0f, theme.Px(UiTokens.S1) - 1, Size.Y), ProceduralAssets.ToColor(UiTokens.Success));
    }

    private static string Tooltip(TechNode node)
    {
        var lines = new List<string> { node.Name.TrText(), TechScreenModel.Detail(Entry(node)).TrText() };
        if (node.Unlocks.Count > 0) lines.Add(string.Join(", ", node.Unlocks.Select(it => it.Name.TrText())));
        return string.Join("\n", lines);
    }

    private static Unciv.Queries.Empire.TechTreeEntry Entry(TechNode node) =>
        new(node.Name, node.Era, node.GridColumn, node.GridRow, node.Cost, node.Progress, node.Prerequisites, node.State, node.QueuePosition, node.Turns, node.Description);

    private Label Small(string text) => new() { Text = text, ThemeTypeVariation = UiVariants.LabelSmall, MouseFilter = MouseFilterEnum.Ignore };

    private TextureRect Glyph(string uiName, int size, Rgb? tint) => Glyph(AssetKey.Of(RulesetAssetKeys.UiIcon, uiName), size, tint);

    private TextureRect Glyph(AssetKey key, int size) => new()
    {
        Texture = theme.Icon(key), CustomMinimumSize = new Vector2(theme.Px(size), theme.Px(size)), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.LinearWithMipmaps, MouseFilter = MouseFilterEnum.Ignore,
        SizeFlagsVertical = SizeFlags.ShrinkCenter,
    };

    private TextureRect Glyph(AssetKey key, int size, Rgb? tint)
    {
        var picture = Glyph(key, size);
        if (tint is { } rgb) picture.Modulate = ProceduralAssets.ToColor(rgb);
        return picture;
    }

    private static void IgnoreMouse(Control control)
    {
        control.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var child in control.GetChildren().OfType<Control>()) IgnoreMouse(child);
    }
}
