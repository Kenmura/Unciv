using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C013 / C067: the notification column on the right of the world screen (<see cref="WorldHud"/> places it): a toggle that shows the count and, when open, a drop-down list
/// of the viewer's notifications newest first (<see cref="NotificationsModel.Rows"/>) that floats over the map without moving the rest of the HUD. Pressing a row raises
/// <see cref="Chosen"/>; the owner jumps the camera or opens the screen. Ambient alerts (<see cref="AlertKind.Ambient"/>) arrive as short-lived toasts in the column
/// (<see cref="PushToast"/>); pressing one asks for the alert review. The view shows what it is given.
/// </summary>
public partial class NotificationListView : VBoxContainer
{
    private readonly Button toggle = new() { Name = "NotificationsToggle" };
    private readonly Label offers = new() { Name = "OfferStatus", Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button review = new() { Name = "ReviewAlerts", Visible = false, ThemeTypeVariation = UiVariants.ButtonDanger };
    private readonly Control dropHost = new() { Name = "NotificationDrop", MouseFilter = MouseFilterEnum.Ignore };
    private readonly VBoxContainer extras = new() { Name = "NotificationExtras", MouseFilter = MouseFilterEnum.Ignore }; // offers and toasts: they give way to the open list
    private readonly VBoxContainer toasts = new() { Name = "NotificationToasts", MouseFilter = MouseFilterEnum.Ignore };
    private readonly List<(Button Button, int Left)> toastList = [];
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
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(review);
        AddChild(toggle);
        AddChild(dropHost);
        AddChild(extras);
        extras.AddChild(offers);
        extras.AddChild(toasts);
        dropHost.AddChild(list);
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
            extras.Visible = !value;
            list.ResetSize(); // the drop-down is not in a container: refit it to its content (a hidden control keeps the size it had)
            toggle.Text = NotificationsModel.ToggleLabel(rows.Count, value).TrText();
        }
    }

    public string SummaryText => summary.Text;

    public string ToggleText => toggle.Text;

    public Button? ToggleButton => toggle;

    /// <summary>The toasts showing now (their texts, newest last).</summary>
    public IReadOnlyList<string> ToastTexts => [.. toastList.Select(it => it.Button.Text)];

    private const int ToastFrames = 150; // frames, not seconds: captures must not depend on machine speed
    private const int MaxToasts = 3;

    /// <summary>Shows a non-blocking toast in the column for a few seconds; pressing it asks for the alert review. Only the newest <see cref="MaxToasts"/> stay.</summary>
    public void PushToast(string text)
    {
        var button = new Button
        {
            Name = "Toast_" + toastList.Count,
            Text = text.TrText(),
            ThemeTypeVariation = UiVariants.ButtonSecondary,
            Alignment = HorizontalAlignment.Left,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            CustomMinimumSize = new Vector2(RowWidth, theme.Px(28)),
            TooltipText = text.TrText(),
        };
        button.Pressed += () => ReviewRequested?.Invoke();
        toasts.AddChild(button);
        toastList.Add((button, ToastFrames));
        while (toastList.Count > MaxToasts) DropToast(0);
        Visible = true;
    }

    public override void _Process(double delta)
    {
        for (int i = toastList.Count - 1; i >= 0; i--)
        {
            toastList[i] = (toastList[i].Button, toastList[i].Left - 1);
            if (toastList[i].Left <= 0) DropToast(i);
        }
    }

    private void DropToast(int index)
    {
        toasts.RemoveChild(toastList[index].Button);
        toastList[index].Button.QueueFree();
        toastList.RemoveAt(index);
        Visible = rows.Count > 0 || deferred > 0 || offers.Visible || toastList.Count > 0;
    }

    private float RowWidth => theme.Px(theme.Breakpoint == UiBreakpoint.Compact ? 220 : 300);

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

    /// <summary>C039: the trade offers still waiting for an answer and what became of the latest ones (<see cref="OfferTracker"/>), under the list.</summary>
    public string OfferText => offers.Text;

    public void SetOffers(IReadOnlyList<string> lines)
    {
        offers.Text = string.Join("\n", lines.Select(it => it.TrText()));
        offers.Visible = lines.Count > 0;
        if (lines.Count > 0) offers.CustomMinimumSize = new Vector2(RowWidth, 0f);
        Visible = rows.Count > 0 || deferred > 0 || lines.Count > 0 || toastList.Count > 0;
    }

    /// <summary>How many pending alerts wait to be reviewed (informational ones and ones put off with "Decide later"): the button that opens them shows while there are any.</summary>
    public void SetWaiting(int count)
    {
        deferred = count;
        review.Visible = count > 0;
        review.Text = AlertPopupModel.ReviewLabel(count).TrText();
        Visible = rows.Count > 0 || deferred > 0 || offers.Visible || toastList.Count > 0;
    }

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
        toggle.CustomMinimumSize = new Vector2(RowWidth + uiTheme.Px(UiTokens.S4) * 2, 0f); // as wide as the drop-down, so it stays on screen
        toggle.Text = NotificationsModel.ToggleLabel(rows.Count, IsOpen).TrText();
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
        summary.Text = NotificationsModel.Summary(turn, rows).TrText();
        summary.CustomMinimumSize = new Vector2(RowWidth, theme.Px(56)); // room for three wrapped lines: an autowrapped label does not reserve them itself
        summary.TooltipText = summary.Text;
        scroll.CustomMinimumSize = new Vector2(RowWidth + theme.Px(16), theme.Px(Math.Min(rows.Count, 5) * 32));
        foreach (var row in rows)
        {
            var button = new Button
            {
                Name = "Notification_" + row.Index,
                Text = row.Text.TrText(),
                ThemeTypeVariation = UiVariants.ButtonRow,
                Alignment = HorizontalAlignment.Left,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = new Vector2(RowWidth, theme.Px(28)),
                TooltipText = (row.Category + (row.Target == null ? "" : row.Target.Kind == NotificationTargetKind.Tile ? ": click to show on the map" : ": click to open")).TrText(),
            };
            button.AddThemeFontSizeOverride("font_size", theme.Px(14));
            var chosen = row;
            button.Pressed += () => Chosen?.Invoke(chosen);
            items.AddChild(button);
            buttons[row.Index] = button;
        }
        toggle.Text = NotificationsModel.ToggleLabel(rows.Count, IsOpen).TrText();
        list.ResetSize();
        Visible = rows.Count > 0 || deferred > 0 || offers.Visible || toastList.Count > 0; // nothing to list: no button either
    }
}
