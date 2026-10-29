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

    /// <summary>C090: ping the tile for the allies (look here, danger, help).</summary>
    PingLook,
    PingDanger,
    PingHelp,
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
    private readonly Button pingLook = new() { Name = "MarkerMenuPingLook", Text = "Ping allies: Look here".Tr() };
    private readonly Button pingDanger = new() { Name = "MarkerMenuPingDanger", Text = "Ping allies: Danger".Tr() };
    private readonly Button pingHelp = new() { Name = "MarkerMenuPingHelp", Text = "Ping allies: Help".Tr() };

    public MapMarkerMenuView()
    {
        Name = "MarkerMenu";
        Visible = false;
        AddChild(column);
        foreach (var button in new[] { edit, remove, list, pingLook, pingDanger, pingHelp })
        {
            button.Alignment = HorizontalAlignment.Left;
            column.AddChild(button);
        }
        edit.Pressed += () => Chosen?.Invoke(MarkerMenuChoice.Edit);
        remove.Pressed += () => Chosen?.Invoke(MarkerMenuChoice.Remove);
        list.Pressed += () => Chosen?.Invoke(MarkerMenuChoice.List);
        pingLook.Pressed += () => Chosen?.Invoke(MarkerMenuChoice.PingLook);
        pingDanger.Pressed += () => Chosen?.Invoke(MarkerMenuChoice.PingDanger);
        pingHelp.Pressed += () => Chosen?.Invoke(MarkerMenuChoice.PingHelp);
    }

    public event Action<MarkerMenuChoice>? Chosen;

    public Button EditButton => edit;

    public Button RemoveButton => remove;

    public Button ListButton => list;

    public Button PingLookButton => pingLook;

    public Button PingDangerButton => pingDanger;

    public Button PingHelpButton => pingHelp;

    public void Init(UiTheme uiTheme) => Theme = uiTheme.Theme;

    /// <summary>Opens the menu with its top-left corner at <paramref name="at"/> (kept inside <paramref name="screen"/>). The first entry says Place or Edit; Remove only shows when the tile has a marker; the three ping entries show only when <paramref name="canPing"/> (a hosted game).</summary>
    public void Open(Vector2 at, Vector2 screen, bool hasMarker, bool canPing = false)
    {
        edit.Text = (hasMarker ? "Edit marker" : "Place marker").Tr();
        remove.Visible = hasMarker;
        foreach (var button in new[] { pingLook, pingDanger, pingHelp }) button.Visible = canPing; // C090: only in a hosted game (the session has allies to tell)
        Visible = true;
        Size = GetCombinedMinimumSize();
        Position = new Vector2(Math.Clamp(at.X, 0f, Math.Max(0f, screen.X - Size.X)), Math.Clamp(at.Y, 0f, Math.Max(0f, screen.Y - Size.Y)));
    }

    public void Close() => Visible = false;
}
