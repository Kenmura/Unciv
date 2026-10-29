using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C045: the in-game players-and-chat panel of a hosted game. Top: a line per player (connection dot, name, status) with Kick and Rebind on the host's view; Rebind opens a
/// field for the new device's player key. Below: the chat log and a line to type into. It draws <see cref="PlayerRow"/>s and <see cref="ChatLine"/>s and decides nothing:
/// typing, kicking and rebinding are reported through events and the host answers.
/// </summary>
public partial class SocialView : Control
{
    private readonly PanelContainer panel = new() { Name = "SocialPanel", MouseFilter = MouseFilterEnum.Stop };
    private readonly VBoxContainer players = new() { Name = "PlayerRows" };
    private readonly HBoxContainer rebind = new() { Name = "RebindRow", Visible = false };
    private readonly LineEdit keyEdit = new() { Name = "RebindKey", PlaceholderText = "New player key".Tr(), CustomMinimumSize = new Vector2(220, 0) };
    private readonly Label rebindFor = new() { Name = "RebindFor" };
    private readonly VBoxContainer log = new() { Name = "ChatLog" };
    private readonly ScrollContainer scroll = new() { Name = "ChatScroll", CustomMinimumSize = new Vector2(0, 150), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly LineEdit input = new() { Name = "ChatInput", PlaceholderText = "Say something".Tr(), MaxLength = SocialModel.MaxChatChars, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Label notice = new() { Name = "SocialNotice", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(260, 0) };
    private string? rebindCiv;

    public SocialView()
    {
        Name = "Social";
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        var column = new VBoxContainer();
        column.AddChild(new Label { Text = "Players".Tr() });
        column.AddChild(players);
        rebind.AddChild(rebindFor);
        rebind.AddChild(keyEdit);
        var apply = new Button { Name = "RebindApply", Text = "Rebind seat".Tr() };
        apply.Pressed += SubmitRebind;
        rebind.AddChild(apply);
        var cancel = new Button { Name = "RebindCancel", Text = "Cancel".Tr() };
        cancel.Pressed += CloseRebind;
        rebind.AddChild(cancel);
        column.AddChild(rebind);
        column.AddChild(new HSeparator());
        column.AddChild(new Label { Text = "Chat".Tr() });
        scroll.AddChild(log);
        log.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        column.AddChild(scroll);
        var send = new HBoxContainer();
        send.AddChild(input);
        var sendButton = new Button { Name = "ChatSend", Text = "Send".Tr() };
        sendButton.Pressed += SubmitChat;
        send.AddChild(sendButton);
        input.TextSubmitted += _ => SubmitChat();
        column.AddChild(send);
        column.AddChild(notice);
        panel.AddChild(column);
        AddChild(panel);
    }

    /// <summary>The player typed a line (already cleaned; never blank) and pressed Send or Enter.</summary>
    public event Action<string>? ChatSubmitted;

    public event Action<string>? KickRequested;

    /// <summary>Civ id and the pasted key.</summary>
    public event Action<string, string>? RebindRequested;

    public IReadOnlyList<PlayerRow> Rows { get; private set; } = [];

    public IReadOnlyList<string> ChatTexts => [.. log.GetChildren().OfType<Label>().Select(it => it.Text)];

    public string NoticeText => notice.Text;

    public bool RebindOpen => rebind.Visible;

    public string? RebindCivId => rebindCiv;

    public Rect2 PanelRect => panel.GetGlobalRect();

    public void Init(UiTheme theme)
    {
        Theme = theme.Theme;
        panel.CustomMinimumSize = new Vector2(theme.Px(340), 0);
        scroll.CustomMinimumSize = new Vector2(0, theme.Px(150));
        keyEdit.CustomMinimumSize = new Vector2(theme.Px(180), 0);
    }

    /// <summary>Lays the panel out with its top-right corner at <paramref name="topRight"/>.</summary>
    public void LayoutAt(Vector2 topRight)
    {
        panel.Size = panel.GetCombinedMinimumSize();
        panel.Position = new Vector2(topRight.X - panel.Size.X, topRight.Y);
    }

    /// <summary>Shows or hides the panel.</summary>
    public void Toggle(bool open) => Visible = open;

    public void ShowPlayers(IReadOnlyList<PlayerRow> rows)
    {
        Rows = rows;
        foreach (var child in players.GetChildren())
        {
            players.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var row in rows)
        {
            var line = new HBoxContainer { Name = "Player_" + row.CivId };
            var good = row.Status == SeatStatus.Online;
            line.AddChild(new ColorRect
            {
                Name = good ? "Connected" : "Offline",
                CustomMinimumSize = new Vector2(10, 10),
                Color = ToColour(good ? PlaceholderPalette.HealthGood : row.Status == SeatStatus.Offline ? PlaceholderPalette.HealthBad : PlaceholderPalette.HealthWarn),
            });
            line.AddChild(new Label { Name = "Name", Text = row.IsYou ? row.Name + " (" + "you".Tr() + ")" : row.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill });
            line.AddChild(new Label { Name = "Status", Text = StatusText(row.Status).Tr() });
            if (row.CanKick)
            {
                var kick = new Button { Name = "Kick_" + row.CivId, Text = "Kick".Tr() };
                var civ = row.CivId;
                kick.Pressed += () => KickRequested?.Invoke(civ);
                line.AddChild(kick);
            }
            if (row.CanRebind)
            {
                var rebindButton = new Button { Name = "Rebind_" + row.CivId, Text = "Rebind".Tr() };
                var civ = row.CivId;
                var name = row.Name;
                rebindButton.Pressed += () => OpenRebind(civ, name);
                line.AddChild(rebindButton);
            }
            players.AddChild(line);
        }
        if (rebindCiv != null && rows.All(it => it.CivId != rebindCiv)) CloseRebind();
    }

    private static string StatusText(SeatStatus status) => status switch
    {
        SeatStatus.Online => "Online",
        SeatStatus.Offline => "Offline",
        SeatStatus.AwaitingDevice => "Waiting for new device",
        _ => "Released (AI)",
    };

    public void AddChat(ChatLine line)
    {
        log.AddChild(new Label { Text = line.Sender + ": " + line.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(280, 0) });
        while (log.GetChildCount() > SocialModel.MaxChatLines)
        {
            var oldest = log.GetChild(0);
            log.RemoveChild(oldest);
            oldest.QueueFree();
        }
        Callable.From(() => scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue).CallDeferred();
    }

    public void SetNotice(string text) => notice.Text = text;

    /// <summary>Sets what is typed in the chat line (scenarios and tests; the player types).</summary>
    public void SetTyped(string text) => input.Text = text;

    public void SetKeyTyped(string text) => keyEdit.Text = text;

    private void SubmitChat()
    {
        if (SocialModel.Clean(input.Text) is not { } text) return;
        input.Text = "";
        ChatSubmitted?.Invoke(text);
    }

    private void OpenRebind(string civ, string name)
    {
        rebindCiv = civ;
        rebindFor.Text = name;
        keyEdit.Text = "";
        rebind.Visible = true;
    }

    private void SubmitRebind()
    {
        var key = keyEdit.Text.Trim();
        if (rebindCiv == null || key.Length == 0) return;
        var civ = rebindCiv;
        CloseRebind();
        RebindRequested?.Invoke(civ, key);
    }

    private void CloseRebind()
    {
        rebindCiv = null;
        rebind.Visible = false;
        keyEdit.Text = "";
    }

    private static Color ToColour(Rgb rgb) => new(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);
}
