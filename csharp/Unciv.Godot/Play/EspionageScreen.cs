using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.Queries.Empire;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C051, redesigned in C074: the espionage screen (Kotlin <c>EspionageScreen</c>) in a <see cref="ScreenFrame"/>. Above, a <see cref="DataTable"/> of the viewer's spies (rank, location, action) whose
/// trailing cell holds the row's Move button and, when the engine allows a coup, a coup button ("Coup 35%", the whole sentence as its tooltip); below, a table of the places a spy can be (the
/// hideout, the cities the engine offers, with the viewer's spies in them). Pressing Move on a spy turns the places it may go to into "Move here" buttons (the button now says Cancel); "Assign spies"
/// in the footer asks the owner to open the multi-spy picker (<see cref="DecisionPickerModel.MoveSpies"/>). Every list is <see cref="EspionageModel"/> over the engine's
/// <see cref="SpyDestinationsQuery"/> answer, which only ever describes the viewer's own spies. The screen's only change to the world is the spy command it sends through the <see cref="IGameSession"/>.
/// </summary>
public partial class EspionageScreen : PanelContainer
{
    private readonly IGameSession session;
    private readonly GameQueries queries;
    private readonly UiTheme theme;
    private readonly Dictionary<string, Button> moveButtons = [];
    private readonly Dictionary<string, Button> coupButtons = [];
    private readonly Dictionary<string, Button> hereButtons = [];
    private ScreenFrame frame = null!; // JUSTIFY: C074 assigned by BuildFrame in the constructor
    private Label message = null!; // JUSTIFY: C074 assigned by BuildFrame in the constructor
    private Button assign = null!; // JUSTIFY: C074 assigned by BuildFrame in the constructor
    private ScrollContainer scroll = null!; // JUSTIFY: C074 assigned by BuildFrame in the constructor
    private VBoxContainer body = null!; // JUSTIFY: C074 assigned by BuildFrame in the constructor
    private SpyDestinationsResult? result;
    private EspionageSpec? spec;
    private string? selectedSpy;
    private string? notice;
    private string messageText = "";
    private int generation;

    public EspionageScreen(IGameSession gameSession, UiTheme uiTheme)
    {
        session = gameSession;
        queries = new GameQueries(gameSession);
        theme = uiTheme;
        Name = "EspionagePanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        Theme = uiTheme.Theme;
        BuildFrame();
    }

    public override void _EnterTree() => theme.LayoutChanged += OnLayoutChanged;

    public override void _ExitTree() => theme.LayoutChanged -= OnLayoutChanged;

    /// <summary>Sizes are fixed when a view is built, so a change of scale or breakpoint builds the frame again and redraws the answer it has.</summary>
    private void OnLayoutChanged()
    {
        BuildFrame();
        if (Visible) Rebuild();
    }

    private void BuildFrame()
    {
        if (frame != null)
        {
            RemoveChild(frame); // at once: a queued free would leave the old frame in the layout until the frame ends
            frame.QueueFree();
        }
        frame = new ScreenFrame(theme, "Espionage", closeLabel: "Close espionage");
        frame.CloseRequested += Close;
        AddChild(frame);
        assign = new Button { Name = "EspionageAssign", Text = "Assign spies".Tr(), Disabled = spec == null || !spec.CanAssign };
        assign.Pressed += () => AssignRequested?.Invoke();
        frame.SetFooter([(assign, ChoiceRole.Primary)]);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        message = new Label { Name = "Message", ThemeTypeVariation = UiVariants.LabelSecondary, AutowrapMode = TextServer.AutowrapMode.WordSmart, Text = messageText, Visible = messageText.Length > 0 };
        column.AddChild(message);
        scroll = new ScrollContainer { Name = "EspionageScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, SizeFlagsVertical = SizeFlags.ExpandFill };
        body = new VBoxContainer { Name = "EspionageBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", theme.Px(UiTokens.S3));
        scroll.AddChild(body);
        column.AddChild(scroll);
        frame.SetContent(column);
    }

    /// <summary>The spy whose Move button was pressed (the place buttons then say "Move here"), or null.</summary>
    public string? SelectedSpy => selectedSpy;

    /// <summary>The page as drawn, null while closed or when the game has no espionage.</summary>
    public EspionageSpec? Spec => spec;

    /// <summary>The text shown instead of the tables (espionage off), or null.</summary>
    public string? Notice => notice;

    public string MessageText => messageText;

    /// <summary>The frame of the screen (title, close button, footer).</summary>
    public ScreenFrame Frame => frame;

    public Button AssignButton => assign;

    /// <summary>Scrolls the tables so the control is inside the visible part (a click needs it there).</summary>
    public void EnsureVisible(Control control) => scroll.EnsureControlVisible(control);

    public Button? MoveButton(string spy) => moveButtons.GetValueOrDefault(spy);

    public Button? CoupButton(string spy) => coupButtons.GetValueOrDefault(spy);

    /// <summary>The "Move here" button of a place (the city id, or empty for the hideout), present only while a spy is selected and the engine offers it that place.</summary>
    public Button? HereButton(string cityId) => hereButtons.GetValueOrDefault(cityId);

    /// <summary>Raised when "Assign spies" was pressed: the owner shows <see cref="DecisionPickerModel.MoveSpies"/>.</summary>
    public event Action? AssignRequested;

    /// <summary>Raised after the engine accepted a command made here, so the owner refreshes the world.</summary>
    public event Action? CommandAccepted;

    public event Action? Closed;

    public async Task OpenAsync()
    {
        Visible = true;
        selectedSpy = null;
        await RefreshAsync();
    }

    public void Close()
    {
        generation++;
        Visible = false;
        Closed?.Invoke();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Asks the engine again and redraws.</summary>
    public async Task RefreshAsync()
    {
        int ticket = ++generation;
        var answer = await queries.SpyDestinations();
        if (ticket != generation) return; // closed or refreshed meanwhile
        result = answer;
        spec = answer == null ? null : EspionageModel.Build(answer);
        if (selectedSpy != null && (answer == null || EspionageModel.Targets(answer, selectedSpy).Count == 0)) selectedSpy = null;
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var child in body.GetChildren())
        {
            body.RemoveChild(child);
            child.QueueFree();
        }
        moveButtons.Clear();
        coupButtons.Clear();
        hereButtons.Clear();
        notice = null;
        if (spec == null || result == null)
        {
            notice = "Espionage is not enabled in this game.";
            body.AddChild(new EmptyState(theme, null, notice));
            assign.Disabled = true;
            return;
        }
        assign.Disabled = !spec.CanAssign;
        SetMessage(selectedSpy == null ? "" : $"Choose where {selectedSpy} goes.".TrText());
        body.AddChild(BuildSpies(spec));
        body.AddChild(BuildLocations(spec, result));
    }

    private Control BuildSpies(EspionageSpec page)
    {
        var box = new VBoxContainer { Name = "SpyTable" };
        box.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        box.AddChild(new SectionHeader("Spies"));
        if (page.Empty.Length > 0)
        {
            box.AddChild(new EmptyState(theme, null, page.Empty));
            return box;
        }
        var table = new DataTable([new DataColumn("Spy"), new DataColumn("Spy rank", true), new DataColumn("Location"), new DataColumn("Action")], actions: true) { Name = "SpyData" };
        table.SetRows(page.Spies.Select(it => (IReadOnlyList<string>)[it.Name, it.Rank, it.Location.TrText(), it.Action.TrText()]).ToList(), index =>
        {
            var spy = page.Spies[index];
            var actions = new HBoxContainer { Name = $"SpyActions_{spy.Name}" };
            actions.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
            var move = new Button { Name = $"Move_{spy.Name}", Text = (selectedSpy == spy.Name ? "Cancel" : "Move").Tr(), Disabled = !spy.CanMove };
            string name = spy.Name;
            move.Pressed += () =>
            {
                selectedSpy = selectedSpy == name ? null : name;
                Rebuild();
            };
            moveButtons[spy.Name] = move;
            actions.AddChild(move);
            if (spy.Coup is { } coup)
            {
                var button = OverviewScreen.CoupButton(theme, spy.Name, spy.CoupShort, spy.CoupLabel, danger: coup.Stage);
                button.Name = $"Coup_{spy.Name}";
                button.Pressed += () => Fire(Send(coup));
                coupButtons[spy.Name] = button;
                actions.AddChild(button);
            }
            return actions;
        });
        box.AddChild(table);
        return box;
    }

    private Control BuildLocations(EspionageSpec page, SpyDestinationsResult answer)
    {
        var box = new VBoxContainer { Name = "LocationTable" };
        box.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        box.AddChild(new SectionHeader("Places"));
        var targets = selectedSpy == null ? [] : EspionageModel.Targets(answer, selectedSpy);
        var table = new DataTable([new DataColumn("City"), new DataColumn("Civilization"), new DataColumn("Spy present")], actions: true) { Name = "LocationData" };
        table.SetRows(page.Locations.Select(it => (IReadOnlyList<string>)[it.Name.TrText(), it.Civ.TrText(), it.SpiesPresent]).ToList(), index =>
        {
            var location = page.Locations[index];
            if (EspionageModel.TargetFor(targets, location) is not { } target) return null;
            var button = new Button { Name = "Here_" + (location.CityId ?? ""), Text = "Move here".Tr() };
            button.Pressed += () =>
            {
                selectedSpy = null;
                Fire(Send(target.Command));
            };
            hereButtons[location.CityId ?? ""] = button;
            return button;
        });
        box.AddChild(table);
        return box;
    }

    private async Task Send(GameCommand command)
    {
        var sent = await session.SubmitAsync(command);
        if (!sent.Accepted)
        {
            await RefreshAsync();
            SetMessage("Refused: ".Tr() + sent.Message);
            return;
        }
        CommandAccepted?.Invoke();
        await RefreshAsync();
    }

    private async void Fire(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            GD.PushError("Session call failed: " + e);
            SetMessage("Connection problem: " + e.Message);
        }
    }

    private void SetMessage(string text)
    {
        messageText = text;
        message.Text = text;
        message.Visible = text.Length > 0;
    }
}
