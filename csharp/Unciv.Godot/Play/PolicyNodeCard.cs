using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C071: one policy of a branch as a node (design/ui-redesign.md 3.4): the policy icon on a round badge, the name below it (two lines, ellipsized; the full name and why it can or cannot
/// be adopted are in the tooltip). The adoption state shows in the border and the badge: the node takes one of the <c>PolicyNode*</c> theme variations
/// (<see cref="UiVariants.PolicyNode"/>) and the pressed state is the selection.
/// </summary>
public partial class PolicyNodeCard : Button
{
    private readonly MarginContainer content = new() { Name = "Content", MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label nameLabel = new()
    {
        Name = "NodeName", ThemeTypeVariation = UiVariants.LabelSmall, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2,
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore,
    };

    public PolicyNodeCard(UiTheme theme, PolicyCell policy, int width, int height)
    {
        Cell = policy;
        Name = policy.Id;
        ThemeTypeVariation = UiVariants.PolicyNode(policy.State, policy.Pickable);
        ToggleMode = true;
        ActionMode = ActionModeEnum.Release;
        CustomMinimumSize = new Vector2(width, height);
        Size = new Vector2(width, height);
        TooltipText = policy.Name.TrText() + "\n" + policy.Hint.TrText();
        FocusMode = FocusModeEnum.All;

        int pad = theme.Px(UiTokens.S1);
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) content.AddThemeConstantOverride("margin_" + side, pad);
        AddChild(content);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", theme.Px(2));
        content.AddChild(column);
        var fill = policy.IsAdopted ? UiTokens.AccentLo : policy.State == "Locked" ? UiTokens.Surface3 : UiTokens.Info.Scale(60);
        var badge = new IconBadge(theme, policy.IconKey, ProceduralAssets.ToColor(fill), ProceduralAssets.ToColor(UiTokens.TextPrimary), 32) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        column.AddChild(badge);
        nameLabel.Text = policy.Name.TrText();
        if (policy.State == "Locked") nameLabel.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(UiTokens.TextMuted));
        column.AddChild(nameLabel);
    }

    public PolicyCell Cell { get; }

    /// <summary>The name label (ellipsized after two lines; the tooltip holds the full name).</summary>
    public Label NameLabel => nameLabel;

    /// <summary>The card's own content, laid out over the whole card: its minimum width must stay within the card's width.</summary>
    public Control Content => content;

    /// <summary>The theme variation the state picked.</summary>
    public string Variation => ThemeTypeVariation;

    /// <summary>Dims the node (a search that does not match it).</summary>
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
}
