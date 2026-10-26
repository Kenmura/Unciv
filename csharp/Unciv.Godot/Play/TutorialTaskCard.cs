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
    private readonly VBoxContainer column = new() { Name = "TaskColumn" };
    private readonly Button collapsedButton = new() { Name = "TaskCollapsed", Text = "?", Visible = false };
    private UiTheme theme = null!; // JUSTIFY: C054 assigned by Init before the card is shown
    private TutorialTaskResult? task;

    public TutorialTaskCard()
    {
        Name = "TutorialTaskCard";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        panel.AddChild(column);
        AddChild(panel);
        AddChild(collapsedButton);
        panel.GuiInput += OnPanelInput;
        collapsedButton.Pressed += () => SetCollapsed(false);
    }

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
        if (result == null || result.Event.Length == 0)
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
