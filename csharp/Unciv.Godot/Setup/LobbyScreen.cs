using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.Client.Net.Session;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.Net;
using Unciv.Net.Lobby;
using Unciv.Server;
using Unciv.Utils;

namespace Unciv.GodotClient.Setup;

/// <summary>
/// C035: the lobby screen, one view for the host and for a joiner. It reads the <see cref="LobbyState"/> its <see cref="ClientSession"/> receives and changes the player's own seat
/// only by sending <c>LobbySet</c> (nation, colour, ready) through that session; the host additionally holds the in-process <see cref="ServerLobby"/> for Kick, Close or Open slot,
/// AI slot and Start. The settings are shown read-only (the host edits them on the Host screen). Chat goes through the session. The screen drains the session's event channel on the main
/// thread (<see cref="_Process"/>), so a refusal, a kick or the host closing the lobby reaches the player as a sentence, and the start of the game as <see cref="GameStarting"/>.
/// A joiner whose join was refused (for example a ruleset mismatch) gets the same screen from <see cref="Refused"/>, which shows only the reason.
/// </summary>
public partial class LobbyScreen : Control
{
    private const int MaxChatLines = 40;

    private readonly UiTheme theme;
    private readonly NewGameForm nations;
    private readonly ClientSession? session;
    private readonly ServerLobby? hostControls;
    private readonly string ownKey;
    private readonly Label titleLabel = new() { Name = "LobbyTitle" };
    private readonly VBoxContainer slotsBox = new() { Name = "Slots" };
    private readonly VBoxContainer nationBox = new() { Name = "NationPicker", SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly ScrollContainer pickerScroll = new() { Name = "PickerScroll", Visible = false, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly HBoxContainer bottomRow = new() { Name = "SettingsAndChat", SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Label settingsLabel = new() { Name = "Settings", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2 };
    private readonly VBoxContainer chatBox = new() { Name = "ChatLog" };
    private readonly ScrollContainer chatScroll = new() { Name = "ChatScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Label messageLabel = new() { Name = "LobbyMessage", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label startHint = new() { Name = "StartHint", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Dictionary<string, Button> nationButtons = [];
    private readonly Dictionary<string, Button> colourButtons = [];
    private readonly Dictionary<int, Button> kickButtons = [];
    private readonly Dictionary<int, Button> closeButtons = [];
    private readonly Dictionary<int, Button> aiButtons = [];
    private readonly Dictionary<int, Button> openButtons = [];
    private readonly List<Label> slotLabels = [];
    private readonly List<string> chat = [];
    private LobbyState? lobby;
    private bool busy;
    private bool ended;

    public LobbyScreen(UiTheme theme, NewGameForm nations, ClientSession session, string ownKey, ServerLobby? hostControls = null)
    {
        this.theme = theme;
        this.nations = nations;
        this.session = session;
        this.ownKey = ownKey;
        this.hostControls = hostControls;
        Name = "LobbyScreen";
        Build(hostControls != null);
        lobby = session.Lobby;
        Rebuild();
    }

    private LobbyScreen(UiTheme theme, NewGameForm nations, string reason)
    {
        this.theme = theme;
        this.nations = nations;
        ownKey = "";
        Name = "LobbyScreen";
        Build(false);
        End(reason);
    }

    /// <summary>The screen of a joiner the host refused: only the reason (<see cref="LobbyModel.RefusalText"/>) and Leave.</summary>
    public static LobbyScreen Refused(UiTheme theme, NewGameForm nations, string code) => new(theme, nations, LobbyModel.RefusalText(code));

    public Button LeaveButton { get; private set; } = null!; // JUSTIFY: C035 assigned by Build, which every constructor runs

    public Button StartButton { get; private set; } = null!; // JUSTIFY: C035 assigned by Build, which every constructor runs

    public Button ReadyButton { get; private set; } = null!; // JUSTIFY: C035 assigned by Build, which every constructor runs

    public Button NationToggle { get; private set; } = null!; // JUSTIFY: C035 assigned by Build, which every constructor runs

    public Button SendButton { get; private set; } = null!; // JUSTIFY: C035 assigned by Build, which every constructor runs

    public LineEdit ChatField { get; private set; } = null!; // JUSTIFY: C035 assigned by Build, which every constructor runs

    /// <summary>Raised when the player presses Leave; the owner of the screen leaves the session (or stops hosting) and pops the screen.</summary>
    public event Action? LeaveRequested;

    /// <summary>Raised when the host started the game (the session announced it); the game screen takes over from here.</summary>
    public event Action? GameStarting;

    /// <summary>What the screen currently tells the player (an error, a refusal, a closed lobby), or empty.</summary>
    public string Message => messageLabel.Text;

    /// <summary>The text of the slot table, one line per slot.</summary>
    public IReadOnlyList<string> SlotLines => slotLabels.Select(it => it.Text).ToList();

    /// <summary>The read-only settings, one line each.</summary>
    public IReadOnlyList<string> SettingsLines => settingsLabel.Text.Split('\n');

    /// <summary>The chat so far, oldest first, as "Sender: text".</summary>
    public IReadOnlyList<string> ChatLines => chat;

    public string StartReason => startHint.Text;

    public LobbyState? State => lobby;

    public bool IsHost => hostControls != null;

    /// <summary>The nation button of the picker (the picker is open), including "Random".</summary>
    public Button? NationButton(string nation) => nationButtons.GetValueOrDefault(nation);

    public Button? ColourButton(string colour) => colourButtons.GetValueOrDefault(colour);

    public Button? KickButton(int slot) => kickButtons.GetValueOrDefault(slot);

    public Button? CloseButton(int slot) => closeButtons.GetValueOrDefault(slot);

    public Button? AiButton(int slot) => aiButtons.GetValueOrDefault(slot);

    public Button? OpenButton(int slot) => openButtons.GetValueOrDefault(slot);

    /// <summary>Scrolls the nation picker so the control is in view.</summary>
    public void Reveal(Control control) => pickerScroll.EnsureControlVisible(control);

    public override void _Process(double delta)
    {
        if (session == null || ended) return;
        bool changed = false;
        while (session.Events.TryRead(out var next))
        {
            switch (next)
            {
                case LobbyUpdated updated:
                    lobby = updated.Lobby;
                    changed = true;
                    break;
                case LobbyChatReceived received:
                    AddChat(received.Sender, received.Text);
                    break;
                case GameStarted:
                    GameStarting?.Invoke();
                    break;
                case JoinRefusedEvent refused:
                    End(LobbyModel.RefusalText(refused.Code));
                    return;
                case HostClosed:
                    End("The host closed the lobby.");
                    return;
                case SessionEnded over when !string.IsNullOrEmpty(over.Reason) && over.Reason != ClientCodes.SessionClosed:
                    End(LobbyModel.RefusalText(over.Reason));
                    return;
            }
        }
        if (changed) Rebuild();
    }

    /// <summary>Rebuilds the slot table, the settings and the Start button from the latest <see cref="LobbyState"/>.</summary>
    public void Rebuild()
    {
        if (lobby is not { } state) return;
        titleLabel.Text = $"{"Lobby".TrText()}: {LobbyModel.Title(state)}";
        settingsLabel.Text = string.Join("\n", LobbyModel.SettingsLines(state));
        FillSlots(state);
        FillPicker(state);
        var check = LobbyModel.CanStart(state);
        StartButton.Visible = IsHost;
        StartButton.Disabled = busy || !check.CanStart;
        startHint.Visible = IsHost;
        startHint.Text = check.Reason;
    }

    private LobbySlot? Own(LobbyState state) => state.Slots.FirstOrDefault(it => it.Kind == "Human" && it.PlayerKeyId == ownKey);

    private void Build(bool host)
    {
        this.Fill();
        var column = new VBoxContainer();
        column.Fill();
        // C101: a touch build keeps the content off the screen edges (rounded corners, the notch); a mouse build is unchanged
        if (theme.MinTargetPx > 0)
        {
            var margin = new MarginContainer { Name = "LobbyMargin" };
            margin.Fill();
            foreach (string side in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, theme.Px(UiTokens.S3));
            AddChild(margin);
            column.SetAnchorsPreset(LayoutPreset.TopLeft);
            column.SizeFlagsVertical = SizeFlags.ExpandFill;
            margin.AddChild(column);
        }
        else
        {
            AddChild(column);
            // T312: on a window wider than the widest content (ultrawide) the lobby keeps that width, centred
            Resized += () =>
            {
                float extra = Math.Max(0f, (Size.X - theme.Px(UiTokens.MaxContentWidth)) / 2f);
                column.OffsetLeft = extra;
                column.OffsetRight = -extra;
            };
        }
        column.AddChild(titleLabel);
        slotsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        column.AddChild(slotsBox);

        var seat = new HBoxContainer();
        ReadyButton = new Button { Text = "Ready".Tr(), Name = "ReadyButton", ToggleMode = true, Visible = false };
        ReadyButton.Pressed += OnReady;
        NationToggle = new Button { Text = "Pick nation".Tr(), Name = "NationToggle", ToggleMode = true, Visible = false };
        NationToggle.Pressed += () => ShowPicker(NationToggle.ButtonPressed);
        seat.AddChild(ReadyButton);
        seat.AddChild(NationToggle);
        column.AddChild(seat);

        pickerScroll.AddChild(nationBox);
        column.AddChild(pickerScroll);

        // Settings (read-only; the host edits them on the Host screen) on the left, chat on the right; hidden while the nation picker is open.
        column.AddChild(bottomRow);
        // C101: the settings scroll when they do not fit beside the chat, so Start and Leave stay on the screen of a phone
        var settingsScroll = new ScrollContainer { Name = "SettingsScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2 };
        var settings = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        settingsScroll.AddChild(settings);
        settings.AddChild(new Label { Text = (host ? "Settings (change them on the Host screen)" : "Settings (set by the host)").Tr(), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        settingsLabel.AddThemeFontSizeOverride("font_size", theme.Px(13));
        settings.AddChild(settingsLabel);
        bottomRow.AddChild(settingsScroll);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3 };
        bottomRow.AddChild(right);
        right.AddChild(new Label { Text = "Chat".Tr() });
        chatBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        chatScroll.AddChild(chatBox);
        right.AddChild(chatScroll);
        var send = new HBoxContainer();
        ChatField = new LineEdit { Name = "ChatField", PlaceholderText = "Say something".Tr(), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        ChatField.TextSubmitted += _ => SendChat();
        SendButton = new Button { Text = "Send".Tr(), Name = "SendButton" };
        SendButton.Pressed += SendChat;
        send.AddChild(ChatField);
        send.AddChild(SendButton);
        right.AddChild(send);

        var bottom = new HBoxContainer();
        StartButton = new Button { Text = "Start game".Tr(), Name = "StartGameButton", Visible = false, Disabled = true };
        StartButton.Pressed += () => _ = StartAsync();
        LeaveButton = new Button { Text = "Leave".Tr(), Name = "LeaveButton" };
        LeaveButton.Pressed += () => LeaveRequested?.Invoke();
        bottom.AddChild(StartButton);
        bottom.AddChild(LeaveButton);
        column.AddChild(bottom);
        column.AddChild(startHint);
        column.AddChild(messageLabel);
    }

    // The nation picker takes the place of the settings and chat while it is open (the window is small).
    private void ShowPicker(bool show)
    {
        pickerScroll.Visible = show;
        bottomRow.Visible = !show;
    }

    private void FillSlots(LobbyState state)
    {
        Clear(slotsBox);
        slotLabels.Clear();
        kickButtons.Clear();
        closeButtons.Clear();
        aiButtons.Clear();
        openButtons.Clear();
        var own = Own(state);
        foreach (var slot in state.Slots)
        {
            int index = slot.Slot;
            var line = new HBoxContainer { Name = $"Slot{(index + 1).ToString(CultureInfo.InvariantCulture)}" };
            var text = new Label { Text = LobbyModel.SlotText(slot, ownKey, state.HostName), CustomMinimumSize = new Vector2(theme.Px(185), 0) };
            line.AddChild(text);
            slotLabels.Add(text);
            if (slot.Kind == "Human")
            {
                line.AddChild(new Label { Text = (slot.Ready ? "Ready" : "Not ready").TrText(), CustomMinimumSize = new Vector2(theme.Px(72), 0) });
                line.AddChild(NationLabel(slot));
                line.AddChild(Swatch(slot.Color));
            }
            if (IsHost) AddHostButtons(line, slot);
            slotsBox.AddChild(line);
        }
        ReadyButton.Visible = own != null;
        NationToggle.Visible = own != null;
        if (own != null)
        {
            ReadyButton.ButtonPressed = own.Ready;
            ReadyButton.Text = (own.Ready ? "Ready (press to cancel)" : "Ready").TrText();
        }
        else ShowPicker(false);
    }

    private Control NationLabel(LobbySlot slot)
    {
        string nation = slot.Nation ?? Constants.Random;
        var label = new Label { Text = nation.TrText(), CustomMinimumSize = new Vector2(theme.Px(100), 0) };
        if (nations.NationColours(nation) is { } colours) label.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(colours.Background).Lightened(0.35f));
        return label;
    }

    private Control Swatch(string? colour) => new ColorRect
    {
        Color = ProceduralAssets.ToColor(PlaceholderPalette.ForLobbyColour(colour)),
        CustomMinimumSize = new Vector2(theme.Px(24), theme.Px(24)),
    };

    private void AddHostButtons(HBoxContainer line, LobbySlot slot)
    {
        int index = slot.Slot;
        var lobbyHost = hostControls.NotNull().Lobby;
        switch (slot.Kind)
        {
            case "Human" when slot.PlayerKeyId != ownKey:
                kickButtons[index] = Add(line, "Kick", () => lobbyHost.KickAsync(index));
                break;
            case "Open":
                aiButtons[index] = Add(line, "AI", () => lobbyHost.SetSlotKindAsync(index, SlotKind.AI));
                closeButtons[index] = Add(line, "Close", () => lobbyHost.CloseSlotAsync(index));
                break;
            case "AI":
                openButtons[index] = Add(line, "Open", () => lobbyHost.SetSlotKindAsync(index, SlotKind.Open));
                break;
            case "Closed":
                openButtons[index] = Add(line, "Open", () => lobbyHost.OpenSlotAsync(index));
                break;
        }
    }

    private Button Add(HBoxContainer line, string text, Func<Task> action)
    {
        var button = new Button { Text = text.Tr(), Name = $"{text}Button" };
        button.Pressed += () => _ = RunAsync(action);
        line.AddChild(button);
        return button;
    }

    private void FillPicker(LobbyState state)
    {
        Clear(nationBox);
        nationButtons.Clear();
        colourButtons.Clear();
        if (Own(state) is not { } own) return;
        nationBox.AddChild(new Label { Text = "Nation".Tr() });
        var grid = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        nationBox.AddChild(grid);
        foreach (string name in new[] { Constants.Random }.Concat(nations.PlayableNations))
        {
            string nation = name;
            var button = new Button { Text = name.TrText(), ToggleMode = true, ButtonPressed = string.Equals(own.Nation, name, StringComparison.OrdinalIgnoreCase) };
            if (nations.NationColours(name) is { } colours)
            {
                var box = new StyleBoxFlat { BgColor = ProceduralAssets.ToColor(colours.Background), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4 };
                button.AddThemeStyleboxOverride("normal", box);
                button.AddThemeStyleboxOverride("hover", box);
                button.AddThemeStyleboxOverride("pressed", box);
                button.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(colours.Text));
                button.AddThemeColorOverride("font_pressed_color", ProceduralAssets.ToColor(colours.Text));
                button.AddThemeColorOverride("font_hover_color", ProceduralAssets.ToColor(colours.Text));
                button.AddThemeColorOverride("font_disabled_color", ProceduralAssets.ToColor(colours.Text).Darkened(0.4f));
            }
            button.Disabled = name != Constants.Random && state.Slots.Any(it => it.Slot != own.Slot && it.Kind != "Closed" && string.Equals(it.Nation, name, StringComparison.OrdinalIgnoreCase));
            button.Pressed += () => _ = RunAsync(async () => await Session().SetLobbySlotAsync(own.Slot, nation, null, own.Ready));
            nationButtons[name] = button;
            grid.AddChild(button);
        }
        nationBox.AddChild(new Label { Text = "Colour".Tr() });
        var colours2 = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        nationBox.AddChild(colours2);
        foreach (string name in PlaceholderPalette.LobbyColourNames)
        {
            string colour = name;
            var button = new Button { Text = name.TrText(), ToggleMode = true, ButtonPressed = own.Color == name };
            var box = new StyleBoxFlat { BgColor = ProceduralAssets.ToColor(PlaceholderPalette.ForLobbyColour(name)), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4 };
            button.AddThemeStyleboxOverride("normal", box);
            button.AddThemeStyleboxOverride("hover", box);
            button.AddThemeStyleboxOverride("pressed", box);
            foreach (string stateName in new[] { "font_color", "font_hover_color", "font_pressed_color" })
                button.AddThemeColorOverride(stateName, ProceduralAssets.ToColor(PlaceholderPalette.Background));
            button.Disabled = state.Slots.Any(it => it.Slot != own.Slot && it.Kind == "Human" && it.Color == name);
            button.Pressed += () => _ = RunAsync(async () => await Session().SetLobbySlotAsync(own.Slot, null, colour, own.Ready));
            colourButtons[name] = button;
            colours2.AddChild(button);
        }
    }

    private ClientSession Session() => session.NotNull();

    private void OnReady()
    {
        if (lobby is not { } state || Own(state) is not { } own) return;
        bool ready = !own.Ready;
        _ = RunAsync(async () => await Session().SetLobbySlotAsync(own.Slot, null, null, ready));
    }

    private void SendChat()
    {
        string text = ChatField.Text.Trim();
        if (text.Length == 0 || session == null || ended) return;
        ChatField.Text = "";
        _ = RunAsync(async () => await session.SendChatAsync(text));
    }

    private void AddChat(string? sender, string text)
    {
        chat.Add($"{sender ?? "?"}: {text}");
        while (chat.Count > MaxChatLines) chat.RemoveAt(0);
        Clear(chatBox);
        foreach (string line in chat) chatBox.AddChild(new Label { Text = line, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        Callable.From(() => chatScroll.ScrollVertical = (int)chatScroll.GetVScrollBar().MaxValue).CallDeferred();
    }

    private async Task StartAsync()
    {
        if (busy || hostControls == null) return;
        busy = true;
        StartButton.Disabled = true;
        try
        {
            SetMessage("Starting the game ...", false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await hostControls.StartGameAsync();
            SetMessage("", false);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            SetMessage(e.Message, true);
        }
        finally
        {
            busy = false;
            Rebuild();
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            SetMessage(e.Message, true);
        }
    }

    // The lobby is over for this player: nothing more to change, only the reason and Leave.
    private void End(string reason)
    {
        ended = true;
        ReadyButton.Visible = false;
        NationToggle.Visible = false;
        ShowPicker(false);
        bottomRow.Visible = false;
        StartButton.Visible = false;
        startHint.Visible = false;
        SendButton.Disabled = true;
        ChatField.Editable = false;
        Clear(slotsBox);
        slotLabels.Clear();
        SetMessage(reason, true);
    }

    private void SetMessage(string text, bool isError)
    {
        messageLabel.Text = text.TrText();
        messageLabel.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(isError ? PlaceholderPalette.ErrorText : PlaceholderPalette.Text));
    }

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
