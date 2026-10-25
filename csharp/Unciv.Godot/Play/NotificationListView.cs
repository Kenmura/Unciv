using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C013: the clickable notification list on the left of the world screen: a toggle that shows the count and, when open, the viewer's notifications newest first
/// (<see cref="NotificationsModel.Rows"/>). Pressing a row raises <see cref="Chosen"/>; the owner jumps the camera or opens the screen. The view shows what it is given.
/// </summary>
public partial class NotificationListView : VBoxContainer
{
    private readonly Button toggle = new() { Name = "NotificationsToggle" };
    private readonly Button review = new() { Name = "ReviewAlerts", Visible = false };
    private readonly PanelContainer list = new() { Name = "NotificationList", Visible = false };
    private readonly VBoxContainer inner = new();
    private readonly ScrollContainer scroll = new() { Name = "NotificationScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly VBoxContainer items = new() { Name = "NotificationRows" };
    private readonly Label summary = new() { Name = "NotificationSummary", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Dictionary<int, Button> buttons = [];
    private UiTheme theme = null!; // JUSTIFY: C013 assigned by Init before the list is updated
    private IReadOnlyList<NotificationRow> rows = [];
    private int turn;
    private int deferred;

    public NotificationListView()
    {
        Name = "Notifications";
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(review);
        AddChild(toggle);
        AddChild(list);
        review.Pressed += () => ReviewRequested?.Invoke();
        list.AddChild(inner);
        inner.AddChild(summary);
        inner.AddChild(scroll);
        scroll.AddChild(items);
        toggle.Pressed += () => IsOpen = !IsOpen;
    }

    public IReadOnlyList<NotificationRow> Rows => rows;

    public bool IsOpen
    {
        get => list.Visible;
        set
        {
            list.Visible = value;
            toggle.Text = NotificationsModel.ToggleLabel(rows.Count, value);
        }
    }

    public string SummaryText => summary.Text;

    public string ToggleText => toggle.Text;

    public Button? ToggleButton => toggle;

    /// <summary>The button of the row at this snapshot position (the <see cref="NotificationRow.Index"/>), if it is shown.</summary>
    public Button? RowButton(int index) => buttons.GetValueOrDefault(index);

    /// <summary>Scrolls the list so the row at this snapshot position is in view.</summary>
    public void EnsureVisible(int index)
    {
        if (buttons.TryGetValue(index, out var button)) scroll.EnsureControlVisible(button);
    }

    public event Action<NotificationRow>? Chosen;

    /// <summary>The player asked to see the alerts they put off again.</summary>
    public event Action? ReviewRequested;

    public Button ReviewButton => review;

    /// <summary>How many pending alerts wait to be reviewed (informational ones and ones put off with "Decide later"): the button that opens them shows while there are any.</summary>
    public void SetWaiting(int count)
    {
        deferred = count;
        review.Visible = count > 0;
        review.Text = AlertPopupModel.ReviewLabel(count);
        Visible = rows.Count > 0 || deferred > 0;
    }

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
        toggle.Text = NotificationsModel.ToggleLabel(0, false);
    }

    public void Update(int currentTurn, IReadOnlyList<NotificationRow> newRows)
    {
        turn = currentTurn;
        rows = newRows;
        foreach (var child in items.GetChildren())
        {
            items.RemoveChild(child);
            child.QueueFree();
        }
        buttons.Clear();
        summary.Text = NotificationsModel.Summary(turn, rows);
        summary.CustomMinimumSize = new Vector2(theme.Px(320), theme.Px(56)); // room for three wrapped lines: an autowrapped label does not reserve them itself
        summary.TooltipText = summary.Text;
        scroll.CustomMinimumSize = new Vector2(theme.Px(336), theme.Px(Math.Min(rows.Count, 5) * 32));
        foreach (var row in rows)
        {
            var button = new Button
            {
                Name = "Notification_" + row.Index,
                Text = row.Text,
                Alignment = HorizontalAlignment.Left,
                ClipText = true,
                CustomMinimumSize = new Vector2(theme.Px(320), theme.Px(28)),
                TooltipText = row.Category + (row.Target == null ? "" : row.Target.Kind == NotificationTargetKind.Tile ? ": click to show on the map" : ": click to open"),
            };
            button.AddThemeFontSizeOverride("font_size", theme.Px(14));
            var chosen = row;
            button.Pressed += () => Chosen?.Invoke(chosen);
            items.AddChild(button);
            buttons[row.Index] = button;
        }
        toggle.Text = NotificationsModel.ToggleLabel(rows.Count, IsOpen);
        Visible = rows.Count > 0 || deferred > 0; // nothing to list: no button either
    }
}
