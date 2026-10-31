using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Ui;
using TabBar = Unciv.GodotClient.Ui.TabBar;
using Unciv.Models.Translations;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Setup;

/// <summary>
/// C015: the new-game screen. Tabs for Players (slots with human/AI toggle, a nation grid in civ colours, player and city-state counts), Game (difficulty, speed, starting era,
/// victory types), Map (type, shape, size, resources, world wrap, seed, ruins and wonders), Rules (base ruleset and mods) and Advanced (barbarians, one-city challenge, nukes ...).
/// It only edits a <see cref="NewGameForm"/>, shows what is wrong with it, and on Start asks <c>start</c> for a session; the engine work is behind
/// <see cref="LocalHost.Start"/>. The form is a separate component so the host lobby (C034) can reuse it.
/// </summary>
public partial class NewGameScreen : Control
{
    private readonly UiTheme theme;
    private readonly Func<NewGameForm, StartOutcome> start;
    private readonly VBoxContainer playersPage = new() { Name = "PlayersPage" };
    private readonly VBoxContainer gamePage = new() { Name = "GamePage" };
    private readonly VBoxContainer mapPage = new() { Name = "MapPage" };
    private readonly VBoxContainer rulesPage = new() { Name = "RulesPage" };
    private readonly VBoxContainer advancedPage = new() { Name = "AdvancedPage" };
    private readonly Label messageLabel = new() { Name = "SetupMessage", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label summaryLabel = new() { Name = "SetupSummary", ThemeTypeVariation = UiVariants.LabelSecondary, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
    private readonly ScreenFrame frame;
    private readonly Dictionary<string, Button> nationButtons = [];
    private readonly List<Button> slotNationButtons = [];
    private readonly List<Button> slotHumanButtons = [];
    private readonly Dictionary<string, ChoiceRow> rows = [];
    private LineEdit seedField = null!; // JUSTIFY: C015 assigned by FillMap, which the constructor runs
    private int selectedSlot;
    private bool starting;

    public NewGameScreen(UiTheme theme, NewGameForm form, Func<NewGameForm, StartOutcome> start)
    {
        this.theme = theme;
        this.start = start;
        Form = form;
        Name = "NewGameScreen";
        var backdrop = new ColorRect { Name = "Backdrop", Color = ProceduralAssets.ToColor(UiTokens.Bg1), MouseFilter = MouseFilterEnum.Ignore };
        backdrop.Fill();
        AddChild(backdrop);
        frame = new ScreenFrame(theme, "New game", closeButton: false);
        frame.Fill();
        AddChild(frame);
        var column = new VBoxContainer();
        column.FitConstant(theme, "separation", UiTokens.S2);
        Tabs = new TabBar { SizeFlagsVertical = SizeFlags.ExpandFill };
        foreach (var (title, page) in new[] { ("Players", playersPage), ("Game", gamePage), ("Map", mapPage), ("Rules", rulesPage), ("Advanced", advancedPage) })
        {
            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            page.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            page.FitConstant(theme, "separation", UiTokens.S2);
            scroll.AddChild(page);
            Tabs.AddTab(title, scroll);
        }
        column.AddChild(Tabs);
        column.AddChild(messageLabel);
        column.AddChild(summaryLabel);
        frame.SetContent(column);
        StartButton = new Button { Text = "Start game".Tr(), Name = "StartButton" };
        StartButton.Pressed += () => _ = StartAsync();
        BackButton = new Button { Text = "Back".Tr(), Name = "BackButton" };
        BackButton.Pressed += () => BackRequested?.Invoke();
        frame.SetFooter([(BackButton, ChoiceRole.Ghost), (StartButton, ChoiceRole.Primary)]);
        Refresh();
    }

    public NewGameForm Form { get; }

    public TabBar Tabs { get; }

    public Button StartButton { get; }

    /// <summary>The footer summary of the choices (players, city-states, difficulty, speed, map).</summary>
    public string Summary => summaryLabel.Text;

    public Button BackButton { get; }

    /// <summary>Raised with the new game's session after a successful Start.</summary>
    public event Action<LocalHost>? Started;

    public event Action? BackRequested;

    /// <summary>What the screen currently tells the player: the form's problems, or the reasons Start failed.</summary>
    public string Message => messageLabel.Text;

    public LineEdit SeedField => seedField;

    public int SelectedSlot => selectedSlot;

    public Button? SlotNationButton(int slot) => slot >= 0 && slot < slotNationButtons.Count ? slotNationButtons[slot] : null;

    public Button? SlotHumanButton(int slot) => slot >= 0 && slot < slotHumanButtons.Count ? slotHumanButtons[slot] : null;

    /// <summary>The nation grid button of a nation (or "Random").</summary>
    public Button? NationButton(string nation) => nationButtons.GetValueOrDefault(nation);

    /// <summary>The option button of a choice row: field is "Difficulty", "Speed", "Era", "Type", "Shape", "Size", "Resources" or "Base ruleset".</summary>
    public Button? ChoiceButton(string field, string option) => rows.GetValueOrDefault(field)?.ButtonFor(option);

    /// <summary>Scrolls the current tab so the control is in view.</summary>
    public void Reveal(Control control)
    {
        for (var node = control.GetParent(); node != null; node = node.GetParent())
        {
            if (node is not ScrollContainer scroll) continue;
            scroll.EnsureControlVisible(control);
            return;
        }
    }

    private void SetMessage(string text, bool isError)
    {
        messageLabel.Text = text.TrText();
        messageLabel.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(isError ? PlaceholderPalette.ErrorText : PlaceholderPalette.Text));
        messageLabel.Visible = text.Length > 0;
    }

    // Rebuilds every page from the form (cheap: well under a hundred buttons) and shows the form's problems.
    public void Refresh()
    {
        FillPlayers();
        FillGame();
        FillMap();
        FillRules();
        FillAdvanced();
        ShowProblems();
    }

    private void ShowProblems()
    {
        summaryLabel.Text = string.Join(", ", $"{Form.Slots.Count.ToString(CultureInfo.InvariantCulture)} {"players".Tr()}", $"{Form.CityStates.ToString(CultureInfo.InvariantCulture)} {"city-states".Tr()}",
            Form.Difficulty.TrText(), Form.Speed.TrText(), $"{Form.MapType.TrText()} {Form.Size.TrText()}");
        var problems = Form.Problems();
        SetMessage(string.Join("\n", problems), problems.Count > 0);
    }

    private async Task StartAsync()
    {
        if (starting) return;
        starting = true;
        StartButton.Disabled = true;
        SetMessage("Generating the map ...", false);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); // let the text show before the (synchronous) generation
        try
        {
            var outcome = start(Form);
            if (outcome.Host is { } host)
            {
                Started?.Invoke(host);
                return;
            }
            SetMessage(string.Join("\n", outcome.Errors), true);
        }
        finally
        {
            starting = false;
            StartButton.Disabled = false;
        }
    }

    // Pages

    private static void Clear(Node page)
    {
        foreach (var child in page.GetChildren())
        {
            page.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void Change(string? error)
    {
        Refresh();
        if (error != null)
        {
            SetMessage(error, true);
        }
    }

    private static void AddHeading(Container page, string text) => page.AddChild(new SectionHeader(text));

    private void AddChoice(Container page, string field, string heading, IEnumerable<string> options, string selected, Action<string> chosen) =>
        AddCheckedChoice(page, field, heading, options, selected, value =>
        {
            chosen(value);
            return null;
        });

    // chosen returns the reason the choice was refused, or null
    private void AddCheckedChoice(Container page, string field, string heading, IEnumerable<string> options, string selected, Func<string, string?> chosen)
    {
        AddHeading(page, heading);
        var row = new ChoiceRow(options, selected);
        row.Chosen += value => Change(chosen(value));
        rows[field] = row;
        page.AddChild(row);
    }

    private void AddCheck(Container page, string text, bool on, Action<bool> changed) =>
        AddCheckedCheck(page, text, on, value =>
        {
            changed(value);
            return null;
        });

    private void AddCheckedCheck(Container page, string text, bool on, Func<bool, string?> changed)
    {
        var box = new CheckBox { Text = text.TrText(), ButtonPressed = on };
        box.Toggled += value => Change(changed(value));
        page.AddChild(box);
    }

    private void FillPlayers()
    {
        Clear(playersPage);
        nationButtons.Clear();
        slotNationButtons.Clear();
        slotHumanButtons.Clear();
        selectedSlot = Math.Clamp(selectedSlot, 0, Math.Max(0, Form.Slots.Count - 1));

        var counts = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        counts.FitConstant(theme, "h_separation", UiTokens.S6);
        AddStepper(counts, "Players", new Stepper(theme, NewGameForm.MinPlayers, Math.Max(NewGameForm.MinPlayers, Form.MaxPlayers), Form.Slots.Count, decreaseLabel: "Fewer players", increaseLabel: "More players"),
            value => Form.SetPlayerCount(value));
        AddStepper(counts, "City-states", new Stepper(theme, 0, Form.MaxCityStates, Form.CityStates, decreaseLabel: "Fewer city-states", increaseLabel: "More city-states"),
            value => Form.SetCityStates(value));
        playersPage.AddChild(counts);

        var body = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.FitConstant(theme, "separation", UiTokens.S3);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.FitConstant(theme, "separation", UiTokens.S1);
        for (int i = 0; i < Form.Slots.Count; i++)
        {
            int slot = i;
            var line = new HBoxContainer { Name = $"Slot{(i + 1).ToString(CultureInfo.InvariantCulture)}" };
            line.FitConstant(theme, "separation", UiTokens.S2);
            var nation = new Button
            {
                Text = $"{(i + 1).ToString(CultureInfo.InvariantCulture)}. {Form.Slots[i].Nation.TrText()}",
                ToggleMode = true,
                ButtonPressed = i == selectedSlot,
                ThemeTypeVariation = UiVariants.ButtonRow,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            nation.Pressed += () =>
            {
                selectedSlot = slot;
                Refresh();
            };
            var human = new Button { Text = (Form.Slots[i].IsHuman ? "Human" : "AI").TrText(), ThemeTypeVariation = UiVariants.ButtonChip };
            human.Pressed += () => Change(Form.SetHuman(slot, !Form.Slots[slot].IsHuman));
            line.AddChild(Emblem(Form.Slots[i].Nation, 24));
            line.AddChild(nation);
            line.AddChild(human);
            list.AddChild(line);
            slotNationButtons.Add(nation);
            slotHumanButtons.Add(human);
        }
        body.AddChild(list);
        body.AddChild(NationCard(Form.Slots[selectedSlot].Nation));
        playersPage.AddChild(body);

        AddHeading(playersPage, $"Nation for player {(selectedSlot + 1).ToString(CultureInfo.InvariantCulture)}:");
        var grid = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.FitConstant(theme, "h_separation", UiTokens.S2);
        grid.FitConstant(theme, "v_separation", UiTokens.S2);
        playersPage.AddChild(grid);
        foreach (string name in new[] { Constants.Random }.Concat(Form.PlayableNations))
        {
            var button = new Button
            {
                ToggleMode = true,
                ButtonPressed = Form.Slots[selectedSlot].Nation == name,
                ThemeTypeVariation = UiVariants.ButtonRow,
                TooltipText = name.TrText(),
            };
            button.FitMin(theme, 88, 72);
            button.SetMeta(IconButton.LabelMeta, name);
            var cell = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
            cell.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            cell.FitConstant(theme, "separation", UiTokens.S1);
            var emblem = Emblem(name, 36);
            emblem.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            cell.AddChild(emblem);
            cell.AddChild(new Label
            {
                Text = name.TrText(), ThemeTypeVariation = UiVariants.LabelSmall, HorizontalAlignment = HorizontalAlignment.Center,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore,
            });
            button.AddChild(cell);
            bool taken = name != Constants.Random && Form.Slots.Select((s, index) => (s, index)).Any(it => it.s.Nation == name && it.index != selectedSlot);
            button.Disabled = taken;
            button.Pressed += () => Change(Form.SetNation(selectedSlot, name));
            nationButtons[name] = button;
            grid.AddChild(button);
        }
    }

    private void AddStepper(Container parent, string label, Stepper stepper, Func<int, string?> changed)
    {
        var row = new HBoxContainer();
        row.FitConstant(theme, "separation", UiTokens.S2);
        row.AddChild(new Label { Text = label.TrText(), ThemeTypeVariation = UiVariants.LabelSecondary, VerticalAlignment = VerticalAlignment.Center });
        row.AddChild(stepper);
        stepper.Changed += value => Change(changed(value));
        parent.AddChild(row);
    }

    // A nation's emblem: its icon on a disc in the nation's colours (the neutral colours for Random).
    private IconBadge Emblem(string nation, int size)
    {
        var colours = Form.NationColours(nation);
        var fill = ProceduralAssets.ToColor(colours?.Background ?? UiTokens.Surface3);
        var glyph = ProceduralAssets.ToColor(colours?.Text ?? UiTokens.TextPrimary);
        return new IconBadge(theme, AssetKey.Of(RulesetAssetKeys.NationIcon, nation), fill, glyph, size);
    }

    // The detail pane of the selected slot's nation: emblem and name, leader, unique ability and start preference.
    private PanelContainer NationCard(string nation)
    {
        var card = new PanelContainer { Name = "NationDetail", ThemeTypeVariation = UiVariants.PanelCard };
        theme.Fit(card, () => card.CustomMinimumSize = new Vector2(theme.Px(theme.Breakpoint == UiBreakpoint.Compact ? 170 : 260), 0f));
        var box = new VBoxContainer();
        box.FitConstant(theme, "separation", UiTokens.S2);
        var head = new HBoxContainer();
        head.FitConstant(theme, "separation", UiTokens.S2);
        head.AddChild(Emblem(nation, 40));
        head.AddChild(new Label { Text = nation.TrText(), ThemeTypeVariation = UiVariants.LabelHeading, VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        box.AddChild(head);
        if (Form.DescribeNation(nation) is { } detail)
        {
            if (detail.Leader.Length > 0) box.AddChild(new Label { Text = detail.Leader.TrText(), ThemeTypeVariation = UiVariants.LabelSecondary, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            if (detail.UniqueName.Length > 0) box.AddChild(new Label { Text = detail.UniqueName.TrText(), ThemeTypeVariation = UiVariants.LabelBodyStrong, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            if (detail.UniqueText.Length > 0) box.AddChild(new Label { Text = detail.UniqueText.TrText(), AutowrapMode = TextServer.AutowrapMode.WordSmart });
            if (detail.Prefers.Count > 0) box.AddChild(new Label { Text = $"{"Starts near".Tr()}: {string.Join(", ", detail.Prefers.Select(it => it.TrText()))}", ThemeTypeVariation = UiVariants.LabelSmall, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            if (detail.Avoids.Count > 0) box.AddChild(new Label { Text = $"{"Avoids".Tr()}: {string.Join(", ", detail.Avoids.Select(it => it.TrText()))}", ThemeTypeVariation = UiVariants.LabelSmall, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        }
        else
        {
            box.AddChild(new Label { Text = "A nation is picked at random when the game starts.".Tr(), ThemeTypeVariation = UiVariants.LabelSecondary, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        }
        card.AddChild(box);
        return card;
    }

    private void FillGame()
    {
        Clear(gamePage);
        AddChoice(gamePage, "Difficulty", "Difficulty", Form.Options.Difficulties.Select(it => it.Name), Form.Difficulty, v => Form.Difficulty = v);
        AddChoice(gamePage, "Speed", "Game speed", Form.Options.Speeds.Select(it => it.Name), Form.Speed, v => Form.Speed = v);
        AddChoice(gamePage, "Era", "Starting era", Form.Options.Eras.Select(it => it.Name), Form.Era, v => Form.Era = v);
        AddHeading(gamePage, "Victory types");
        var victories = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        gamePage.AddChild(victories);
        foreach (string victory in Form.SelectableVictories)
        {
            string name = victory;
            AddCheckedCheck(victories, name, Form.Victories.Contains(name), on => Form.SetVictory(name, on));
        }
    }

    private void FillMap()
    {
        Clear(mapPage);
        var o = Form.Options.MapOptions;
        AddChoice(mapPage, "Type", "Map type", o.Types, Form.MapType, v => Form.MapType = v);
        AddChoice(mapPage, "Shape", "Map shape", o.Shapes, Form.Shape, v => Form.Shape = v);
        AddChoice(mapPage, "Size", "Map size", o.Sizes, Form.Size, v => Form.Size = v);
        AddChoice(mapPage, "Resources", "Resources", o.Resources, Form.Resources, v => Form.Resources = v);
        var flags = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mapPage.AddChild(flags);
        AddCheck(flags, "World wrap", Form.WorldWrap, on => Form.WorldWrap = on);
        AddCheck(flags, "No ancient ruins", Form.NoRuins, on => Form.NoRuins = on);
        AddCheck(flags, "No natural wonders", Form.NoNaturalWonders, on => Form.NoNaturalWonders = on);
        AddHeading(mapPage, "Seed (empty for a random map)");
                seedField = new LineEdit { Name = "SeedField", Text = Form.SeedText, PlaceholderText = "random".Tr() }.FitMin(theme, 240);
        seedField.TextChanged += text =>
        {
            Form.SeedText = text;
            ShowProblems();
        };
        mapPage.AddChild(seedField);
    }

    private void FillRules()
    {
        Clear(rulesPage);
        AddCheckedChoice(rulesPage, "Base ruleset", "Base ruleset", Form.BaseRulesets, Form.BaseRuleset, Form.SetBaseRuleset);
        AddHeading(rulesPage, Form.AvailableMods.Count == 0 ? "No mods installed" : "Mods");
        foreach (string mod in Form.AvailableMods)
        {
            string name = mod;
            AddCheckedCheck(rulesPage, name, Form.Mods.Contains(name), on => Form.SetMod(name, on));
        }
    }

    private void FillAdvanced()
    {
        Clear(advancedPage);
        AddCheck(advancedPage, "No barbarians", Form.NoBarbarians, on => Form.NoBarbarians = on);
        AddCheck(advancedPage, "Raging barbarians", Form.RagingBarbarians, on => Form.RagingBarbarians = on);
        AddCheck(advancedPage, "One city challenge", Form.OneCityChallenge, on => Form.OneCityChallenge = on);
        AddCheck(advancedPage, "No city razing", Form.NoCityRazing, on => Form.NoCityRazing = on);
        AddCheck(advancedPage, "Nuclear weapons", Form.NuclearWeapons, on => Form.NuclearWeapons = on);
        AddCheck(advancedPage, "Espionage", Form.Espionage, on => Form.Espionage = on);
        AddCheck(advancedPage, "God mode (cheats; the developer console needs it)", Form.GodMode, on => Form.GodMode = on);
    }
}
