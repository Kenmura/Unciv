using Godot;

namespace Unciv.GodotClient.Ui;

/// <summary>C065: a small chip with a key name ("T"), shown in tooltips and next to menu items.</summary>
public partial class KeyHint : PanelContainer
{
    private readonly Label label = new() { ThemeTypeVariation = UiVariants.LabelSmall };

    public KeyHint(string key)
    {
        ThemeTypeVariation = UiVariants.PanelKeyHint;
        MouseFilter = MouseFilterEnum.Ignore;
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.Text = key;
        AddChild(label);
    }

    public string Key
    {
        get => label.Text;
        set => label.Text = value;
    }
}
