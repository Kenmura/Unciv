using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// A titled, scrolling list. <see cref="AddItem"/> adds a selectable row (optional icon); <see cref="AddRow"/> adds any control
/// (for example an <see cref="IconLabel"/>). The vertical scroll bar appears when the rows do not fit the panel's height.
/// </summary>
public partial class ListPanel : PanelContainer
{
    private readonly UiTheme theme;
    private readonly VBoxContainer rows = new();
    private readonly ButtonGroup group = new();
    private readonly List<Button> items = [];

    public ListPanel(UiTheme theme, string title, Vector2 size)
    {
        this.theme = theme;
        CustomMinimumSize = size;
        var column = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(column);
        TitleLabel = new Label { Text = title.TrText() };
        column.AddChild(TitleLabel);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(scroll);
        rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(rows);
        Scroll = scroll;
    }

    public Label TitleLabel { get; }

    public ScrollContainer Scroll { get; }

    public int Count => rows.GetChildCount();

    /// <summary>Index among the selectable items (not among all rows), or -1.</summary>
    public int SelectedIndex => items.FindIndex(it => it.ButtonPressed);

    public event Action<int>? Selected;

    /// <summary>The button of the <paramref name="index"/>th selectable item (rows added with <see cref="AddRow"/> do not count), or null.</summary>
    public Button? ItemAt(int index) => index >= 0 && index < items.Count ? items[index] : null;

    public void AddRow(Control row) => rows.AddChild(row);

    public void AddItem(string text, AssetKey? icon = null)
    {
        int index = items.Count;
        var button = new Button
        {
            Text = text.TrText(),
            ToggleMode = true,
            ButtonGroup = group,
            Alignment = HorizontalAlignment.Left,
            ClipText = true,
        };
        if (icon is { } key)
        {
            button.Icon = theme.Icon(key);
            button.ExpandIcon = true;
            button.AddThemeConstantOverride("icon_max_width", theme.Px(UiTheme.IconSize));
        }
        button.Pressed += () => Selected?.Invoke(index);
        items.Add(button);
        rows.AddChild(button);
    }

    public void Clear()
    {
        foreach (var child in rows.GetChildren()) child.QueueFree();
        items.Clear();
    }
}
