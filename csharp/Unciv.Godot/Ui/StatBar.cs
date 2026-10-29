using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// C065: a progress bar on an Inset track filled in a stat colour, thin (6 px) or labelled (14 px), with optional text to its right ("3 turns").
/// Growth, borders, production, research and golden age use it. The fill is a theme override built in <c>ThemeBuilder</c>, so no stylebox is made here.
/// </summary>
public partial class StatBar : HBoxContainer
{
    private readonly UiTheme theme;
    private readonly ProgressBar bar = new() { ShowPercentage = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
    private readonly Label text = new() { ThemeTypeVariation = UiVariants.LabelSmall, Visible = false };
    private readonly Rgb colour;
    private readonly bool labelled;

    public StatBar(UiTheme theme, string stat, double value, double max, bool labelled = false, string? caption = null)
    {
        this.theme = theme;
        this.labelled = labelled;
        colour = PlaceholderPalette.ForStat(stat);
        Stat = stat;
        bar.ThemeTypeVariation = labelled ? UiVariants.StatBarLabelled : UiVariants.StatBarThin;
        AddChild(bar);
        AddChild(text);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Caption = caption;
        SetProgress(value, max);
        Restyle();
        theme.ScaleChanged += Restyle;
    }

    public string Stat { get; }

    /// <summary>The filled share, 0 to 1 (0 when the maximum is not positive).</summary>
    public double Fraction => bar.MaxValue > 0 ? Math.Clamp(bar.Value / bar.MaxValue, 0, 1) : 0;

    public string? Caption
    {
        get => text.Visible ? text.Text : null;
        set
        {
            text.Visible = !string.IsNullOrEmpty(value);
            text.Text = value ?? "";
        }
    }

    public ProgressBar Bar => bar;

    public void SetProgress(double value, double max)
    {
        bar.MaxValue = Math.Max(max, 0);
        bar.Value = Math.Clamp(value, 0, bar.MaxValue);
    }

    public override void _ExitTree() => theme.ScaleChanged -= Restyle;

    private void Restyle()
    {
        int height = theme.Px(labelled ? UiTokens.BarLabelled : UiTokens.BarThin);
        bar.CustomMinimumSize = new Vector2(theme.Px(UiTokens.S8), height);
        bar.AddThemeStyleboxOverride("fill", ThemeBuilder.BarFill(colour, labelled, theme.Scale));
    }
}
