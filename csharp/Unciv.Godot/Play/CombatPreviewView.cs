using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C012/C068: the battle card: a VS layout with the two fighters as columns (name, civ, strength, predicted health bar and modifiers), the outcome under them, an Attack
/// button (primary) that raises <see cref="ConfirmRequested"/> and a Cancel button (ghost). It sits above the unit panel, centred at the bottom of the screen.
/// <see cref="BodyText"/> is the same table as plain text lines.
/// </summary>
public partial class CombatPreviewView : PanelContainer
{
    private const int Width = 380;

    private readonly VBoxContainer column = new() { Name = "Column" };
    private readonly Label title = new() { Name = "Title", ThemeTypeVariation = UiVariants.LabelHeading, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly HBoxContainer fighters = new() { Name = "Fighters" };
    private readonly VBoxContainer outcome = new() { Name = "Outcome" };
    private readonly Button confirm = new() { Name = "Confirm", ThemeTypeVariation = UiVariants.ButtonPrimary };
    private readonly Button cancel = new() { Name = "Cancel", Text = "Cancel".Tr(), ThemeTypeVariation = UiVariants.ButtonGhost };
    private UiTheme? theme;

    public CombatPreviewView()
    {
        Name = "CombatPreview";
        Visible = false;
        ThemeTypeVariation = UiVariants.PanelCard;
        MouseFilter = MouseFilterEnum.Stop;
        column.AddThemeConstantOverride("separation", UiTokens.S2);
        fighters.AddThemeConstantOverride("separation", UiTokens.S3);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", UiTokens.S2);
        row.AddChild(confirm);
        row.AddChild(cancel);
        column.AddChild(title);
        column.AddChild(fighters);
        column.AddChild(outcome);
        column.AddChild(row);
        AddChild(column);
        confirm.Pressed += () => ConfirmRequested?.Invoke();
        cancel.Pressed += () => CancelRequested?.Invoke();
    }

    public string TitleText => title.Text;

    public string BodyText { get; private set; } = "";

    public string ConfirmText => confirm.Text;

    public bool ConfirmEnabled => !confirm.Disabled;

    /// <summary>How many fighter columns the card shows (0 to 2).</summary>
    public int FighterCount => fighters.GetChildren().OfType<VBoxContainer>().Count();

    public event Action? ConfirmRequested;

    public event Action? CancelRequested;

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
    }

    public void Show(CombatPreviewText text)
    {
        title.Text = text.Title.TrText();
        BodyText = string.Join("\n", text.Lines.Select(it => it.TrText()));
        confirm.Text = text.ConfirmLabel.TrText();
        confirm.Disabled = !text.CanConfirm;
        Fill(fighters);
        Fill(outcome);
        if (text.Card is { } card)
        {
            if (card.Attacker != null) fighters.AddChild(Column(card.Attacker, true));
            if (card.Attacker != null && card.Defender != null)
                fighters.AddChild(new Label { Text = "VS".Tr(), ThemeTypeVariation = UiVariants.LabelOverline, VerticalAlignment = VerticalAlignment.Center });
            if (card.Defender != null) fighters.AddChild(Column(card.Defender, false));
            foreach (var line in card.Outcome)
                outcome.AddChild(new Label { Text = line.TrText(), ThemeTypeVariation = UiVariants.LabelBodyStrong, HorizontalAlignment = HorizontalAlignment.Center });
        }
        Visible = true;
        LayoutIn(GetViewportRect().Size);
    }

    public void Clear() => Visible = false;

    public void LayoutIn(Vector2 screen)
    {
        float width = theme?.Px(Width) ?? Width;
        CustomMinimumSize = new Vector2(width, 0f);
        Size = Vector2.Zero;
        var size = GetCombinedMinimumSize();
        Position = new Vector2(Math.Max((screen.X - size.X) / 2f, (theme?.Px(260) ?? 260f)), screen.Y - 12f - size.Y); // right of the unit panel
    }

    private VBoxContainer Column(FighterCard fighter, bool attacker)
    {
        var box = new VBoxContainer { Name = attacker ? "Attacker" : "Defender", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", UiTokens.S1);
        var align = attacker ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        box.AddChild(new Label { Text = fighter.Name.TrText(), ThemeTypeVariation = UiVariants.LabelBodyStrong, HorizontalAlignment = align, ClipText = true });
        box.AddChild(new Label { Text = fighter.Civ.TrText(), ThemeTypeVariation = UiVariants.LabelSmall, HorizontalAlignment = align, ClipText = true });
        box.AddChild(new Label { Text = Int(fighter.Strength), ThemeTypeVariation = UiVariants.LabelDisplay, HorizontalAlignment = align });
        if (theme != null)
        {
            string range = fighter.HealthMin == fighter.HealthMax ? Int(fighter.HealthMax) : $"{Int(fighter.HealthMin)}-{Int(fighter.HealthMax)}";
            var bar = new StatBar(theme, "Food", fighter.HealthMax, Math.Max(1, fighter.MaxHealth), caption: $"{Int(fighter.Health)} > {range}");
            bar.TooltipText = $"{"Health".TrText()} {Int(fighter.Health)}/{Int(fighter.MaxHealth)}";
            box.AddChild(bar);
        }
        foreach (var modifier in fighter.Modifiers)
            box.AddChild(new Label { Text = modifier.TrText(), ThemeTypeVariation = UiVariants.LabelSmall, HorizontalAlignment = align, ClipText = true });
        return box;
    }

    private static void Fill(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
