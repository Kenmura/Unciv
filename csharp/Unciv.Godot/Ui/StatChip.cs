using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// C065: a stat's icon badge (a circle in the stat colour), its value in tabular figures and an optional signed delta coloured by sign ("46 +5").
/// Used in the top bar, cards and costs.
/// </summary>
public partial class StatChip : HBoxContainer
{
    private readonly Label valueLabel = new() { ThemeTypeVariation = UiVariants.LabelNumber, VerticalAlignment = VerticalAlignment.Center };
    private readonly Label deltaLabel = new() { ThemeTypeVariation = UiVariants.LabelNumber, VerticalAlignment = VerticalAlignment.Center, Visible = false };

    public StatChip(UiTheme theme, string stat, string text, int? deltaValue = null)
    {
        Stat = stat;
        this.FitConstant(theme, "separation", UiTokens.S1); // T323
        AddChild(new IconBadge(theme, AssetKey.Of(RulesetAssetKeys.StatIcon, stat), ProceduralAssets.ToColor(PlaceholderPalette.ForStat(stat)), ProceduralAssets.ToColor(UiTokens.TextPrimary), 20));
        AddChild(valueLabel);
        AddChild(deltaLabel);
        Value = text;
        Delta = deltaValue;
    }

    public string Stat { get; }

    public string Value
    {
        get => valueLabel.Text;
        set => valueLabel.Text = value;
    }

    /// <summary>The shown delta (null hides it); its text is "+n" or "-n" and its colour follows the sign.</summary>
    public int? Delta
    {
        get => deltaValue;
        set
        {
            deltaValue = value;
            deltaLabel.Visible = value != null;
            if (value is not { } v) return;
            deltaLabel.Text = UiText.DeltaText(v);
            deltaLabel.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(UiText.DeltaColour(v)));
        }
    }

    private int? deltaValue;

    public string DeltaText => deltaLabel.Visible ? deltaLabel.Text : "";

    public Color DeltaColour => deltaLabel.GetThemeColor("font_color");
}
