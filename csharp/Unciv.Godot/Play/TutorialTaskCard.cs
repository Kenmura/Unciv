using Godot;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.Queries.Tutorial;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C054: the floating card of the current tutorial task (Kotlin TutorialTaskTable): the task's lines and choice buttons from the engine's query, under the
/// top bar. A click on the card folds it to a small "?" button and back (the fold is client-only). It shows what it is given; choosing raises
/// <see cref="ChoiceChosen"/>. Task pictures are not shown (the client has no loader for ruleset extra images yet).
/// </summary>
public partial class TutorialTaskCard : Control
{
    private const int MaxWidth = 340;
    private readonly PanelContainer panel = new() { Name = "TaskPanel", MouseFilter = MouseFilterEnum.Stop };
    private readonly VBoxContainer shell = new() { Name = "TaskShell" };
    private readonly VBoxContainer column = new() { Name = "TaskColumn" };
    private readonly HBoxContainer header = new() { Name = "TaskHeader" };
    private readonly Button closeButton = new() { Name = "TaskClose" };
    private readonly Button neverButton = new() { Name = "TaskNever" };
    private readonly Button collapsedButton = new() { Name = "TaskCollapsed", Text = "?", Visible = false };
    private UiTheme theme = null!; // JUSTIFY: C054 assigned by Init before the card is shown
    private TutorialTaskResult? task;

    public TutorialTaskCard()
    {
        Name = "TutorialTaskCard";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        neverButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        neverButton.ThemeTypeVariation = UiVariants.ButtonGhost;
        neverButton.AutowrapMode = TextServer.AutowrapMode.WordSmart; // a narrow phone card wraps the text instead of growing wider than its budget
        neverButton.CustomMinimumSize = new Vector2(1f, 0f);
        header.AddChild(neverButton);
        header.AddChild(closeButton);
        shell.AddChild(header);
        shell.AddChild(column);
        panel.AddChild(shell);
        AddChild(panel);
        AddChild(collapsedButton);
        panel.GuiInput += OnPanelInput;
        collapsedButton.Pressed += () => SetCollapsed(false);
        closeButton.Pressed += () =>
        {
            Closed = true;
            Show(null);
        };
        neverButton.Pressed += () => NeverShowRequested?.Invoke();
    }

    /// <summary>The X was pressed: the card stays away for the rest of this game screen (the "Never show tutorials" button is the permanent removal).</summary>
    public bool Closed { get; private set; }

    /// <summary>A new game starts: the card may appear again.</summary>
    public void ResetClosed() => Closed = false;

    public Button CloseButton => closeButton;

    public Button NeverButton => neverButton;

    /// <summary>The player chose "Never show tutorials": the owner turns the persisted setting off.</summary>
    public event Action? NeverShowRequested;

    /// <summary>The Event name being shown ("" when the card is hidden).</summary>
    public string EventName => Visible && task != null ? task.Event : "";

    public bool Collapsed { get; private set; }

    /// <summary>Where the open card is on screen (for scenarios that click it).</summary>
    public Rect2 CardRect => panel.GetGlobalRect();

    public Button CollapsedButton => collapsedButton;

    public IReadOnlyList<string> LineTexts => column.GetChildren().OfType<Label>().Select(it => it.Text).ToList();

    public IReadOnlyList<Button> ChoiceButtons => column.GetChildren().OfType<Button>().ToList();

    public event Action<string, int>? ChoiceChosen;

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
    }

    /// <summary>Shows the task (null or an empty Event name hides the card). The fold is kept while the task stays the same.</summary>
    public void Show(TutorialTaskResult? result)
    {
        if (result == null || result.Event.Length == 0 || Closed)
        {
            task = null;
            Visible = false;
            return;
        }
        if (task == null || task.Event != result.Event) Collapsed = false;
        task = result;
        Visible = true;
        Build();
    }

    public void SetCollapsed(bool collapsed)
    {
        Collapsed = collapsed;
        if (task != null) Build();
    }

    /// <summary>Centres the card under <paramref name="top"/> (the HUD's bottom edge) in a screen of <paramref name="screen"/> size.</summary>
    public void LayoutIn(Vector2 screen, float top)
    {
        float width = CardWidth(screen.X);
        panel.CustomMinimumSize = new Vector2(width, 0f);
        panel.Size = new Vector2(width, 0f); // a container never shrinks below its minimum, so this is the content's own height
        panel.Position = new Vector2((screen.X - panel.Size.X) / 2f, top);
        collapsedButton.Size = collapsedButton.GetCombinedMinimumSize();
        collapsedButton.Position = new Vector2((screen.X - collapsedButton.Size.X) / 2f, top);
        Position = Vector2.Zero;
        Size = screen;
    }

    private float CardWidth(float screenWidth) => Math.Min(theme?.Px(MaxWidth) ?? (float)MaxWidth, screenWidth * 0.5f);

    private void Build()
    {
        foreach (var child in column.GetChildren())
        {
            column.RemoveChild(child);
            child.QueueFree();
        }
        neverButton.Text = "Never show tutorials".Tr();
        closeButton.Text = "X".Tr();
        panel.Visible = !Collapsed;
        collapsedButton.Visible = Collapsed;
        if (Collapsed || task == null) return;
        // A wrapping label reports a huge minimum height until it knows its width, so give it one (the panel's width less its padding)
        float labelWidth = CardWidth(GetViewportRect().Size.X) - (theme?.Px(24) ?? 24);
        foreach (var line in task.Lines.Where(it => it.Text.Length != 0))
        {
            column.AddChild(new Label
            {
                CustomMinimumSize = new Vector2(labelWidth, 0f),
                Text = line.Text.TrText(),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = line.Centered ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                MouseFilter = MouseFilterEnum.Pass,
            });
        }
        string eventName = task.Event;
        foreach (var choice in task.Choices)
        {
            int index = choice.Index;
            var button = new Button { Text = choice.Text.Tr(), Name = "TaskChoice" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            button.Pressed += () => ChoiceChosen?.Invoke(eventName, index);
            column.AddChild(button);
        }
    }

    private void OnPanelInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) SetCollapsed(true);
    }
}
