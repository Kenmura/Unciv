using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.Queries.Empire;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C051: the espionage screen (Kotlin <c>EspionageScreen</c>). Left, the viewer's spies with rank, location, action, a Move button and, when the engine allows a coup, a coup
/// button; right, the places a spy can be (the hideout, the cities the engine offers, with the viewer's spies in them). Pressing Move on a spy turns the places it may go to
/// into "Move here" buttons (the button now says Cancel); "Assign spies" asks the owner to open the multi-spy picker (<see cref="DecisionPickerModel.MoveSpies"/>). Every list is
/// <see cref="EspionageModel"/> over the engine's <see cref="SpyDestinationsQuery"/> answer, which only ever describes the viewer's own spies. The screen's only change to
/// the world is the spy command it sends through the <see cref="IGameSession"/>.
/// </summary>
public partial class EspionageScreen : PanelContainer
{
    private readonly IGameSession session;
    private readonly GameQueries queries;
    private readonly Label message = new() { Name = "Message" };
    private readonly Button assign = new() { Name = "EspionageAssign" };
    private readonly VBoxContainer body = new() { Name = "EspionageBody", SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Dictionary<string, Button> moveButtons = [];
    private readonly Dictionary<string, Button> coupButtons = [];
    private readonly Dictionary<string, Button> hereButtons = [];
    private SpyDestinationsResult? result;
    private EspionageSpec? spec;
    private string? selectedSpy;
    private string? notice;
    private int generation;

    public EspionageScreen(IGameSession gameSession, UiTheme uiTheme)
    {
        session = gameSession;
        queries = new GameQueries(gameSession);
        Name = "EspionagePanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        Theme = uiTheme.Theme;

        var column = new VBoxContainer();
        AddChild(column);
        var head = new HBoxContainer();
        column.AddChild(head);
        var heading = new Label { Text = "Espionage".Tr(), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddThemeFontSizeOverride("font_size", uiTheme.Px(20));
        head.AddChild(heading);
        assign.Text = "Assign spies".Tr();
        assign.Pressed += () => AssignRequested?.Invoke();
        head.AddChild(assign);
        var close = new Button { Name = "EspionageClose", Text = "Close espionage".Tr() };
        close.Pressed += Close;
        head.AddChild(close);
        message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(message);
        var scroll = new ScrollContainer { Name = "EspionageScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(body);
        column.AddChild(scroll);
    }

    /// <summary>The spy whose Move button was pressed (the place buttons then say "Move here"), or null.</summary>
    public string? SelectedSpy => selectedSpy;

    /// <summary>The page as drawn, null while closed or when the game has no espionage.</summary>
    public EspionageSpec? Spec => spec;

    /// <summary>The text shown instead of the tables (espionage off), or null.</summary>
    public string? Notice => notice;

    public string MessageText => message.Text;

    public Button AssignButton => assign;

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
            body.AddChild(new Label { Text = notice.Tr() });
            assign.Disabled = true;
            return;
        }
        assign.Disabled = !spec.CanAssign;
        SetMessage(selectedSpy == null ? "" : $"Choose where {selectedSpy} goes.".TrText());
        var panes = new VBoxContainer { Name = "EspionagePanes" };
        panes.AddThemeConstantOverride("separation", 16);
        body.AddChild(panes);
        panes.AddChild(BuildSpies(spec));
        panes.AddChild(BuildLocations(spec, result));
    }

    private Control BuildSpies(EspionageSpec page)
    {
        var box = new VBoxContainer { Name = "SpyTable" };
        if (page.Empty.Length > 0)
        {
            box.AddChild(new Label { Text = page.Empty.Tr() });
            return box;
        }
        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 14);
        box.AddChild(grid);
        foreach (string heading in new[] { "Spy", "Spy rank", "Location", "Action", "", "" }) grid.AddChild(new Label { Text = heading.Tr() });
        foreach (var spy in page.Spies)
        {
            grid.AddChild(new Label { Text = spy.Name });
            grid.AddChild(new Label { Text = spy.Rank });
            grid.AddChild(new Label { Text = spy.Location.TrText() });
            grid.AddChild(new Label { Text = spy.Action.TrText() });
            var move = new Button { Name = $"Move_{spy.Name}", Text = (selectedSpy == spy.Name ? "Cancel" : "Move").Tr(), Disabled = !spy.CanMove };
            string name = spy.Name;
            move.Pressed += () =>
            {
                selectedSpy = selectedSpy == name ? null : name;
                Rebuild();
            };
            moveButtons[spy.Name] = move;
            grid.AddChild(move);
            if (spy.Coup is { } coup)
            {
                var button = new Button { Name = $"Coup_{spy.Name}", Text = spy.CoupLabel.TrText() };
                button.Pressed += () => Fire(Send(coup));
                coupButtons[spy.Name] = button;
                grid.AddChild(button);
            }
            else grid.AddChild(new Control());
        }
        return box;
    }

    private Control BuildLocations(EspionageSpec page, SpyDestinationsResult answer)
    {
        var box = new VBoxContainer { Name = "LocationTable" };
        var targets = selectedSpy == null ? [] : EspionageModel.Targets(answer, selectedSpy);
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 14);
        box.AddChild(grid);
        foreach (string heading in new[] { "City", "Civilization", "Spy present", "" }) grid.AddChild(new Label { Text = heading.Tr() });
        foreach (var location in page.Locations)
        {
            grid.AddChild(new Label { Text = location.Name.TrText() });
            grid.AddChild(new Label { Text = location.Civ.TrText() });
            grid.AddChild(new Label { Text = location.SpiesPresent });
            if (EspionageModel.TargetFor(targets, location) is { } target)
            {
                var button = new Button { Name = "Here_" + (location.CityId ?? ""), Text = "Move here".Tr() };
                button.Pressed += () =>
                {
                    selectedSpy = null;
                    Fire(Send(target.Command));
                };
                hereButtons[location.CityId ?? ""] = button;
                grid.AddChild(button);
            }
            else grid.AddChild(new Control());
        }
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

    private void SetMessage(string text) => message.Text = text;
}
