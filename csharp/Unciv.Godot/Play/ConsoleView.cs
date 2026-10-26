using Godot;
using Unciv.Assets;
using Unciv.Commands.DevConsole;
using Unciv.GodotClient.Assets;
using Unciv.Models.Translations;
using Unciv.GodotClient.Ui;
using Unciv.Logic.Map;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C046: the developer console popup (Kotlin <c>DevConsolePopup</c>), a panel under the top bar that does not block the map: a one-line input with history (Up/Down), autocomplete (Tab) and the engine's response shown under it
/// (green: ran; gold: usage or an answer; red: refused). It decides nothing: Enter first asks the engine to plan the line (<see cref="GameQueries.ConsolePlan"/>), and only an
/// OK line is sent, as one <see cref="RunConsoleCommand"/> through the session, so every console edit is in the command log. Completion comes from
/// <see cref="GameQueries.ConsoleAutocomplete"/>. The history and the "Keep open" choice are handed to the owner (<see cref="StateChanged"/>) to persist in the client settings.
/// Deliberate deviations from Kotlin: no on-screen history/autocomplete buttons or unique builder; the word-jump keys are the text input's own.
/// </summary>
public partial class ConsoleView : Control
{
    private readonly PanelContainer panel = new() { Name = "ConsolePanel" };
    private readonly LineEdit input = new() { Name = "ConsoleInput", MaxLength = Unciv.DevConsole.ConsoleRunner.MaxLineLength, KeepEditingOnTextSubmit = true };
    private readonly Label response = new() { Name = "ConsoleResponse", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly CheckBox keepOpenBox = new() { Name = "ConsoleKeepOpen", Text = "Keep open".Tr() };
    private GameQueries? queries;
    private IGameSession? session;
    private Func<(HexCoord? Tile, int? UnitId)> selection = () => (null, null);
    private Action<Task> run = _ => { };
    private List<string> history = [];
    private int position;
    private bool keepOpen;
    private float topMargin = 72f;

    public ConsoleView()
    {
        Name = "Console";
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore; // not modal: a click on the map still selects the tile or unit the next line works on
        AddChild(panel);
        var column = new VBoxContainer();
        panel.AddChild(column);
        var head = new HBoxContainer();
        head.AddChild(new Label { Name = "ConsoleTitle", Text = "Developer Console".Tr(), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        head.AddChild(keepOpenBox);
        column.AddChild(head);
        column.AddChild(input);
        column.AddChild(response);
        input.TextSubmitted += _ => run(EnterAsync());
        input.GuiInput += OnFieldInput;
        keepOpenBox.Toggled += pressed =>
        {
            keepOpen = pressed;
            StateChanged?.Invoke(history, keepOpen);
            input.GrabFocus();
        };
    }

    public LineEdit Input => input;

    public string ResponseText => response.Text;

    public ConsoleTone ResponseTone { get; private set; } = ConsoleTone.Hint;

    public bool KeepOpen => keepOpen;

    public IReadOnlyList<string> History => history;

    /// <summary>Raised when the history or the "Keep open" choice changed, with both.</summary>
    public event Action<IReadOnlyList<string>, bool>? StateChanged;

    public void Init(UiTheme uiTheme)
    {
        Theme = uiTheme.Theme;
        topMargin = uiTheme.Px(72);
    }

    /// <summary>Connects the console to a game: its queries, the session that takes the command, the owner's selection (tile, unit) and the observer of tasks.</summary>
    public void Bind(IGameSession gameSession, GameQueries gameQueries, Func<(HexCoord? Tile, int? UnitId)> selected, Action<Task> observe)
    {
        session = gameSession;
        queries = gameQueries;
        selection = selected;
        run = observe;
    }

    public void LayoutIn(Vector2 screen)
    {
        Size = screen;
        float width = Mathf.Max(260f, screen.X * 0.6f);
        input.CustomMinimumSize = new Vector2(width, 0f);
        response.CustomMinimumSize = new Vector2(width, 0f);
        panel.Position = new Vector2((screen.X - width) / 2f - 8f, topMargin);
    }

    /// <summary>Shows the console with the stored history and the "Keep open" choice; the input is empty and has the keyboard focus.</summary>
    public void Open(IReadOnlyList<string> storedHistory, bool storedKeepOpen)
    {
        history = ConsoleModel.SanitizeHistory(storedHistory);
        position = history.Count;
        keepOpen = storedKeepOpen;
        keepOpenBox.SetPressedNoSignal(keepOpen);
        input.Text = "";
        Show("", ConsoleTone.Hint);
        Visible = true;
        input.GrabFocus();
    }

    public void Close() => Visible = false;

    /// <summary>Escape: closes the console. False when it is not open.</summary>
    public bool Cancel()
    {
        if (!Visible) return false;
        Close();
        return true;
    }

    /// <summary>Up (-1) and Down (+1): puts the older or newer history line into the input, the caret at its end.</summary>
    public void NavigateHistory(int delta)
    {
        int next = ConsoleModel.Step(history.Count, position, delta);
        if (next < 0) return;
        position = next;
        input.Text = history[next];
        input.CaretColumn = input.Text.Length;
    }

    /// <summary>Enter: asks the engine what the line does; an OK line is sent as one logged command, anything else only shows its response.</summary>
    public async Task EnterAsync()
    {
        if (queries == null || session == null) return;
        string line = input.Text.Trim();
        var (tile, unit) = selection();
        var plan = await queries.ConsolePlan(line, tile, unit);
        if (plan == null)
        {
            Show("The game did not answer".Tr(), ConsoleTone.Error);
            return;
        }
        var tone = ConsoleModel.ToneOf(plan.Kind);
        if (tone != ConsoleTone.Ok)
        {
            Show(plan.Message ?? "", tone);
            return;
        }
        var result = await session.SubmitAsync(new RunConsoleCommand { Line = line, Tile = tile, UnitId = unit });
        if (!result.Accepted)
        {
            Show(string.IsNullOrEmpty(result.Message) ? "The game refused the command".Tr() : result.Message, ConsoleTone.Error);
            return;
        }
        history = ConsoleModel.AddHistory(history, line);
        position = history.Count;
        StateChanged?.Invoke(history, keepOpen);
        input.Text = "";
        Show(string.IsNullOrEmpty(plan.Message) ? "Done".Tr() : plan.Message, ConsoleTone.Ok);
        if (!keepOpen) Close();
    }

    /// <summary>Tab: completes the last word of the line from the engine; a list of choices or a refusal is shown as the response.</summary>
    public async Task CompleteAsync()
    {
        if (queries == null) return;
        var (tile, unit) = selection();
        var completion = await queries.ConsoleAutocomplete(input.Text, tile, unit);
        if (completion == null) return;
        if (completion.IsError)
        {
            Show(completion.Message ?? "", ConsoleTone.Error);
            return;
        }
        if (completion.Text != null)
        {
            input.Text = ConsoleModel.ApplyCompletion(input.Text, completion.ReplaceLength, completion.Text);
            input.CaretColumn = input.Text.Length;
        }
        if (completion.Message != null) Show(completion.Message, ConsoleTone.Hint);
    }

    private void OnFieldInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key) return;
        switch (key.Keycode)
        {
            case Key.Tab:
                run(CompleteAsync());
                break;
            case Key.Escape: // the text field would take it for itself; the screen's Escape handling never sees it
                Cancel();
                break;
            case Key.Up:
                NavigateHistory(-1);
                break;
            case Key.Down:
                NavigateHistory(1);
                break;
            default:
                return;
        }
        AcceptEvent();
    }

    // The engine's text is shown as it is (the console stays English, like the Kotlin one).
    private void Show(string text, ConsoleTone tone)
    {
        ResponseTone = tone;
        response.Text = text;
        response.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(ConsoleModel.ColourOf(tone)));
    }
}
