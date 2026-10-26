using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C047: the menu of the AutoPlay button, a small popup above it: one button per <see cref="AutoPlayEntry"/> with how long it plays beside the label. The view decides
/// nothing; the owner reacts to <see cref="Chosen"/> (starts the run). A refused entry (not your turn) is shown greyed with its reason as tooltip.
/// </summary>
public partial class AutoPlayMenuView : PanelContainer
{
    private readonly VBoxContainer column = new() { Name = "AutoPlayColumn" };
    private readonly Dictionary<string, Button> byId = [];

    public AutoPlayMenuView()
    {
        Name = "AutoPlayMenu";
        Visible = false;
        AddChild(column);
    }

    public IReadOnlyList<AutoPlayEntry> Entries { get; private set; } = [];

    public Button? ButtonById(string id) => byId.GetValueOrDefault(id);

    public event Action<AutoPlayKind>? Chosen;

    private UiTheme? theme;

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
    }

    public void Open(IReadOnlyList<AutoPlayEntry> entries)
    {
        Entries = entries;
        Clear();
        column.AddChild(new Label { Name = "AutoPlayTitle", Text = AutoPlayModel.MenuTitle.TrText(), HorizontalAlignment = HorizontalAlignment.Center });
        foreach (var entry in entries)
        {
            var row = new HBoxContainer { Name = entry.Id + "Row" };
            var button = new Button { Name = entry.Id, Text = entry.Label.TrText(), Disabled = !entry.Enabled, CustomMinimumSize = new Vector2(theme?.Px(170) ?? 170f, 0f) };
            if (entry.DisabledReason != null) button.TooltipText = entry.DisabledReason.TrText();
            row.AddChild(button);
            row.AddChild(new Label { Name = entry.Id + "Detail", Text = entry.Detail.TrText() });
            var captured = entry;
            button.Pressed += () =>
            {
                if (captured.Enabled) Chosen?.Invoke(captured.Kind);
            };
            column.AddChild(row);
            byId[entry.Id] = button;
        }
        Visible = true;
        Size = GetCombinedMinimumSize();
    }

    public void Close()
    {
        Entries = [];
        Visible = false;
        Clear();
    }

    private void Clear()
    {
        foreach (var child in column.GetChildren())
        {
            column.RemoveChild(child);
            child.QueueFree();
        }
        byId.Clear();
    }
}
