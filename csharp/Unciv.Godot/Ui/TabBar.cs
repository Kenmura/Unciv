using Godot;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>A row of tab buttons over a content area showing the selected tab's page (only that page is visible).</summary>
public partial class TabBar : VBoxContainer
{
    private readonly HBoxContainer strip = new();
    private readonly PanelContainer content = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly ButtonGroup group = new();
    private readonly List<(Button Tab, Control Page)> tabs = [];

    public TabBar()
    {
        AddChild(strip);
        AddChild(content);
    }

    public int SelectedIndex { get; private set; } = -1;

    public int Count => tabs.Count;

    public Control? SelectedPage => SelectedIndex >= 0 ? tabs[SelectedIndex].Page : null;

    public event Action<int>? TabSelected;

    public void AddTab(string title, Control page)
    {
        int index = tabs.Count;
        var button = new Button { Text = title.TrText(), ToggleMode = true, ButtonGroup = group };
        button.Pressed += () => Select(index);
        strip.AddChild(button);
        page.Visible = false;
        content.AddChild(page);
        tabs.Add((button, page));
        if (SelectedIndex < 0) Select(index);
    }

    public void Select(int index)
    {
        if (index < 0 || index >= tabs.Count || index == SelectedIndex && tabs[index].Tab.ButtonPressed) return;
        SelectedIndex = index;
        for (int i = 0; i < tabs.Count; i++) tabs[i].Page.Visible = i == index;
        tabs[index].Tab.ButtonPressed = true;
        TabSelected?.Invoke(index);
    }
}
