using Godot;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>C065: an overline title (upper case) followed by a hairline rule that fills the rest of the width.</summary>
public partial class SectionHeader : HBoxContainer
{
    private readonly Label label = new() { ThemeTypeVariation = UiVariants.LabelOverline, VerticalAlignment = VerticalAlignment.Center };

    public SectionHeader(string title)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Title = title;
        label.Text = title.TrText().TrimStart('¤', '~').ToUpperInvariant(); // C073: a stat name translates with its icon marker ("¤Gold"), which the overline cannot draw
        AddChild(label);
        AddChild(new HSeparator { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter });
    }

    /// <summary>The untranslated title as given.</summary>
    public string Title { get; }

    /// <summary>The text shown: the translated title in upper case (invariant culture).</summary>
    public string ShownText => label.Text;
}
