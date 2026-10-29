using Godot;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// A row of underline tabs over a content area showing the selected tab's page (only that page is visible). C065: the tabs scroll sideways when they do not
/// fit, with an arrow at each end that appears only then, and the selected tab is scrolled into view.
/// </summary>
public partial class TabBar : VBoxContainer
{
    private readonly HBoxContainer strip = new();
    private readonly ScrollContainer scroller = new() { VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Button left = new() { Text = "◀", ThemeTypeVariation = UiVariants.ButtonGhost, Visible = false, FocusMode = FocusModeEnum.None };
    private readonly Button right = new() { Text = "▶", ThemeTypeVariation = UiVariants.ButtonGhost, Visible = false, FocusMode = FocusModeEnum.None };
    private readonly PanelContainer content = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly ButtonGroup group = new();
    private readonly List<(Button Tab, Control Page)> tabs = [];

    public TabBar()
    {
        strip.AddThemeConstantOverride("separation", 0);
        scroller.AddChild(strip);
        var header = new HBoxContainer();
        header.AddChild(left);
        header.AddChild(scroller);
        header.AddChild(right);
        AddChild(header);
        AddChild(content);
        left.Pressed += () => Scroll(-1);
        right.Pressed += () => Scroll(1);
        scroller.Resized += UpdateArrows;
        strip.Resized += UpdateArrows;
    }

    public int SelectedIndex { get; private set; } = -1;

    public int Count => tabs.Count;

    public Control? SelectedPage => SelectedIndex >= 0 ? tabs[SelectedIndex].Page : null;

    /// <summary>True while the tabs are wider than the bar and the scroll arrows are shown.</summary>
    public bool Overflowing => left.Visible;

    public event Action<int>? TabSelected;

    public void AddTab(string title, Control page)
    {
        int index = tabs.Count;
        var button = new Button { Name = "Tab_" + title, Text = title.TrText(), ToggleMode = true, ButtonGroup = group, ThemeTypeVariation = UiVariants.ButtonTab };
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
        scroller.EnsureControlVisible(tabs[index].Tab);
        TabSelected?.Invoke(index);
    }

    private void Scroll(int direction) => scroller.ScrollHorizontal = Math.Max(0, scroller.ScrollHorizontal + direction * (int)(scroller.Size.X * 0.6f));

    private void UpdateArrows()
    {
        bool overflow = strip.GetCombinedMinimumSize().X > scroller.Size.X + (left.Visible ? left.Size.X + right.Size.X : 0f) + 0.5f;
        left.Visible = right.Visible = overflow;
    }
}
