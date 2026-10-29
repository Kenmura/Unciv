using Godot;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Play;

/// <summary>What the marker context menu offers on a tile.</summary>
public enum MarkerMenuChoice
{
    Edit,
    Remove,
    List,
}

/// <summary>
/// C089: the small context menu a right-click on a tile (with no unit selected) opens at the pointer: place or edit that tile's marker, remove it, or open the marker list.
/// Decides nothing; the owner reacts to <see cref="Chosen"/> and closes it on any other click or Escape.
/// </summary>
public partial class MapMarkerMenuView : PanelContainer
{
    private readonly VBoxContainer column = new() { Name = "MarkerMenuColumn" };
    private readonly Button edit = new() { Name = "MarkerMenuEdit" };
    private readonly Button remove = new() { Name = "MarkerMenuRemove", Text = "Remove marker".Tr() };
    private readonly Button list = new() { Name = "MarkerMenuList", Text = "Map markers".Tr() };

    public MapMarkerMenuView()
    {
        Name = "MarkerMenu";
        Visible = false;
        AddChild(column);
        foreach (var button in new[] { edit, remove, list })
        {
            button.Alignment = HorizontalAlignment.Left;
            column.AddChild(button);
        }
        edit.Pressed += () => Chosen?.Invoke(MarkerMenuChoice.Edit);
        remove.Pressed += () => Chosen?.Invoke(MarkerMenuChoice.Remove);
        list.Pressed += () => Chosen?.Invoke(MarkerMenuChoice.List);
    }

    public event Action<MarkerMenuChoice>? Chosen;

    public Button EditButton => edit;

    public Button RemoveButton => remove;

    public Button ListButton => list;

    public void Init(UiTheme uiTheme) => Theme = uiTheme.Theme;

    /// <summary>Opens the menu with its top-left corner at <paramref name="at"/> (kept inside <paramref name="screen"/>). The first entry says Place or Edit; Remove only shows when the tile has a marker.</summary>
    public void Open(Vector2 at, Vector2 screen, bool hasMarker)
    {
        edit.Text = (hasMarker ? "Edit marker" : "Place marker").Tr();
        remove.Visible = hasMarker;
        Visible = true;
        Size = GetCombinedMinimumSize();
        Position = new Vector2(Math.Clamp(at.X, 0f, Math.Max(0f, screen.X - Size.X)), Math.Clamp(at.Y, 0f, Math.Max(0f, screen.Y - Size.Y)));
    }

    public void Close() => Visible = false;
}
