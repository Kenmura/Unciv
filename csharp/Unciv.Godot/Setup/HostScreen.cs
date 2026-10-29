using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using Godot;
using Unciv.Assets;
using Unciv.Client.Net.Hosting;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.Net.Identity;
using TabBar = Unciv.GodotClient.Ui.TabBar;

namespace Unciv.GodotClient.Setup;

/// <summary>What the host screen needs from the machine, as plain callbacks so a scenario can supply fixed ones (no real user folder, clipboard or network cards).</summary>
public sealed class HostScreenEnvironment
{
    public required Func<ClientIdentity> Identity { get; init; }

    public required Func<X509Certificate2> Certificate { get; init; }

    /// <summary>The folder with one directory per saved hosted game.</summary>
    public required string HostedGamesDirectory { get; init; }

    /// <summary>The name the host appears under in the lobby.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wall clock in Unix milliseconds: the map seed when none was typed.</summary>
    public required Func<long> Clock { get; init; }

    public required Action<string> SetClipboard { get; init; }

    public Func<IReadOnlyList<LocalAddress>> Addresses { get; init; } = LocalAddresses.Enumerate;
}

/// <summary>
/// C034: the Host screen. Tabs for Game (name, civs, city-states, AI fill, difficulty, speed, era, victories), Map, Rules (base ruleset and mods), Network (turn timer, password, port,
/// the addresses of this machine and the port-forward hint) and Saved games (resume a hosted game). It only edits a <see cref="HostSettingsForm"/> (whose game side is the C015
/// <see cref="NewGameForm"/>, with the same refusals) and shows what is wrong with it. Start hosting opens the lobby and listens (<see cref="HostingSession"/>); the screen then shows who
/// sits in which slot, the invite for the chosen address and Copy invite until Stop hosting. The slot table here is deliberately plain: the lobby screen (C035) replaces it.
/// Resuming a saved game reopens it with the same host and listens again.
/// </summary>
public partial class HostScreen : Control
{
    private readonly UiTheme theme;
    private readonly HostScreenEnvironment env;
    private readonly VBoxContainer gamePage = new() { Name = "GamePage" };
    private readonly VBoxContainer mapPage = new() { Name = "MapPage" };
    private readonly VBoxContainer rulesPage = new() { Name = "RulesPage" };
    private readonly VBoxContainer networkPage = new() { Name = "NetworkPage" };
    private readonly VBoxContainer savedPage = new() { Name = "SavedPage" };
    private readonly Label messageLabel = new() { Name = "HostMessage", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Dictionary<string, ChoiceRow> rows = [];
    private readonly List<Button> resumeButtons = [];
    private readonly VBoxContainer settingsColumn = new() { Name = "SettingsColumn" };
    private readonly VBoxContainer hostingPanel = new() { Name = "HostingPanel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly ScrollContainer hostingScroll = new() { Name = "HostingScroll", Visible = false, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly VBoxContainer slotsBox = new() { Name = "Slots" };
    private readonly Label hostingTitle = new() { Name = "HostingTitle" };
    private readonly Label hostingStatus = new() { Name = "HostingStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label addressNote = new() { Name = "AddressNote", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer hostingAddresses = new() { Name = "HostingAddresses" };
    private HostedGameInfo? resumedInfo;
    private ClientIdentity? identity;
    private X509Certificate2? certificate;
    private int selectedAddress;
    private string slotSignature = "";
    private bool busy;

    public HostScreen(UiTheme theme, HostSettingsForm form, HostScreenEnvironment env)
    {
        this.theme = theme;
        this.env = env;
        Form = form;
        Name = "HostScreen";
        var column = new VBoxContainer();
        column.Fill();
        AddChild(column);

        settingsColumn.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddChild(settingsColumn);
        Tabs = new TabBar { SizeFlagsVertical = SizeFlags.ExpandFill };
        foreach (var (title, page) in new[] { ("Game", gamePage), ("Map", mapPage), ("Rules", rulesPage), ("Network", networkPage), ("Saved games", savedPage) })
        {
            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            page.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            scroll.AddChild(page);
            Tabs.AddTab(title, scroll);
        }
        settingsColumn.AddChild(Tabs);
        var bottom = new HBoxContainer();
        StartButton = new Button { Text = "Start hosting".Tr(), Name = "StartHostingButton" };
        StartButton.Pressed += () => _ = StartAsync();
        BackButton = new Button { Text = "Back".Tr(), Name = "BackButton" };
        BackButton.Pressed += () => BackRequested?.Invoke();
        bottom.AddChild(StartButton);
        bottom.AddChild(BackButton);
        settingsColumn.AddChild(bottom);

        BuildHostingPanel(column);
        column.AddChild(messageLabel);
        Refresh();
    }

    public HostSettingsForm Form { get; }

    public TabBar Tabs { get; }

    public Button StartButton { get; private set; }

    public Button BackButton { get; }

    public Button StopButton { get; private set; } = null!; // JUSTIFY: C034 assigned by BuildHostingPanel, which the constructor runs

    public Button CopyInviteButton { get; private set; } = null!; // JUSTIFY: C034 assigned by BuildHostingPanel, which the constructor runs

    public LineEdit InviteField { get; private set; } = null!; // JUSTIFY: C034 assigned by BuildHostingPanel, which the constructor runs

    public LineEdit NameField { get; private set; } = null!; // JUSTIFY: C034 assigned by FillGame, which the constructor runs

    public LineEdit SeedField { get; private set; } = null!; // JUSTIFY: C034 assigned by FillMap, which the constructor runs

    public LineEdit PasswordField { get; private set; } = null!; // JUSTIFY: C034 assigned by FillNetwork, which the constructor runs

    public LineEdit PortField { get; private set; } = null!; // JUSTIFY: C034 assigned by FillNetwork, which the constructor runs

    public LineEdit PublicAddressField { get; private set; } = null!; // JUSTIFY: C034 assigned by FillNetwork, which the constructor runs

    public CheckBox DynamicTimerCheck { get; private set; } = null!; // JUSTIFY: T252 assigned by FillNetwork, which the constructor runs
    public CheckBox AiFillCheck { get; private set; } = null!; // JUSTIFY: C034 assigned by FillGame, which the constructor runs

    /// <summary>The running host (new lobby or resumed game), or null while the settings are shown.</summary>
    public HostingSession? Session { get; private set; }

    public bool IsHosting => Session != null;

    /// <summary>Raised when the player leaves the screen; hosting, if running, is stopped first.</summary>
    public event Action? BackRequested;

    /// <summary>Raised after hosting started (a new lobby or a resumed game).</summary>
    public event Action<HostingSession>? HostingStarted;

    /// <summary>What the screen currently tells the player.</summary>
    public string Message => messageLabel.Text;

    /// <summary>The addresses offered (those of this machine); the invite uses the selected one unless a public address was typed.</summary>
    public IReadOnlyList<LocalAddress> Addresses { get; private set; } = [];

    /// <summary>The address in the invite: the typed public address, else the selected local one, else the loopback address.</summary>
    public string InviteAddress =>
        Form.PublicAddress.Trim().Length > 0 ? Form.PublicAddress.Trim() : selectedAddress < Addresses.Count ? Addresses[selectedAddress].Address : "127.0.0.1";

    /// <summary>The invite string for <see cref="InviteAddress"/>; empty while not hosting.</summary>
    public string Invite => Session?.Invite(InviteAddress) ?? "";

    /// <summary>The Resume button of the n-th saved game (newest first).</summary>
    public Button? ResumeButton(int index) => index >= 0 && index < resumeButtons.Count ? resumeButtons[index] : null;

    /// <summary>The option button of a choice row: field is "Difficulty", "Speed", "Era", "Type", "Shape", "Size", "Resources", "Base ruleset", "Timer" or "Shortest turn".</summary>
    public Button? ChoiceButton(string field, string option) => rows.GetValueOrDefault(field)?.ButtonFor(option);

    /// <summary>The address buttons of the Network tab (before hosting) or the hosting panel.</summary>
    public Button? AddressButton(string address) => FindAddressRow()?.ButtonFor(address);

    /// <summary>The text of the slot table while hosting, one line per slot.</summary>
    public IReadOnlyList<string> SlotLines => slotsBox.GetChildren().OfType<Label>().Select(it => it.Text).ToList();

    public void Reveal(Control control)
    {
        for (var node = control.GetParent(); node != null; node = node.GetParent())
        {
            if (node is not ScrollContainer scroll) continue;
            scroll.EnsureControlVisible(control);
            return;
        }
    }

    public override void _Process(double delta)
    {
        if (Session is not { Lobby: not null } session) return;
        var signature = string.Join("|", session.Slots.Select(it => $"{it.Slot}:{it.Kind}:{it.PlayerName}:{it.Connected}"));
        if (signature == slotSignature) return;
        slotSignature = signature;
        FillSlots();
    }

    public override void _ExitTree()
    {
        // Leaving the screen without Stop hosting (Back, the game replaced the stack) must not leave a port open.
        if (Session is { } running)
        {
            Session = null;
            _ = running.DisposeAsync().AsTask();
        }
    }

    private void SetMessage(string text, bool isError)
    {
        messageLabel.Text = text.TrText();
        messageLabel.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(isError ? PlaceholderPalette.ErrorText : PlaceholderPalette.Text));
    }

    /// <summary>Rebuilds every settings page from the form and shows its problems.</summary>
    public void Refresh()
    {
        Addresses = env.Addresses();
        selectedAddress = Math.Clamp(selectedAddress, 0, Math.Max(0, Addresses.Count - 1));
        FillGame();
        FillMap();
        FillRules();
        FillNetwork();
        FillSaved();
        ShowProblems();
    }

    private void ShowProblems()
    {
        var problems = Form.Problems();
        SetMessage(string.Join("\n", problems), problems.Count > 0);
    }

    private void Change(string? error)
    {
        Refresh();
        if (error != null) SetMessage(error, true);
    }

    // Start and resume

    private async Task StartAsync()
    {
        if (busy || Session != null) return;
        busy = true;
        StartButton.Disabled = true;
        try
        {
            var built = Form.Build(env.Clock());
            if (built.Settings is not { } settings)
            {
                SetMessage(string.Join("\n", built.Errors), true);
                return;
            }
            SetMessage("Opening the lobby ...", false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var started = await HostingSession.StartLobbyAsync(settings, Identity(), Certificate(), env.DisplayName, Form.Port);
            ShowHosting(started, null);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            SetMessage(e.Message, true);
        }
        finally
        {
            busy = false;
            StartButton.Disabled = false;
        }
    }

    private async Task ResumeAsync(HostedGameInfo game)
    {
        if (busy || Session != null) return;
        busy = true;
        try
        {
            if (!HostSettingsForm.TryParsePort(Form.Port.ToString(CultureInfo.InvariantCulture), out int port, out string? problem))
            {
                SetMessage(problem ?? "", true);
                return;
            }
            SetMessage("Reopening the saved game ...", false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var resumed = await HostingSession.ResumeAsync(game.Directory, Identity(), Certificate(), port);
            ShowHosting(resumed, game);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            SetMessage(e.Message, true);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task StopAsync()
    {
        if (Session is not { } running) return;
        Session = null;
        StopButton.Disabled = true;
        await running.DisposeAsync();
        StopButton.Disabled = false;
        hostingScroll.Visible = false;
        settingsColumn.Visible = true;
        resumedInfo = null;
        slotSignature = "";
        Refresh();
        SetMessage("Hosting stopped.", false);
    }

    private ClientIdentity Identity() => identity ??= env.Identity();

    private X509Certificate2 Certificate() => certificate ??= env.Certificate();

    private void ShowHosting(HostingSession started, HostedGameInfo? resumed)
    {
        if (!IsInsideTree())
        {
            // The player left (Back) while the host was starting: nothing shows it, so do not leave the port open.
            _ = started.DisposeAsync().AsTask();
            return;
        }
        Session = started;
        resumedInfo = resumed;
        slotSignature = "";
        settingsColumn.Visible = false;
        hostingScroll.Visible = true;
        hostingTitle.Text = resumed != null ? "Hosting a saved game".TrText() : $"{"Hosting".TrText()} {Form.GameName.Trim()}";
        hostingStatus.Text = $"{"Friends connect to port".TrText()} {started.Port.ToString(CultureInfo.InvariantCulture)}. {"Host key".TrText()}: {started.Fingerprint}";
        RefreshInvite();
        FillSlots();
        SetMessage("", false);
        HostingStarted?.Invoke(started);
    }

    // Hosting panel

    private void BuildHostingPanel(Container parent)
    {
        hostingScroll.AddChild(hostingPanel);
        parent.AddChild(hostingScroll);
        hostingPanel.AddChild(hostingTitle);
        hostingPanel.AddChild(hostingStatus);
        hostingPanel.AddChild(new Label { Text = "Address friends use:".Tr() });
        hostingPanel.AddChild(hostingAddresses);
        hostingPanel.AddChild(addressNote);
        hostingPanel.AddChild(new Label { Text = "Invite:".Tr() });
        InviteField = new LineEdit { Name = "InviteField", Editable = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        hostingPanel.AddChild(InviteField);
        CopyInviteButton = new Button { Text = "Copy invite".Tr(), Name = "CopyInviteButton" };
        CopyInviteButton.Pressed += CopyInvite;
        hostingPanel.AddChild(CopyInviteButton);
        hostingPanel.AddChild(new Label { Text = "Players:".Tr() });
        hostingPanel.AddChild(slotsBox);
        StopButton = new Button { Text = "Stop hosting".Tr(), Name = "StopHostingButton" };
        StopButton.Pressed += () => _ = StopAsync();
        hostingPanel.AddChild(StopButton);
    }

    private void CopyInvite()
    {
        if (Invite.Length == 0) return;
        env.SetClipboard(Invite);
        SetMessage("Invite copied. Send it to your friends.", false);
    }

    private void RefreshInvite()
    {
        Clear(hostingAddresses);
        if (Session is not { } session) return;
        if (Addresses.Count > 0)
        {
            var row = new ChoiceRow(Addresses.Select(it => it.Address), Addresses[selectedAddress].Address) { Name = "AddressRow" };
            row.Chosen += address =>
            {
                selectedAddress = Math.Max(0, Addresses.ToList().FindIndex(it => it.Address == address));
                RefreshInvite();
            };
            hostingAddresses.AddChild(row);
        }
        InviteField.Text = session.Invite(InviteAddress);
        addressNote.Text = AddressNoteText(session.Port);
    }

    private ChoiceRow? FindAddressRow() => (Session != null ? hostingAddresses : networkPage).GetChildren().OfType<ChoiceRow>().FirstOrDefault(it => it.Name == "AddressRow");

    private void FillSlots()
    {
        Clear(slotsBox);
        if (Session is not { } session) return;
        if (resumedInfo is { } game)
        {
            slotsBox.AddChild(new Label { Text = $"{"Saved game with".TrText()} {string.Join(", ", game.Players)}" });
            slotsBox.AddChild(new Label { Text = "They rejoin with the key they used before.".Tr() });
            return;
        }
        foreach (var slot in session.Slots)
        {
            string text = slot.Kind switch
            {
                "Human" when slot.PlayerKeyId == Identity().PlayerKey => $"{slot.Slot + 1}. {slot.PlayerName} ({"you, the host".TrText()})",
                "Human" => $"{slot.Slot + 1}. {slot.PlayerName}{(slot.Connected == false ? $" ({"disconnected".TrText()})" : "")}",
                _ => $"{slot.Slot + 1}. {"Open".TrText()}",
            };
            slotsBox.AddChild(new Label { Text = text });
        }
    }

    // What the host can tell friends about the chosen address, and the port-forward hint.
    private string AddressNoteText(int port)
    {
        string p = port.ToString(CultureInfo.InvariantCulture);
        var typed = Form.PublicAddress.Trim();
        string scope = typed.Length > 0
            ? $"{typed}: {"the address you typed is used in the invite.".TrText()}"
            : Addresses.Count == 0
                ? "No network address was found; the invite uses 127.0.0.1, which only works on this computer.".TrText()
                : Addresses[selectedAddress].Scope == AddressScope.Lan
                    ? $"{Addresses[selectedAddress].Address}: {"friends on the same network (Wi-Fi or LAN) can use this.".TrText()}"
                    : $"{Addresses[selectedAddress].Address}: {"friends on the internet can use this once the port is open.".TrText()}";
        return $"{scope}\n{PortForwardHint(p)}";
    }

    private static string PortForwardHint(string port) =>
        $"{"Friends over the internet need two things: forward TCP port".TrText()} {port} {"on your router to this computer, and your public address (search for \"what is my IP\"; type it under Network, Public address). Friends on the same network need neither.".TrText()}";

    // Pages

    private static void Clear(Node page)
    {
        foreach (var child in page.GetChildren())
        {
            page.RemoveChild(child);
            child.QueueFree();
        }
    }

    private Button AddButton(Container parent, string text, Action pressed, string? name = null)
    {
        var button = new Button { Text = text.TrText() };
        if (name != null) button.Name = name;
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private static void AddHeading(Container page, string text) => page.AddChild(new Label { Text = text.TrText() });

    private void AddChoice(Container page, string field, string heading, IEnumerable<string> options, string selected, Action<string> chosen)
    {
        AddHeading(page, heading);
        var row = new ChoiceRow(options, selected);
        row.Chosen += value =>
        {
            chosen(value);
            Change(null);
        };
        rows[field] = row;
        page.AddChild(row);
    }

    private void AddCheck(Container page, string text, bool on, Func<bool, string?> changed)
    {
        var box = new CheckBox { Text = text.TrText(), ButtonPressed = on };
        box.Toggled += value => Change(changed(value));
        page.AddChild(box);
    }

    private LineEdit AddField(Container page, string heading, string name, string text, string placeholder, Action<string> changed)
    {
        AddHeading(page, heading);
        var field = new LineEdit { Name = name, Text = text, PlaceholderText = placeholder.TrText(), CustomMinimumSize = new Vector2(theme.Px(260), 0) };
        field.TextChanged += value =>
        {
            changed(value);
            ShowProblems();
        };
        page.AddChild(field);
        return field;
    }

    private void FillGame()
    {
        Clear(gamePage);
        rows.Clear();
        NameField = AddField(gamePage, "Game name", "NameField", Form.GameName, "Multiplayer Game", value => Form.GameName = value);
        var counts = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddButton(counts, "Fewer civs", () => Change(Form.Game.SetPlayerCount(Form.Game.Slots.Count - 1)));
        counts.AddChild(new Label { Text = $" {Form.Game.Slots.Count.ToString(CultureInfo.InvariantCulture)} {"civs".TrText()} " });
        AddButton(counts, "More civs", () => Change(Form.Game.SetPlayerCount(Form.Game.Slots.Count + 1)));
        counts.AddChild(new Label { Text = "   " });
        AddButton(counts, "Fewer city-states", () => Change(Form.Game.SetCityStates(Form.Game.CityStates - 1)));
        counts.AddChild(new Label { Text = $" {Form.Game.CityStates.ToString(CultureInfo.InvariantCulture)} {"city-states".TrText()} " });
        AddButton(counts, "More city-states", () => Change(Form.Game.SetCityStates(Form.Game.CityStates + 1)));
        gamePage.AddChild(counts);
        AiFillCheck = new CheckBox { Name = "AiFillCheck", Text = "Fill empty civs with AI when the game starts".Tr(), ButtonPressed = Form.AiFill };
        AiFillCheck.Toggled += on => Form.AiFill = on;
        gamePage.AddChild(AiFillCheck);
        AddCheck(gamePage, "No barbarians", Form.Game.NoBarbarians, on =>
        {
            Form.Game.NoBarbarians = on;
            return null;
        });
        var o = Form.Game.Options;
        AddChoice(gamePage, "Difficulty", "Difficulty", o.Difficulties.Select(it => it.Name), Form.Game.Difficulty, v => Form.Game.Difficulty = v);
        AddChoice(gamePage, "Speed", "Game speed", o.Speeds.Select(it => it.Name), Form.Game.Speed, v => Form.Game.Speed = v);
        AddChoice(gamePage, "Era", "Starting era", o.Eras.Select(it => it.Name), Form.Game.Era, v => Form.Game.Era = v);
        AddHeading(gamePage, "Victory types");
        var victories = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        gamePage.AddChild(victories);
        foreach (string victory in Form.Game.SelectableVictories)
        {
            string name = victory;
            AddCheck(victories, name, Form.Game.Victories.Contains(name), on => Form.Game.SetVictory(name, on));
        }
    }

    private void FillMap()
    {
        Clear(mapPage);
        var o = Form.Game.Options.MapOptions;
        AddChoice(mapPage, "Type", "Map type", o.Types, Form.Game.MapType, v => Form.Game.MapType = v);
        AddChoice(mapPage, "Shape", "Map shape", o.Shapes, Form.Game.Shape, v => Form.Game.Shape = v);
        AddChoice(mapPage, "Size", "Map size", o.Sizes, Form.Game.Size, v => Form.Game.Size = v);
        AddChoice(mapPage, "Resources", "Resources", o.Resources, Form.Game.Resources, v => Form.Game.Resources = v);
        var flags = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mapPage.AddChild(flags);
        AddCheck(flags, "World wrap", Form.Game.WorldWrap, on =>
        {
            Form.Game.WorldWrap = on;
            return null;
        });
        AddCheck(flags, "No ancient ruins", Form.Game.NoRuins, on =>
        {
            Form.Game.NoRuins = on;
            return null;
        });
        AddCheck(flags, "No natural wonders", Form.Game.NoNaturalWonders, on =>
        {
            Form.Game.NoNaturalWonders = on;
            return null;
        });
        SeedField = AddField(mapPage, "Seed (empty for a random map)", "SeedField", Form.Game.SeedText, "random", value => Form.Game.SeedText = value);
    }

    private void FillRules()
    {
        Clear(rulesPage);
        AddHeading(rulesPage, "Base ruleset");
        var row = new ChoiceRow(Form.Game.BaseRulesets, Form.Game.BaseRuleset);
        row.Chosen += value => Change(Form.Game.SetBaseRuleset(value));
        rows["Base ruleset"] = row;
        rulesPage.AddChild(row);
        AddHeading(rulesPage, Form.Game.AvailableMods.Count == 0 ? "No mods installed" : "Mods (friends need the same ones)");
        foreach (string mod in Form.Game.AvailableMods)
        {
            string name = mod;
            AddCheck(rulesPage, name, Form.Game.Mods.Contains(name), on => Form.Game.SetMod(name, on));
        }
    }

    private void FillNetwork()
    {
        Clear(networkPage);
        AddHeading(networkPage, "Turn timer (turns are simultaneous)");
        var timer = new ChoiceRow(HostSettingsForm.TimerChoices.Select(HostSettingsForm.TimerLabel), HostSettingsForm.TimerLabel(Form.TurnTimerSeconds));
        timer.Chosen += label =>
        {
            Form.TurnTimerSeconds = HostSettingsForm.TimerChoices.FirstOrDefault(it => HostSettingsForm.TimerLabel(it) == label);
            Change(null);
        };
        rows["Timer"] = timer;
        networkPage.AddChild(timer);
        DynamicTimerCheck = new CheckBox { Name = "DynamicTimerCheck", Text = "Scale each turn's time with the game (era, units, wars)".Tr(), ButtonPressed = Form.DynamicTurnTimer };
        DynamicTimerCheck.Toggled += on =>
        {
            Form.DynamicTurnTimer = on;
            Change(null);
        };
        networkPage.AddChild(DynamicTimerCheck);
        AddHeading(networkPage, "Shortest turn when scaling");
        var minTimer = new ChoiceRow(HostSettingsForm.MinTimerChoices.Select(HostSettingsForm.TimerLabel), HostSettingsForm.TimerLabel(Form.TurnTimerMinSeconds));
        minTimer.Chosen += label =>
        {
            Form.TurnTimerMinSeconds = HostSettingsForm.MinTimerChoices.FirstOrDefault(it => HostSettingsForm.TimerLabel(it) == label);
            Change(null);
        };
        rows["Shortest turn"] = minTimer;
        networkPage.AddChild(minTimer);
        PasswordField = AddField(networkPage, "Password (empty for an open game)", "PasswordField", Form.Password, "none", value => Form.Password = value);
        PortField = AddField(networkPage, "Port (TCP)", "PortField", Form.Port.ToString(CultureInfo.InvariantCulture), HostSettingsForm.DefaultPort.ToString(CultureInfo.InvariantCulture), value =>
        {
            Form.Port = HostSettingsForm.TryParsePort(value, out int port, out _) ? port : -1;
        });
        PublicAddressField = AddField(networkPage, "Public address or name (optional, used in the invite)", "PublicAddressField", Form.PublicAddress, "e.g. 203.0.113.7", value => Form.PublicAddress = value);

        AddHeading(networkPage, "Addresses of this computer");
        if (Addresses.Count == 0)
        {
            networkPage.AddChild(new Label { Text = "No network address found.".Tr() });
        }
        else
        {
            var row = new ChoiceRow(Addresses.Select(it => it.Address), Addresses[selectedAddress].Address) { Name = "AddressRow" };
            row.Chosen += address =>
            {
                selectedAddress = Math.Max(0, Addresses.ToList().FindIndex(it => it.Address == address));
                Change(null);
            };
            networkPage.AddChild(row);
        }
        networkPage.AddChild(new Label
        {
            Name = "AddressHint",
            Text = AddressNoteText(Form.Port is >= 1 and <= 65535 ? Form.Port : HostSettingsForm.DefaultPort),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(theme.Px(420), 0),
        });
    }

    private void FillSaved()
    {
        Clear(savedPage);
        resumeButtons.Clear();
        IReadOnlyList<HostedGameInfo> games;
        try
        {
            games = HostedGames.List(env.HostedGamesDirectory, Identity().PlayerKey);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            savedPage.AddChild(new Label { Text = $"{"Saved games cannot be read".TrText()}: {e.Message}" });
            return;
        }
        if (games.Count == 0)
        {
            savedPage.AddChild(new Label { Text = "No saved hosted games yet. A hosted game is saved when you stop hosting.".Tr() });
            return;
        }
        AddHeading(savedPage, "Resume a hosted game (it listens on the port from the Network tab)");
        foreach (var game in games)
        {
            var line = new HBoxContainer { Name = "Saved" + (resumeButtons.Count + 1).ToString(CultureInfo.InvariantCulture) };
            var info = new Label
            {
                Text = $"{string.Join(", ", game.Players)}  ({game.LastWrittenUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC)",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            var resume = new Button { Text = "Resume".Tr(), Disabled = !game.HostBound };
            if (!game.HostBound) resume.TooltipText = "Another player hosted this game; only they can resume it.".Tr();
            var captured = game;
            resume.Pressed += () => _ = ResumeAsync(captured);
            line.AddChild(info);
            line.AddChild(resume);
            savedPage.AddChild(line);
            resumeButtons.Add(resume);
        }
    }
}
