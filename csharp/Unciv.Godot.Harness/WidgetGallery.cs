using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>T323: one of each shared widget, built once; <see cref="Wrong"/> lists those whose size is not what the live theme gives.</summary>
internal sealed partial class WidgetGallery : VBoxContainer
{
    private readonly ListRow row;
    private readonly ListPanel panel;
    private readonly IconLabel label;
    private readonly StatChip chip;
    private readonly StatBar bar;
    private readonly TooltipHost tip;

    public WidgetGallery(UiTheme theme)
    {
        Name = "WidgetGallery";
        var gold = AssetKey.Of(RulesetAssetKeys.StatIcon, "Gold");
        AddChild(row = new ListRow(theme, "Warrior", "Ready", "40", AssetKey.Of(RulesetAssetKeys.UnitIcon, "Warrior")));
        AddChild(panel = new ListPanel(theme, "Units", new Vector2(220, 120)));
        panel.AddItem("Settler", AssetKey.Of(RulesetAssetKeys.UnitIcon, "Settler"));
        AddChild(label = new IconLabel(theme, gold, "+12"));
        AddChild(chip = new StatChip(theme, "Gold", "46", 5));
        AddChild(bar = new StatBar(theme, "Food", 3, 10, labelled: true, caption: "3 turns"));
        AddChild(tip = new TooltipHost(theme) { Visible = false });
    }

    public List<string> Wrong(UiTheme t)
    {
        var wrong = new List<string>();
        void Expect(string what, float actual, float expected)
        {
            if (Math.Abs(actual - expected) > 0.5f) wrong.Add($"{what} is {actual}, the live scale gives {expected}");
        }
        Expect("ListRow height", row.CustomMinimumSize.Y, Math.Max(row.CustomMinimumSize.Y, Math.Max(t.Px(UiTokens.RowHeight), t.MinTargetPx)));
        Expect("ListRow icon", FirstIcon(row), t.Px(UiTheme.IconSize));
        Expect("ListPanel width", panel.CustomMinimumSize.X, t.Px(220));
        Expect("ListPanel height", panel.CustomMinimumSize.Y, t.Px(120));
        Expect("ListPanel item icon", panel.ItemAt(0).NotNull().GetThemeConstant("icon_max_width"), t.Px(UiTheme.IconSize));
        Expect("IconLabel icon", FirstIcon(label), t.Px(UiTheme.IconSize));
        Expect("StatChip separation", chip.GetThemeConstant("separation"), t.Px(UiTokens.S1));
        Expect("StatBar height", bar.Bar.CustomMinimumSize.Y, t.Px(UiTokens.BarLabelled));
        Expect("TooltipHost text width", tip.TextMinWidth, t.Px(120));
        return wrong;
    }

    private static float FirstIcon(Node owner) => owner.FindChildren("*", "", true, false).OfType<TextureRect>().FirstOrDefault()?.CustomMinimumSize.X ?? -1f;
}
