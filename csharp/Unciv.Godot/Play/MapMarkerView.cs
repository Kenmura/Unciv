using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Logic.Map;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C089: the editor of the player's private map markers. Opened on a tile it shows the icon choice and the label field of that tile's marker (Save, and Remove when
/// the tile has one); opened without a tile it is only the list. Below the form every marker of the game is listed: pressing one moves the camera there
/// (<see cref="GoTo"/>). It decides nothing about storage: the owner reacts to <see cref="Saved"/>, <see cref="Removed"/> and <see cref="GoTo"/>.
/// </summary>
public partial class MapMarkerView : Modal
{
    private readonly OptionButton icon = new() { Name = "MarkerIcon" };
    private readonly LineEdit label = new() { Name = "MarkerLabel", MaxLength = MapMarkerModel.MaxLabelLength, PlaceholderText = "Label (optional)".Tr() };
    private readonly Label tile = new() { Name = "MarkerTile", ThemeTypeVariation = UiVariants.LabelSecondary };
    private readonly VBoxContainer form = new() { Name = "MarkerForm" };
    private readonly VBoxContainer rows = new() { Name = "MarkerRows" };
    private readonly ScrollContainer scroll = new() { Name = "MarkerScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly Label empty = new() { Name = "MarkerEmpty", Text = "No markers yet".Tr(), ThemeTypeVariation = UiVariants.LabelSecondary };
    private readonly Button save = new() { Name = "MarkerSave", Text = "Save".Tr() };
    private readonly Button remove = new() { Name = "MarkerRemove", Text = "Remove".Tr() };
    private readonly Button close = new() { Name = "MarkerClose", Text = "Close".Tr() };
    private readonly Dictionary<HexCoord, Button> goButtons = [];
    private HexCoord? editing;

    public MapMarkerView(UiTheme theme)
    {
        Name = "MarkerPopup";
        Init(theme);
        SetHeader("Map markers");
        foreach (var name in MapMarkerModel.Icons) icon.AddItem(name.Tr());
        form.AddChild(tile);
        form.AddChild(icon);
        form.AddChild(label);
        Body.AddChild(form);
        scroll.AddChild(rows);
        rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Body.AddChild(empty);
        Body.AddChild(scroll);
        SetActions([(save, ChoiceRole.Primary), (remove, ChoiceRole.Danger), (close, ChoiceRole.Secondary)]);
        save.Pressed += Save;
        remove.Pressed += Remove;
        close.Pressed += () => Visible = false;
        label.TextSubmitted += _ => Save();
    }

    /// <summary>The tile's marker was confirmed: tile, icon name, label (not yet normalized).</summary>
    public event Action<HexCoord, string, string>? Saved;

    public event Action<HexCoord>? Removed;

    public event Action<HexCoord>? GoTo;

    public LineEdit Field => label;

    public OptionButton IconChoice => icon;

    public Button SaveButton => save;

    public Button RemoveButton => remove;

    public bool FormShown => form.Visible;

    public Button? GoButton(HexCoord coord) => goButtons.GetValueOrDefault(coord);

    public int ListedCount => goButtons.Count;

    /// <summary>The icon name picked now.</summary>
    public string SelectedIcon => MapMarkerModel.Icons[Math.Clamp(icon.Selected, 0, MapMarkerModel.Icons.Count - 1)];

    /// <summary>Shows the editor for <paramref name="target"/> (the form is hidden when null: the list only). <paramref name="existing"/> is the tile's marker when it has one.</summary>
    public void Open(HexCoord? target, MapMarker? existing, IReadOnlyList<MapMarker> all)
    {
        editing = target;
        form.Visible = target != null;
        save.Visible = target != null;
        remove.Visible = existing != null;
        if (target is { } at)
        {
            tile.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} ({1}, {2})", "Tile".Tr(), at.X, at.Y);
            icon.Selected = Math.Max(0, MapMarkerModel.Icons.ToList().IndexOf(existing?.Icon ?? MapMarkerModel.DefaultIcon));
            label.Text = existing?.Label ?? "";
        }
        Fit(Modal.MinWidth);
        SetList(all);
        Visible = true;
        if (target != null) label.GrabFocus();
    }

    /// <summary>Rebuilds the list (the owner calls it after a change while the editor stays open).</summary>
    public void SetList(IReadOnlyList<MapMarker> all)
    {
        foreach (var child in rows.GetChildren())
        {
            rows.RemoveChild(child);
            child.QueueFree();
        }
        goButtons.Clear();
        foreach (var marker in all)
        {
            var coord = new HexCoord(marker.X, marker.Y);
            string text = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} ({1}, {2}) {3}", marker.Icon.Tr(), marker.X, marker.Y, marker.Label).TrimEnd();
            var go = new Button { Name = $"Go_{marker.X}_{marker.Y}", Text = text, Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
            go.Pressed += () => GoTo?.Invoke(coord);
            rows.AddChild(go);
            goButtons[coord] = go;
        }
        empty.Visible = all.Count == 0;
        scroll.Visible = all.Count > 0;
        FitScroll(scroll, rows, 0.35f);
    }

    public void Close() => Visible = false;

    public void Save()
    {
        if (editing is not { } at) return;
        Visible = false;
        Saved?.Invoke(at, SelectedIcon, label.Text);
    }

    public void Remove()
    {
        if (editing is not { } at) return;
        Visible = false;
        Removed?.Invoke(at);
    }

    protected override bool OnCancel()
    {
        Visible = false;
        return true;
    }
}
