using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// C065: "[-] value [+]": a number between <see cref="Min"/> and <see cref="Max"/> changed in <see cref="Step"/>s. The value is clamped to the range
/// whoever sets it, and the button that would leave the range is disabled. Replaces the Fewer/More button pairs.
/// </summary>
public partial class Stepper : HBoxContainer
{
    private readonly Label shown = new() { ThemeTypeVariation = UiVariants.LabelNumber, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Func<int, string> format;
    private int value;

    public Stepper(UiTheme theme, int min, int max, int value, int step = 1, string decreaseLabel = "Decrease", string increaseLabel = "Increase", Func<int, string>? format = null)
    {
        if (min > max) throw new ArgumentException("min is above max");
        if (step < 1) throw new ArgumentException("step must be at least 1");
        Min = min;
        Max = max;
        Step = step;
        this.format = format ?? (v => v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Minus = new IconButton(theme, AssetKey.Of(RulesetAssetKeys.UiIcon, "Reduce"), decreaseLabel);
        Plus = new IconButton(theme, AssetKey.Of(RulesetAssetKeys.UiIcon, "Increase"), increaseLabel);
        Minus.Pressed += () => Value -= Step;
        Plus.Pressed += () => Value += Step;
        shown.CustomMinimumSize = new Vector2(theme.Px(UiTokens.S8 + UiTokens.S4), 0);
        AddChild(Minus);
        AddChild(shown);
        AddChild(Plus);
        Apply(Math.Clamp(value, min, max));
    }

    public int Min { get; }

    public int Max { get; }

    public int Step { get; }

    public IconButton Minus { get; }

    public IconButton Plus { get; }

    /// <summary>The text of the value as shown (after the formatter).</summary>
    public string ShownText => shown.Text;

    /// <summary>The current value; assigning outside the range clamps it. <see cref="Changed"/> is raised only when the value actually changed.</summary>
    public int Value
    {
        get => value;
        set
        {
            int clamped = Math.Clamp(value, Min, Max);
            if (clamped == this.value) return;
            Apply(clamped);
            Changed?.Invoke(clamped);
        }
    }

    public event Action<int>? Changed;

    private void Apply(int v)
    {
        value = v;
        shown.Text = format(v);
        Minus.Disabled = v <= Min;
        Plus.Disabled = v >= Max;
    }
}
